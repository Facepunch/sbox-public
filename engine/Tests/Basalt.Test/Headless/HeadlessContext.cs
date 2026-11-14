using Basalt;
using Basalt.Culling;
using Basalt.Gpu;
using System;
using System.Collections.Generic;

namespace BasaltTests.Headless;

/// <summary>
/// A recording context that keeps what it's asked to do as <see cref="GpuCommand"/>s, and hands them to its device when
/// submitted. It keeps the state a real one does - parameters, bound targets, pass values - so Basalt sees what it would.
/// </summary>
internal sealed class HeadlessContext : GpuContext
{
	readonly HeadlessDevice device;
	HeadlessContext frame;
	readonly HeadlessParameters attributes = new();
	readonly Stack<string> scopes = new();

	// What's recorded since Begin: into a shared context's list when borrowing it
	List<GpuCommand> commands = new();
	HeadlessContext shared;
	bool recording;
	GpuSemaphore wait, signal;

	public HeadlessContext( HeadlessDevice device, HeadlessContext frame )
	{
		this.device = device;
		this.frame = frame;
	}

	void Record( GpuCommand command )
	{
		if ( !recording ) throw new InvalidOperationException( $"Recording {command.Op} into a context that isn't recording" );
		(shared ?? this).commands.Add( command );
	}

	GpuCommand Command( GpuOp op ) => new( op, scopes.Count == 0 ? null : string.Join( " / ", scopes.Reverse() ) );

	public override bool AsyncCompute { get; set; }

	internal override bool GpuScopesEnabled => true;

	internal override bool IsFrame => frame is null;

	internal override bool IsRecording => recording;

	internal override ShaderParameters Attributes => attributes;

	internal override (int Full, int Similar) MaterialBinds => default;

	internal override StageTarget Bound => bound;
	StageTarget bound;
	bool boundValid;

	internal override ViewConstantsKey? ViewConstants => viewConstants;
	ViewConstantsKey? viewConstants;

	public override OverlayStencil OverlayStencil { get; set; }

	public override GpuBuffer<uint> InstanceIds { get; set; }

	public override OcclusionCuller Occlusion { get; set; }

	public override void BeginSegment( GpuContext frame, GpuContext shared )
	{
		if ( this.frame is null || frame is null ) throw new InvalidOperationException( "Only a segment context records a frame's segments" );
		this.frame = (HeadlessContext)frame;
		Begin( shared );
	}

	public override void Begin( GpuContext shared = null )
	{
		if ( recording ) throw new InvalidOperationException( "Already recording" );
		if ( shared is not null && AsyncCompute ) throw new InvalidOperationException( "An async compute context records into its own context" );

		if ( frame is not null )
		{
			attributes.Clear();
			attributes.Parent = frame.attributes;
			InstanceIds = frame.InstanceIds;
			Occlusion = frame.Occlusion;
		}

		this.shared = (HeadlessContext)shared;
		commands = new();
		scopes.Clear();
		recording = true;
		wait = signal = default;
		OverlayStencil = OverlayStencil.None;
		boundValid = false;
		bound = default;
		viewConstants = null;
		passStateCount = 0;
		if ( frame is null ) viewSetWritten = false;
	}

	bool viewSetWritten;

	/// <summary>Recorded once a frame, with the attributes the view set is filled from as they are then.</summary>
	public override void WriteViewSet()
	{
		if ( frame is not null ) throw new InvalidOperationException( "Only a frame context writes the view set" );
		if ( viewSetWritten ) return;
		viewSetWritten = true;
		var snapshot = new HeadlessParameters();
		attributes.MergeTo( snapshot );
		Record( Command( GpuOp.WriteViewSet ) with { Parameters = snapshot } );
	}

	public override void Submit( GpuSwapChain swapChain = default )
	{
		if ( !recording ) return;
		device.BeginSubmitting( swapChain );
		SubmitNext();
	}

	public override void SubmitNext()
	{
		if ( !recording ) return;

		try
		{
			if ( shared is null ) device.Submit( this, wait, signal, commands );
		}
		finally
		{
			End();
		}
	}

	public override void End()
	{
		recording = false;
		shared = null;
	}

	public override GpuSemaphore SignalAtEnd()
	{
		if ( signal.Handle != IntPtr.Zero ) throw new InvalidOperationException( "A context signals once" );
		return signal = device.Signal( this );
	}

	public override void WaitAtBegin( GpuSemaphore semaphore )
	{
		if ( wait.Handle != IntPtr.Zero ) throw new InvalidOperationException( "A context waits once" );
		device.Wait( semaphore );
		wait = semaphore;
	}

	public override void Dispose() { }

	public override void ResetMaterialBind() { }

	public override void Bind( in StageTarget target )
	{
		if ( boundValid && target.Color == bound.Color && target.Depth == bound.Depth && target.DepthSlice == bound.DepthSlice && target.SrgbWrite == bound.SrgbWrite )
		{
			if ( bound.Viewport != target.Viewport || bound.MinZ != target.MinZ || bound.MaxZ != target.MaxZ ) SetViewport( target.Viewport, target.MinZ, target.MaxZ );
			bound = target;
			return;
		}

		Record( Command( GpuOp.Bind ) with { Texture = target.Color, Depth = target.Depth, Count = target.DepthSlice } );
		boundValid = true;
		bound = target;
		SetViewport( target.Viewport, target.MinZ, target.MaxZ );
	}

	internal override void RestoreBound()
	{
		Invalidate();
		Bind( bound );
	}

	internal override void Invalidate() => boundValid = false;

	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );

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

	public override GpuScope BeginGpuScope( string name )
	{
		scopes.Push( name );
		return new GpuScope( (IntPtr)scopes.Count, false );
	}

	public override void EndGpuScope( GpuScope scope )
	{
		if ( scopes.Count != (int)scope.Marker ) throw new InvalidOperationException( "GPU scopes must end innermost first" );
		scopes.Pop();
	}

	public override void SetObjectAttributes( ShaderParameters objectAttributes, ShaderParameters lightmap = null ) { }

	public override bool DispatchSkinning( GpuMaterial skinningShader, RenderMesh mesh, int modelMesh, GpuBuffer transforms, ReadOnlySpan<int> slots, ReadOnlySpan<int> vertexCacheOffsets, int blendWeights,
		GpuBuffer volumes = null, ReadOnlySpan<int> volumeOffsets = default, ReadOnlySpan<int> volumeCounts = default, GpuBuffer anchors = null, ReadOnlySpan<int> anchorOffsets = default, bool morph = false )
	{
		if ( skinningShader is null || mesh.Geometry is null ) return false;
		Record( Command( GpuOp.DispatchSkinning ) with { Material = skinningShader, Mesh = mesh, Count = slots.Length, Buffer = transforms } );
		return true;
	}

	public override void GenerateMorphs() => Record( Command( GpuOp.GenerateMorphs ) );

	public override void BarrierVertexCacheToWrite() => Record( Command( GpuOp.BufferBarrier ) with
	{
		SrcStage = GpuStage.PreRasterizationShaders,
		SrcAccess = GpuAccess.ShaderRead,
		DstStage = GpuStage.ComputeShader,
		DstAccess = GpuAccess.ShaderWrite,
	} );

	public override void BarrierVertexCacheToRead() => Record( Command( GpuOp.BufferBarrier ) with
	{
		SrcStage = GpuStage.ComputeShader,
		SrcAccess = GpuAccess.ShaderWrite,
		DstStage = GpuStage.PreRasterizationShaders,
		DstAccess = GpuAccess.ShaderRead,
	} );

	public override bool BindVertexCacheIds( RenderMesh mesh, in RenderMesh.Draw draw )
	{
		if ( mesh.Geometry is null ) return false;
		Record( Command( GpuOp.BindVertexCacheIds ) with { Mesh = mesh } );
		return true;
	}

	public override bool DrawSky( SceneLighting lighting, StringToken shaderMode )
	{
		if ( lighting.SkyMaterial is null ) return false;
		Record( Command( GpuOp.DrawSky ) with { Material = lighting.SkyMaterial, Mode = shaderMode, Texture = bound.Color, Depth = bound.Depth } );
		return true;
	}

	public override void CopyToSwapChain( in RenderOutput output, Rect viewport )
	{
		if ( !output.HasCopy ) return;

		Invalidate();
		var destination = output.Texture is { } texture ? output.Scratch ?? texture : device.SwapChainColor( output.SwapChain );
		Record( Command( GpuOp.CopyToOutput ) with { Texture = destination, Source = output.Target.Resolved } );
	}

	public override GpuTexture OutputColor( in RenderOutput output, out bool srgbWrite )
	{
		if ( output.Texture is { } texture )
		{
			srgbWrite = !texture.Format.IsFloat();
			return output.Scratch ?? texture;
		}

		if ( output.SwapChain.IsValid )
		{
			srgbWrite = true;
			return device.SwapChainColor( output.SwapChain );
		}

		srgbWrite = output.Target.SrgbWrite;
		return output.Target.Samples > 1 ? output.Target.Resolved : output.Target.Color;
	}

	public override void Resolve( in RenderOutput output )
	{
		if ( output.Target is not { Samples: > 1 } target ) return;
		Record( Command( GpuOp.Resolve ) with { Texture = target.Resolved, Source = bound.Color } );
	}

	public override void ResolveScratch( in RenderOutput output )
	{
		if ( output.Scratch is not { } scratch || output.Texture is not { } texture ) return;

		Invalidate();
		Record( Command( GpuOp.ResolveScratch ) with { Texture = texture, Source = scratch } );
	}

	public override void CopyTexture( GpuTexture source, GpuTexture destination, int slices )
		=> Record( Command( GpuOp.CopyTexture ) with { Texture = destination, Source = source, Count = slices } );

	public override void CopyTextureRegion( GpuTexture source, GpuTexture destination, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height )
		=> Record( Command( GpuOp.CopyTexture ) with { Texture = destination, Source = source, Count = 1 } );

	void Barrier( GpuTexture texture, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess, TextureLayout layout ) => Record( Command( GpuOp.TextureBarrier ) with
	{
		Texture = texture,
		SrcStage = srcStage,
		SrcAccess = srcAccess,
		DstStage = dstStage,
		DstAccess = dstAccess,
		Layout = layout,
	} );

	public override void BarrierToDepthWrite( GpuTexture depth ) => Barrier( depth, GpuStage.FragmentShader, default,
		GpuStage.EarlyFragmentTests | GpuStage.LateFragmentTests, GpuAccess.DepthStencilAttachmentRead | GpuAccess.DepthStencilAttachmentWrite, TextureLayout.DepthAttachment );

	public override void BarrierToComputeRead( GpuTexture depth ) => Barrier( depth, GpuStage.LateFragmentTests, GpuAccess.DepthStencilAttachmentWrite,
		GpuStage.ComputeShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );

	public override void BarrierColorToComputeRead( GpuTexture texture ) => Barrier( texture, GpuStage.ColorAttachmentOutput, GpuAccess.ColorAttachmentWrite,
		GpuStage.ComputeShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );

	public override void BarrierComputeToPixelShaderRead( GpuTexture texture ) => Barrier( texture, GpuStage.ComputeShader, GpuAccess.ShaderWrite,
		GpuStage.FragmentShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );

	public override void BarrierToPixelShaderRead( GpuTexture depth ) => Barrier( depth, GpuStage.LateFragmentTests, GpuAccess.DepthStencilAttachmentWrite,
		GpuStage.FragmentShader, GpuAccess.ShaderRead, TextureLayout.ShaderRead );

	public override void TextureBarrier( GpuTexture texture, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess, TextureLayout layout )
		=> Barrier( texture, srcStage, srcAccess, dstStage, dstAccess, layout );

	public override void BufferBarrier( GpuBuffer buffer, GpuStage srcStage, GpuAccess srcAccess, GpuStage dstStage, GpuAccess dstAccess ) => Record( Command( GpuOp.BufferBarrier ) with
	{
		Buffer = buffer,
		SrcStage = srcStage,
		SrcAccess = srcAccess,
		DstStage = dstStage,
		DstAccess = dstAccess,
	} );

	public override void SetViewport( Rect rect, float minZ = 0, float maxZ = 1 ) => bound = bound with { Viewport = rect, MinZ = minZ, MaxZ = maxZ };

	public override void Clear( Color color, bool clearColor = true, bool clearDepth = true )
		=> Record( Command( GpuOp.Clear ) with { Texture = clearColor ? bound.Color : null, Depth = clearDepth ? bound.Depth : null } );

	public override void SetConstants<T>( StringToken name, in T value )
	{
		attributes.SetData( name, value );
		Record( Command( GpuOp.SetConstants ) with { Mode = name } );
	}

	public override void SetConstantBuffer<T>( StringToken name, IntPtr buffer, in T value ) => SetConstants( name, value );

	public override bool BindGeometry( RenderMesh mesh, in RenderMesh.Draw draw )
	{
		if ( mesh.Geometry is null ) return false;
		Record( Command( GpuOp.BindGeometry ) with { Mesh = mesh } );
		return true;
	}

	public override bool SetRenderState( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool vertexCache = false )
	{
		if ( material is null || mesh.Geometry is null ) return false;
		Record( Command( GpuOp.SetRenderState ) with { Material = material, Mode = shaderMode, Mesh = mesh } );
		return true;
	}

	GpuCommand Draw( GpuOp op, GpuMaterial material, StringToken shaderMode, RenderMesh mesh, int count )
		=> Command( op ) with { Material = material, Mode = shaderMode, Mesh = mesh, Count = count, Texture = bound.Color, Depth = bound.Depth };

	public override bool DrawModel( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false, int textureSize = TextureSizes.All )
	{
		if ( material is null || mesh.Geometry is null ) return false;
		Record( Draw( GpuOp.Draw, material, shaderMode, mesh, instances ) with { TextureSize = textureSize } );
		return true;
	}

	public override bool DrawModelCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int argsIndex, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool skinned = false, int textureSize = TextureSizes.All )
	{
		if ( material is null || mesh.Geometry is null || Occlusion is null ) return false;
		Record( Draw( GpuOp.DrawCulled, material, shaderMode, mesh, 1 ) with { Buffer = Occlusion.Args, TextureSize = textureSize } );
		return true;
	}

	public override bool DrawModelFragments( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, GpuBuffer args, int firstArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All )
	{
		if ( material is null || mesh.Geometry is null ) return false;
		Record( Draw( GpuOp.DrawFragments, material, shaderMode, mesh, count ) with { Buffer = args, TextureSize = textureSize } );
		return true;
	}

	public override bool DrawModelFragmentsCulled( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, OcclusionPhase phase, int firstInstance, int cullArg, int count, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, int textureSize = TextureSizes.All )
	{
		if ( material is null || mesh.Geometry is null || Occlusion is null ) return false;
		Record( Draw( GpuOp.DrawFragmentsCulled, material, shaderMode, mesh, count ) with { Buffer = Occlusion.Args, TextureSize = textureSize } );
		return true;
	}

	public override void UploadBuffer<T>( GpuBuffer buffer, ReadOnlySpan<T> data, int offsetBytes )
	{
		((HeadlessBuffer)buffer.Resource).Upload( System.Runtime.InteropServices.MemoryMarshal.AsBytes( data ), offsetBytes );
		Record( Command( GpuOp.Upload ) with { Buffer = buffer, Count = data.Length } );
	}

	public override void BindInstances( int stream, int firstInstance ) => Record( Command( GpuOp.BindInstances ) with { Count = firstInstance } );

	/// <summary>
	/// Nothing comes back: the GPU never finishes.
	/// </summary>
	public override void ReadBuffer( GpuBuffer buffer, int offset, int bytes, GpuReadback callback )
		=> Record( Command( GpuOp.ReadBuffer ) with { Buffer = buffer, Count = bytes } );

	static readonly StringToken FrameBufferCopyTexture = new( "FrameBufferCopyTexture" );
	static readonly StringToken FrameBufferCopyRectangle = new( "FrameBufferCopyRectangle" );

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
		if ( frameBufferCopy is null || frameBufferCopied ) return;
		frameBufferCopied = true;

		Record( Command( GpuOp.CopyFrameBuffer ) with { Texture = frameBufferCopy, Source = bound.Color } );
		var viewport = frameBufferCopyViewport;
		attributes.Set( FrameBufferCopyTexture, frameBufferCopy );
		attributes.Set( FrameBufferCopyRectangle, new Vector4( 0, 0, viewport.Width / frameBufferCopy.Width, viewport.Height / frameBufferCopy.Height ) );
	}

	public override void Dispatch( GpuMaterial computeShader, int x, int y, int z ) => Dispatch( computeShader, null, x, y, z );

	public override void Dispatch( GpuMaterial computeShader, ShaderParameters parameters, int x, int y, int z )
	{
		Invalidate();
		Record( Command( GpuOp.Dispatch ) with { Material = computeShader, Parameters = parameters, Threads = (x, y, z) } );
	}

	public override void DrawFullscreen( GpuMaterial material, ShaderParameters parameters, StringToken shaderMode )
		=> Record( Command( GpuOp.DrawFullscreen ) with { Material = material, Parameters = parameters, Mode = shaderMode, Texture = bound.Color, Depth = bound.Depth } );

	public override void DrawIndexedInstanced( in RenderMesh.Draw draw, int instanceCount )
		=> Record( Command( GpuOp.DrawIndexed ) with { Count = instanceCount, Texture = bound.Color, Depth = bound.Depth } );
}
