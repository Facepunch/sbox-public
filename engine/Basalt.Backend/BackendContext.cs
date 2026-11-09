using Basalt.Culling;
using Basalt.Gpu;
using System.Runtime.InteropServices;

namespace Basalt.Backend;

/// <summary>
/// Basalt's recording context on a <see cref="CommandList"/>. Basalt binds targets and leaves the rest to the device, as
/// Source 2's render contexts do, so this opens rendering scopes lazily at the first draw or clear, closes them before
/// anything that can't happen inside one, and moves textures between layouts as they're used, from the layout each
/// texture remembers.
/// </summary>
/// <remarks>
/// Layouts are tracked per texture as work is recorded, which is right while recording order is submission order:
/// serial recording on one queue. Parallel segments and async compute need per-segment tracking with fix-ups at
/// submission; until then the backend's host turns both off.
/// </remarks>
internal sealed class BackendContext : GpuContext
{
	/// <summary>
	/// What a command list is in the middle of. Segments recording into their frame's list share it.
	/// </summary>
	sealed class Session
	{
		public CommandList List;
		public bool Rendering;
		public BackendTexture Color, Depth;
		public int Slice;
		public bool Srgb;
		public PipelineHandle Pipeline;
		public BackendContext Binder;
		public int BindVersion = -1;
		public readonly List<(BufferHandle Buffer, int Bytes, GpuReadback Callback)> Readbacks = new();
	}

	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );
	static readonly StringToken TransformBufferName = new( "g_TransformBuffer" );
	static readonly StringToken DirectionalLightName = new( "DirectionalLightCB" );
	static readonly StringToken LightingConfigName = new( "ViewLightingConfigV2" );
	static readonly StringToken LightBufferName = new( "BinnedLightBufferV2" );
	static readonly StringToken DepthMode = new( "Depth" );
	static readonly StringToken ForwardMode = new( "Forward" );
	static readonly StringToken FrameBufferCopyTexture = new( "FrameBufferCopyTexture" );
	static readonly StringToken FrameBufferCopyRectangle = new( "FrameBufferCopyRectangle" );

	readonly BackendDevice device;
	BackendContext frame;
	readonly BackendParameters attributes = new();
	Session recording;
	bool borrowed;
	GpuSemaphore wait, signal;

	// What's bound, which a rendering scope opens on
	StageTarget bound;
	bool boundValid;
	bool clearColor, clearDepth;
	Color clearValue;

	public BackendContext( BackendDevice device, BackendContext frame )
	{
		this.device = device;
		this.frame = frame;
	}

	Device Gpu => device.Gpu;

	CommandList List => recording?.List ?? throw new InvalidOperationException( "This context isn't recording" );

	public override bool AsyncCompute { get; set; }

	internal override bool GpuScopesEnabled => true;

	internal override bool IsFrame => frame is null;

	internal override bool IsRecording => recording is not null;

	internal override ShaderParameters Attributes => attributes;

	internal override (int Full, int Similar) MaterialBinds => default;

	internal override StageTarget Bound => bound;

	internal override ViewConstantsKey? ViewConstants => viewConstants;
	ViewConstantsKey? viewConstants;

	public override OverlayStencil OverlayStencil { get; set; }

	public override GpuBuffer<uint> InstanceIds { get; set; }

	public override OcclusionCuller Occlusion { get; set; }

	//
	// Recording and submission
	//

	public override void BeginSegment( GpuContext frame, GpuContext shared )
	{
		if ( this.frame is null || frame is null ) throw new InvalidOperationException( "Only a segment context records a frame's segments" );
		this.frame = (BackendContext)frame;
		Begin( shared );
	}

	public override void Begin( GpuContext shared = null )
	{
		if ( recording is not null ) throw new InvalidOperationException( "Already recording" );

		if ( frame is not null )
		{
			attributes.Clear();
			attributes.Parent = frame.attributes;
			InstanceIds = frame.InstanceIds;
			Occlusion = frame.Occlusion;
		}

		borrowed = shared is not null;
		if ( borrowed )
		{
			recording = ((BackendContext)shared).recording ?? throw new InvalidOperationException( "The shared context isn't recording" );
		}
		else
		{
			var queue = AsyncCompute && Gpu.Capabilities.AsyncCompute ? QueueType.Compute : QueueType.Graphics;
			recording = new Session { List = Gpu.BeginCommandList( queue, frame is null ? "Basalt frame" : "Basalt segment" ) };
		}

		wait = signal = default;
		boundValid = false;
		bound = default;
		clearColor = clearDepth = false;
		viewConstants = null;
		passStateCount = 0;
		OverlayStencil = OverlayStencil.None;
		recording.BindVersion = -1;
	}

	public override void Submit( GpuSwapChain swapChain = default ) => SubmitNext();

	public override void SubmitNext()
	{
		if ( recording is null ) return;

		try
		{
			if ( borrowed )
			{
				// The owner submits; leave the list as a scope boundary for whoever records next
				CloseScope();
				return;
			}

			CloseScope();
			Span<SyncPoint> waits = stackalloc SyncPoint[1];
			var waitCount = 0;
			if ( wait.Handle != IntPtr.Zero ) waits[waitCount++] = device.PointOf( wait );

			var point = Gpu.Submit( recording.List, waits[..waitCount] );
			if ( signal.Handle != IntPtr.Zero ) device.Signalled( signal, point );
			foreach ( var (buffer, bytes, callback) in recording.Readbacks ) device.AddReadback( point, buffer, bytes, callback );
		}
		finally
		{
			End();
		}
	}

	public override void End()
	{
		recording = null;
		borrowed = false;
	}

	public override GpuSemaphore SignalAtEnd()
	{
		if ( signal.Handle != IntPtr.Zero ) throw new InvalidOperationException( "A context signals once" );
		return signal = device.NewSemaphore();
	}

	public override void WaitAtBegin( GpuSemaphore semaphore )
	{
		if ( wait.Handle != IntPtr.Zero ) throw new InvalidOperationException( "A context waits once" );
		wait = semaphore;
	}

	public override void Dispose() { }

	//
	// Targets and rendering scopes
	//

	public override void ResetMaterialBind() => ForceRebind();

	void ForceRebind()
	{
		if ( recording is not null ) recording.BindVersion = -1;
	}

	public override void Bind( in StageTarget target )
	{
		if ( boundValid && target.Color == bound.Color && target.Depth == bound.Depth && target.DepthSlice == bound.DepthSlice && target.SrgbWrite == bound.SrgbWrite )
		{
			bound = target;
			ApplyViewport();
			return;
		}

		FlushClears();
		bound = target;
		boundValid = true;
		if ( recording is { Rendering: true } && !ScopeMatches() ) CloseScope();
		ApplyViewport();
	}

	internal override void RestoreBound()
	{
		Invalidate();
		Bind( bound );
	}

	internal override void Invalidate()
	{
		boundValid = false;
		ForceRebind();
	}

	bool ScopeMatches()
	{
		var r = recording;
		return r.Rendering && r.Color == bound.Color && r.Depth == bound.Depth && r.Slice == bound.DepthSlice && r.Srgb == bound.SrgbWrite;
	}

	/// <summary>
	/// Open a rendering scope on the bound targets, unless one's open on them already: move them to target layouts and
	/// apply pending clears.
	/// </summary>
	bool EnsureScope()
	{
		var r = recording;
		if ( r is null || !boundValid ) return false;
		if ( ScopeMatches() ) return true;
		CloseScope( flushClears: false );

		var color = bound.Color as BackendTexture;
		var depth = bound.Depth as BackendTexture;
		if ( color is null && depth is null ) return false;

		if ( color is not null ) Transition( color, ImageLayout.ColorTarget );
		if ( depth is not null ) Transition( depth, ImageLayout.DepthTarget );

		Span<ColorTarget> colors = stackalloc ColorTarget[1];
		var colorCount = 0;
		if ( color is not null )
		{
			colors[colorCount++] = new ColorTarget
			{
				Texture = color.Handle,
				Srgb = bound.SrgbWrite && BackendDevice.FormatOf( color ).HasSrgb(),
				Load = clearColor ? LoadOp.Clear : LoadOp.Load,
				Store = StoreOp.Store,
				ClearColor = new System.Numerics.Vector4( clearValue.r, clearValue.g, clearValue.b, clearValue.a ),
			};
		}

		var depthTarget = depth is null ? default : new DepthTarget
		{
			Texture = depth.Handle,
			Layer = bound.DepthSlice,
			Load = clearDepth ? LoadOp.Clear : LoadOp.Load,
			Store = StoreOp.Store,
			ClearDepth = 0,
		};

		clearColor = clearDepth = false;
		r.List.BeginRendering( colors[..colorCount], depthTarget );
		r.Rendering = true;
		r.Color = color;
		r.Depth = depth;
		r.Slice = bound.DepthSlice;
		r.Srgb = bound.SrgbWrite;
		r.Pipeline = default;
		r.BindVersion = -1;
		ApplyViewport();
		return true;
	}

	void CloseScope( bool flushClears = true )
	{
		if ( flushClears ) FlushClears();
		if ( recording is { Rendering: true } r )
		{
			r.List.EndRendering();
			r.Rendering = false;
		}
	}

	/// <summary>
	/// A clear nothing has drawn after yet: open and close a scope so it happens.
	/// </summary>
	void FlushClears()
	{
		if ( !clearColor && !clearDepth ) return;
		if ( EnsureScope() ) CloseScope( flushClears: false );
		clearColor = clearDepth = false;
	}

	void ApplyViewport()
	{
		if ( recording is not { Rendering: true } r || !ScopeMatches() ) return;

		var rect = bound.Viewport;
		var (width, height) = r.List.RenderSize;
		if ( rect.Width <= 0 || rect.Height <= 0 ) rect = new Rect( 0, 0, width, height );
		var maxZ = bound.MaxZ > bound.MinZ ? bound.MaxZ : 1;
		r.List.SetViewport( rect.Left, rect.Top, rect.Width, rect.Height, bound.MinZ, maxZ );
		r.List.SetScissor( (int)rect.Left, (int)rect.Top, (int)MathF.Ceiling( rect.Width ), (int)MathF.Ceiling( rect.Height ) );
	}

	public override void SetViewport( Rect rect, float minZ = 0, float maxZ = 1 )
	{
		bound = bound with { Viewport = rect, MinZ = minZ, MaxZ = maxZ };
		ApplyViewport();
	}

	public override void Clear( Color color, bool clearColor = true, bool clearDepth = true )
	{
		if ( recording is null || !boundValid ) return;

		// A scope already open on these targets has drawn: clear by reopening it
		if ( recording.Rendering && ScopeMatches() ) CloseScope( flushClears: false );
		this.clearColor |= clearColor && bound.Color is not null;
		this.clearDepth |= clearDepth && bound.Depth is not null;
		clearValue = color;
	}

	//
	// Layouts: every texture remembers where its last recorded use left it
	//

	void Transition( BackendTexture texture, ImageLayout layout, bool always = false )
	{
		if ( texture is null ) return;
		if ( texture.Layout == layout && !always ) return;
		if ( recording.Rendering ) CloseScope();

		recording.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( texture.Handle, texture.Layout, layout,
			PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite ) );
		texture.Layout = layout;
	}

	static ImageLayout Map( TextureLayout layout, BackendTexture texture ) => layout switch
	{
		TextureLayout.General => ImageLayout.General,
		TextureLayout.ColorAttachment => ImageLayout.ColorTarget,
		TextureLayout.DepthAttachment => ImageLayout.DepthTarget,
		_ => ImageLayout.ShaderRead,
	};

	void Barrier( GpuTexture texture, ImageLayout layout )
	{
		if ( recording is null || texture is not BackendTexture t ) return;
		CloseScope();
		Transition( t, layout, always: true );
	}

	public override void TextureBarrier( GpuTexture texture, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess, TextureLayout layout )
	{
		if ( texture is BackendTexture t ) Barrier( t, Map( layout, t ) );
	}

	public override void BarrierToDepthWrite( GpuTexture depth ) => Barrier( depth, ImageLayout.DepthTarget );
	public override void BarrierToComputeRead( GpuTexture depth ) => Barrier( depth, ImageLayout.ShaderRead );
	public override void BarrierColorToComputeRead( GpuTexture texture ) => Barrier( texture, ImageLayout.ShaderRead );
	public override void BarrierComputeToPixelShaderRead( GpuTexture texture ) => Barrier( texture, ImageLayout.ShaderRead );
	public override void BarrierToPixelShaderRead( GpuTexture depth ) => Barrier( depth, ImageLayout.ShaderRead );

	public override void BufferBarrier( GpuBuffer buffer, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess )
	{
		if ( recording is null ) return;
		CloseScope();
		List.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
	}

	//
	// Parameters
	//

	public override void SetViewConstants( in ViewConstants constants, ViewConstantsKey? key )
	{
		SetConstants( ViewConstantsName, constants );
		viewConstants = key;
	}

	internal override void InheritViewConstants( ViewConstantsKey key ) => viewConstants = key;

	public override void SetCombo( StringToken name, int value ) => attributes.SetCombo( name, value );

	public override void Set( StringToken name, GpuTexture value ) => attributes.Set( name, value );

	readonly (StringToken Name, bool Combo)[] passState = new (StringToken, bool)[8];
	int passStateCount;

	public override void SetPassCombo( StringToken name, int value )
	{
		RememberPassState( name, combo: true );
		SetCombo( name, value );
	}

	public override void SetPassAttribute( StringToken name, int value )
	{
		RememberPassState( name, combo: false );
		attributes.Set( name, value );
	}

	void RememberPassState( StringToken name, bool combo )
	{
		for ( int i = 0; i < passStateCount; i++ )
		{
			if ( passState[i].Name.Value == name.Value ) return;
		}

		if ( passStateCount == passState.Length ) throw new InvalidOperationException( "Too many pass combos and attributes" );
		passState[passStateCount++] = (name, combo);
	}

	public override void EndPass()
	{
		for ( int i = 0; i < passStateCount; i++ )
		{
			var (name, combo) = passState[i];
			if ( combo ) attributes.SetCombo( name, 0 );
			else attributes.Set( name, 0 );
		}

		passStateCount = 0;
		OverlayStencil = OverlayStencil.None;
	}

	public override void SetConstants<T>( StringToken name, in T value ) => attributes.SetData( name, value );

	public override void SetConstantBuffer<T>( StringToken name, IntPtr buffer, in T value ) => attributes.SetData( name, value );

	public override void SetObjectAttributes( ShaderParameters objectAttributes, ShaderParameters lightmap = null ) { }

	public override GpuScope BeginGpuScope( string name )
	{
		if ( recording is null ) return default;
		List.BeginLabel( name );
		return new GpuScope( 1, false );
	}

	public override void EndGpuScope( GpuScope scope )
	{
		if ( recording is null || scope.Marker == IntPtr.Zero ) return;
		List.EndLabel();
	}

	//
	// Drawing with the mesh program
	//

	public override bool DrawModel( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false )
	{
		if ( material is not BackendMaterial { Program: BackendProgram.Mesh } m || mesh.Geometry is not BackendMesh geometry || skinned ) return false;
		if ( InstanceIds?.Resource is not BackendBuffer instanceIds ) return false;

		var depthMode = shaderMode.Value == DepthMode.Value;
		if ( !depthMode && shaderMode.Value != ForwardMode.Value ) return false;
		if ( !EnsureScope() ) return false;

		var r = recording;
		var key = new MeshProgram.Key( depthMode || r.Color is null, r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb ),
			r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth ), (r.Color ?? r.Depth).Samples );
		var pipeline = device.Mesh.Pipeline( key );

		if ( r.Pipeline != pipeline )
		{
			r.List.SetPipeline( pipeline );
			r.Pipeline = pipeline;
		}

		BindSets();
		if ( key.Depth ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );

		var albedo = m.Albedo;
		r.List.PushConstants( new MeshProgram.MaterialConstants
		{
			Albedo = new System.Numerics.Vector4( albedo.r, albedo.g, albedo.b, albedo.a ),
			ShadowSampler = (uint)device.ShadowSampler,
		} );

		r.List.SetVertexBuffer( 0, geometry.Vertices );
		r.List.SetVertexBuffer( 1, instanceIds.Handle, firstInstance * 4L );
		r.List.SetIndexBuffer( geometry.Indices, geometry.IndexFormat );
		r.List.DrawIndexed( draw.IndexCount, instances, draw.StartIndex );
		return true;
	}

	static Format FormatOfTarget( BackendTexture texture, bool srgb )
	{
		var format = BackendDevice.FormatOf( texture );
		return srgb && format.HasSrgb() ? format.Srgb() : format;
	}

	/// <summary>
	/// Bind the program's sets from this context's parameters, unless nothing's changed since they were bound.
	/// </summary>
	void BindSets()
	{
		var r = recording;
		var version = attributes.Version;
		if ( r.Binder == this && r.BindVersion == version ) return;

		var program = device.Mesh;
		var view = Constants( ViewConstantsName, 1088 );
		var sun = Constants( DirectionalLightName, 416 );
		var lighting = Constants( LightingConfigName, 4128 );
		var transforms = attributes.Get( TransformBufferName ) is GpuBuffer { Resource: BackendBuffer buffer } ? buffer.Handle : default;
		if ( !transforms.IsValid ) transforms = device.EmptyStorage;
		var lights = attributes.Get( LightBufferName ) is GpuBuffer { Resource: BackendBuffer lightBuffer } ? lightBuffer.Handle : device.EmptyStorage;

		r.List.SetBindings( ShaderAbi.VertexSet, program.VertexSet,
		[
			Binding.Uniform( ShaderAbi.ConstantBuffer( 0 ), view.Buffer, view.Offset, view.Size ),
			Binding.Storage( ShaderAbi.Texture( 0 ), transforms ),
		] );

		r.List.SetBindings( ShaderAbi.FragmentSet, program.PixelSet,
		[
			Binding.Uniform( ShaderAbi.ConstantBuffer( 0 ), view.Buffer, view.Offset, view.Size ),
			Binding.Uniform( ShaderAbi.ConstantBuffer( 1 ), sun.Buffer, sun.Offset, sun.Size ),
			Binding.Uniform( ShaderAbi.ConstantBuffer( 2 ), lighting.Buffer, lighting.Offset, lighting.Size ),
			Binding.Storage( ShaderAbi.Texture( 0 ), lights ),
		] );

		r.Binder = this;
		r.BindVersion = version;
	}

	/// <summary>
	/// A named constant struct copied into this frame's upload memory, zeroes if it was never set.
	/// </summary>
	(BufferHandle Buffer, long Offset, long Size) Constants( StringToken name, int size )
	{
		var data = attributes.Get( name ) as byte[];
		var allocation = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
		allocation.Data.Clear();
		if ( data is not null ) data.AsSpan( 0, Math.Min( data.Length, size ) ).CopyTo( allocation.Data );
		return (allocation.Buffer, allocation.Offset, size);
	}

	//
	// Transfers
	//

	public override void UploadBuffer<T>( GpuBuffer buffer, ReadOnlySpan<T> data, int offsetBytes )
	{
		if ( recording is null || buffer.Resource is not BackendBuffer target ) return;
		CloseScope();
		List.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryRead, PipelineStage.Transfer, MemoryAccess.TransferWrite );
		List.UpdateBuffer( target.Handle, MemoryMarshal.AsBytes( data ), offsetBytes );
		List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead );
	}

	public override void ReadBuffer( GpuBuffer buffer, int offset, int bytes, GpuReadback callback )
	{
		if ( recording is null || buffer.Resource is not BackendBuffer source ) return;
		CloseScope();

		var readback = Gpu.CreateBuffer( new BufferInfo { Size = bytes, Memory = MemoryLocation.Readback, Name = "Basalt readback" } );
		List.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead );
		List.CopyBuffer( source.Handle, offset, readback, 0, bytes );
		List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Host, MemoryAccess.HostRead );
		recording.Readbacks.Add( (readback, bytes, callback) );
	}

	public override void CopyTexture( GpuTexture source, GpuTexture destination, int slices )
	{
		if ( recording is null || source is not BackendTexture from || destination is not BackendTexture to ) return;
		CloseScope();
		Transition( from, ImageLayout.CopySource, always: true );
		Transition( to, ImageLayout.CopyDestination, always: true );
		List.CopyTexture( from.Handle, to.Handle, 0, Math.Max( slices, 1 ) );
	}

	public override void CopyToSwapChain( in RenderOutput output, Rect viewport )
	{
		// Only texture outputs: windows present what a ViewTarget holds themselves (BackendDevice.Unwrap)
		if ( recording is null || output.Texture is not BackendTexture texture || output.Target?.Resolved is not BackendTexture source ) return;
		Invalidate();
		CloseScope();

		var destination = output.Scratch as BackendTexture ?? texture;
		Transition( source, ImageLayout.CopySource, always: true );
		Transition( destination, ImageLayout.CopyDestination, always: true );
		var rect = ((int)viewport.Left, (int)viewport.Top, (int)viewport.Width, (int)viewport.Height);
		List.Blit( source.Handle, rect, destination.Handle, rect );
	}

	public override GpuTexture OutputColor( in RenderOutput output, out bool srgbWrite )
	{
		if ( output.Texture is { } texture )
		{
			srgbWrite = !texture.Format.IsFloat();
			return output.Scratch ?? texture;
		}

		srgbWrite = output.Target.SrgbWrite;
		return output.Target.Samples > 1 ? output.Target.Resolved : output.Target.Color;
	}

	public override void Resolve( in RenderOutput output )
	{
		if ( recording is null || output.Target is not { Samples: > 1 } target ) return;
		ResolveInto( target.Color as BackendTexture, target.Resolved as BackendTexture );
	}

	public override void ResolveScratch( in RenderOutput output )
	{
		if ( recording is null || output.Scratch is not BackendTexture scratch || output.Texture is not BackendTexture texture ) return;
		Invalidate();
		ResolveInto( scratch, texture );
	}

	void ResolveInto( BackendTexture source, BackendTexture destination )
	{
		if ( source is null || destination is null ) return;
		CloseScope();
		Transition( source, ImageLayout.CopySource, always: true );
		Transition( destination, ImageLayout.CopyDestination, always: true );
		List.ResolveTexture( source.Handle, destination.Handle );
	}

	GpuTexture frameBufferCopy;
	Rect frameBufferCopyViewport;
	bool frameBufferCopied;

	public override void BeginFrameBufferReads( GpuTexture copy, Rect viewport )
	{
		frameBufferCopy = copy;
		frameBufferCopyViewport = viewport;
		frameBufferCopied = false;
	}

	public override void EndFrameBufferReads() => frameBufferCopy = null;

	public override void CopyFrameBuffer()
	{
		if ( frameBufferCopy is null || frameBufferCopied || bound.Color is null ) return;
		frameBufferCopied = true;

		CopyTexture( bound.Color, frameBufferCopy, 1 );
		var viewport = frameBufferCopyViewport;
		attributes.Set( FrameBufferCopyTexture, frameBufferCopy );
		attributes.Set( FrameBufferCopyRectangle, new Vector4( 0, 0, viewport.Width / frameBufferCopy.Width, viewport.Height / frameBufferCopy.Height ) );
	}

	//
	// What only Source 2 has: skipped, as the headless device skips a mesh with no geometry
	//

	public override bool DispatchSkinning( GpuMaterial skinningShader, RenderMesh mesh, int modelMesh, GpuBuffer transforms, ReadOnlySpan<int> slots, ReadOnlySpan<int> vertexCacheOffsets, int blendWeights,
		GpuBuffer volumes = null, ReadOnlySpan<int> volumeOffsets = default, ReadOnlySpan<int> volumeCounts = default, GpuBuffer anchors = null, ReadOnlySpan<int> anchorOffsets = default, bool morph = false ) => false;

	public override void GenerateMorphs() { }
	public override void BarrierVertexCacheToWrite() { }
	public override void BarrierVertexCacheToRead() { }
	public override bool BindVertexCacheIds( RenderMesh mesh, in RenderMesh.Draw draw ) => false;
	public override bool DrawSky( SceneLighting lighting, StringToken shaderMode ) => false;
	public override bool BindGeometry( RenderMesh mesh, in RenderMesh.Draw draw ) => false;

	public override bool SetRenderState( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool vertexCache = false ) => false;

	public override bool DrawModelCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int argsIndex, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false ) => false;

	public override bool DrawModelFragments( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, GpuBuffer args, int firstArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false ) => false;

	public override bool DrawModelFragmentsCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int cullArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false ) => false;

	public override void BindInstances( int stream, int firstInstance ) { }

	public override void Dispatch( GpuMaterial computeShader, int x, int y, int z ) { }

	public override void Dispatch( GpuMaterial computeShader, ShaderParameters parameters, int x, int y, int z ) { }

	public override void DrawFullscreen( GpuMaterial material, ShaderParameters parameters, StringToken shaderMode ) { }

	public override void DrawIndexedInstanced( in RenderMesh.Draw draw, int instanceCount ) { }
}
