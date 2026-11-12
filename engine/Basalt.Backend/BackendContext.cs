using Basalt.Culling;
using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Runtime.InteropServices;

namespace Basalt.Backend;

/// <summary>
/// Basalt's recording context on a <see cref="CommandList"/>. Basalt binds targets and leaves the rest to the device, as
/// Source 2's render contexts do, so this opens rendering scopes lazily at the first draw or clear, closes them before
/// anything that can't happen inside one, and moves textures between layouts as they're used, from the layout each
/// texture remembers.
/// </summary>
/// <remarks>
/// Layouts are tracked per texture. A frame's context, and segments sharing its list, move each texture's layout as
/// they record, which is right while recording order is submission order. A segment recording a list of its own (the
/// scene recording on several threads) tracks layouts for its list alone: a texture's first use there records what it
/// needs, with no barrier, and at submission, in submission order, a list of fix-ups moves each texture from where the
/// lists before left it to what this one needs, and the texture then remembers where this one left it. Async compute
/// isn't wired yet.
/// </remarks>
internal sealed partial class BackendContext : GpuContext
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

		/// <summary>The context recording into the list now: its clears still pending are flushed before another records.</summary>
		public BackendContext Active;
		public int BindVersion = -1;

		/// <summary>
		/// For a list recorded beside others: each texture's layout when this list first used it (what it needs at its
		/// start) and where it leaves it. Null for a frame's list, which moves the textures' own layouts.
		/// </summary>
		public Dictionary<BackendTexture, (ImageLayout First, ImageLayout Current)> Layouts;
		public readonly List<(BufferHandle Buffer, int Bytes, GpuReadback Callback)> Readbacks = new();

		/// <summary>The list's own scope in <see cref="BackendDevice.PassTimer"/>, -1 if it's not timed.</summary>
		public int TimerScope = -1;

		/// <summary>The timed scope counting statistics in this list, -1 if none: they don't nest.</summary>
		public int Counting = -1;
	}

	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );
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

			// A clear the context before left pending (a pass that drew nothing) happens before this one records
			if ( recording.Active is { } previous && previous != this && previous.recording == recording ) previous.FlushClears();
		}
		else
		{
			var queue = AsyncCompute && Gpu.Capabilities.AsyncCompute ? QueueType.Compute : QueueType.Graphics;
			recording = new Session { List = Gpu.BeginCommandList( queue, frame is null ? "Basalt frame" : "Basalt segment" ) };
			if ( frame is null && device.FrameStartTimestamp is { } stamp )
			{
				device.FrameStartTimestamp = null;
				recording.List.ResetTimestamps( stamp.Queries, stamp.Index, 2 );
				recording.List.WriteTimestamp( stamp.Queries, stamp.Index, PipelineStage.TopOfPipe );
			}

			if ( device.PassTimer is { } timer )
			{
				if ( frame is null ) timer.ResetIn( recording.List );
				recording.TimerScope = timer.Begin( recording.List, recording.List.Name, PassTimer.ScopeKind.List );
			}

			// A segment with a list of its own may record while others do: its layouts are its own until it's submitted
			if ( frame is not null ) recording.Layouts = new();
		}

		wait = signal = default;
		boundValid = false;
		bound = default;
		clearColor = clearDepth = false;
		viewConstants = null;
		passStateCount = 0;
		if ( frame is null ) viewSetWritten = false;
		OverlayStencil = OverlayStencil.None;
		SetObjectAttributes( null );
		recording.BindVersion = -1;
		recording.Active = this;
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

			if ( recording.Active is { } last && last != this && last.recording == recording ) last.FlushClears();
			CloseScope();
			device.PassTimer?.End( recording.List, recording.TimerScope );
			if ( recording.Layouts is { Count: > 0 } ) SubmitLayoutFixUps( recording );
			Span<SyncPoint> waits = stackalloc SyncPoint[1];
			var waitCount = 0;
			if ( wait.Handle != IntPtr.Zero ) waits[waitCount++] = device.PointOf( wait );

			var point = waitCount == 0 && device.BatchSubmits ? Gpu.SubmitLater( recording.List ) : Gpu.Submit( recording.List, waits[..waitCount] );
			if ( signal.Handle != IntPtr.Zero ) device.Signalled( signal, point );
			foreach ( var (buffer, bytes, callback) in recording.Readbacks ) device.AddReadback( point, buffer, bytes, callback );
		}
		finally
		{
			End();
		}
	}

	/// <summary>
	/// Before a segment's own list: move every texture it used from where the lists submitted before left it to what
	/// this one needs at its start, then remember where this one leaves it. Called in submission order.
	/// </summary>
	void SubmitLayoutFixUps( Session session )
	{
		CommandList fixUps = null;
		var timed = -1;
		foreach ( var (texture, (first, current)) in session.Layouts )
		{
			if ( texture.Layout != first )
			{
				if ( fixUps is null )
				{
					fixUps = Gpu.BeginCommandList( session.List.Queue, "Basalt layouts" );
					timed = device.PassTimer?.Begin( fixUps, fixUps.Name, PassTimer.ScopeKind.List ) ?? -1;
				}

				fixUps.Barrier( Basalt.Gpu.TextureBarrier.Transition( texture.Handle, texture.Layout, first,
					PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite ) );
			}

			texture.Layout = current;
		}

		if ( fixUps is not null )
		{
			device.PassTimer?.End( fixUps, timed );
			if ( device.BatchSubmits ) Gpu.SubmitLater( fixUps );
			else Gpu.Submit( fixUps );
		}

		LayoutFixUps += fixUps is null ? 0 : 1;
	}

	/// <summary>Fix-up lists submitted before segments recorded beside others, for tests.</summary>
	internal static int LayoutFixUps;

	/// <summary>
	/// The layout a texture is in at this point of the recording: where this list's own use left it, else where the
	/// lists before left it.
	/// </summary>
	internal ImageLayout LayoutOf( BackendTexture texture )
	{
		if ( recording?.Layouts is { } local && local.TryGetValue( texture, out var known ) ) return known.Current;
		return texture.Layout;
	}

	/// <summary>
	/// Hand the list this context records into to another recorder on the device (the engine's drawing, in a camera's
	/// stage) to record into next, in order: what's open is closed and pending clears are done. Textures are where
	/// <see cref="LendLayout"/> says; tell this context where the other left them with <see cref="SetLayout"/>, then
	/// <see cref="Reclaim"/> it.
	/// </summary>
	internal CommandList Lend()
	{
		CloseScope();
		return List;
	}

	/// <summary>
	/// The layout <paramref name="texture"/> is in for another recorder in the lent list (<see cref="Lend"/>): as
	/// <see cref="LayoutOf"/>, except in a list recorded beside others that hasn't used it yet, where the lists before it may
	/// still move it: there it's <paramref name="start"/>, which the list then needs at its start, as a draw that samples
	/// one does (<see cref="SampledLayoutOf"/>).
	/// </summary>
	internal ImageLayout LendLayout( BackendTexture texture, ImageLayout start )
	{
		if ( recording?.Layouts is not { } local ) return texture.Layout;
		if ( local.TryGetValue( texture, out var known ) ) return known.Current;
		local[texture] = (start, start);
		return start;
	}

	/// <summary>Where another recorder left <paramref name="texture"/> in the list it was lent (<see cref="Lend"/>).</summary>
	internal void SetLayout( BackendTexture texture, ImageLayout layout )
	{
		if ( recording?.Layouts is { } local ) local[texture] = (local.TryGetValue( texture, out var known ) ? known.First : texture.Layout, layout);
		else texture.Layout = layout;
	}

	/// <summary>Carry on recording after a lend: the list's pipeline and sets are another's now, so they're bound again.</summary>
	internal void Reclaim()
	{
		if ( recording is not { } r ) return;
		r.Pipeline = default;
		r.Binder = null;
		r.BindVersion = -1;
		boundValid = false;
	}

	/// <summary>
	/// The layout of a texture a draw samples: as <see cref="LayoutOf"/>, and when a list recorded beside others reads one
	/// it hasn't used yet, it notes that it needs it readable at its start.
	/// </summary>
	internal ImageLayout SampledLayoutOf( BackendTexture texture )
	{
		var layout = LayoutOf( texture );
		if ( layout == ImageLayout.ShaderRead && recording?.Layouts is { } local ) local.TryAdd( texture, (layout, layout) );
		return layout;
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

	public override void Dispose()
	{
		if ( viewSet.IsValid ) Gpu.Destroy( viewSet );
		viewSet = default;
	}

	//
	// The view set
	//

	// A frame context's view set (ShaderAbi.ViewSet): a new one each frame, the last destroyed once the GPU is done with it
	BindingSetHandle viewSet;
	bool viewSetWritten;
	Binding[] viewBindings;

	/// <summary>
	/// Write the view set from this frame context's attributes - constant buffers copied into the frame's pass constants,
	/// buffers by name, placeholders for what isn't set - and bind it for everything this frame records after.
	/// </summary>
	public override void WriteViewSet()
	{
		if ( frame is not null ) throw new InvalidOperationException( "Only a frame context writes the view set" );
		if ( viewSetWritten ) return;
		viewSetWritten = true;

		var cache = device.MaterialCache;
		viewBindings ??= new Binding[ShaderAbi.ViewSetBindings.Length];
		for ( int i = 0; i < viewBindings.Length; i++ )
		{
			var (binding, type, name) = ShaderAbi.ViewSetBindings[i];
			var value = attributes.Get( Token( name ) );
			if ( type == BindingType.UniformBuffer )
			{
				if ( value is byte[] { Length: > 0 } data )
				{
					var size = (data.Length + 15) & ~15;
					var offset = cache.AllocatePassConstants( size, out var target );
					target.Clear();
					data.CopyTo( target );
					viewBindings[i] = Binding.Uniform( binding, cache.PassBuffer, offset, size );
				}
				else
				{
					viewBindings[i] = Binding.Uniform( binding, device.Defaults.Uniform, 0, ShaderDefaults.UniformSize );
				}
			}
			else
			{
				viewBindings[i] = value is GpuBuffer { Resource: BackendBuffer buffer } ? Binding.Storage( binding, buffer.Handle ) : Binding.Storage( binding, device.Defaults.Storage );
			}
		}

		if ( viewSet.IsValid ) Gpu.Destroy( viewSet );
		viewSet = Gpu.CreateBindingSet( Gpu.ViewSetLayout, viewBindings );
		recording?.List.SetViewSet( viewSet );
	}

	/// <summary>Bind the frame's view set on <paramref name="list"/>, once it has one.</summary>
	void BindViewSet( CommandList list )
	{
		var owner = frame ?? this;
		if ( owner.viewSet.IsValid ) list.SetViewSet( owner.viewSet );
	}

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
		// The depth range as given, as Source 2's contexts set it: the 2D sky draws at [0, 0], at the far plane
		r.List.SetViewport( rect.Left, rect.Top, rect.Width, rect.Height, bound.MinZ, bound.MaxZ );
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

		// A list recorded beside others: its first use of a texture is what it needs, moved there before it's submitted
		var local = recording.Layouts;
		var known = default( (ImageLayout First, ImageLayout Current) );
		if ( local is not null && !local.TryGetValue( texture, out known ) )
		{
			local[texture] = (layout, layout);
			return;
		}

		var from = local is null ? texture.Layout : known.Current;
		if ( from == layout && !always ) return;
		if ( recording.Rendering ) CloseScope();

		recording.List.Barrier( Basalt.Gpu.TextureBarrier.Transition( texture.Handle, from, layout,
			PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite ) );
		if ( local is null ) texture.Layout = layout;
		else local[texture] = (known.First, layout);
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

	public override GpuScope BeginGpuScope( string name )
	{
		if ( recording is null ) return default;
		List.BeginLabel( name );
		if ( device.PassTimer is not { } timer ) return new GpuScope( 1, false );

		// Statistics queries begin and end outside rendering here, so the scope can't straddle one; an outer scope counts for the ones inside
		var count = timer.Statistics && recording.Counting < 0;
		if ( count ) CloseScope();
		var timed = timer.Begin( List, name, PassTimer.ScopeKind.Pass, count );
		if ( count && timer.Counts( timed ) ) recording.Counting = timed;
		return new GpuScope( timed + 2, false );
	}

	public override void EndGpuScope( GpuScope scope )
	{
		if ( recording is null || scope.Marker == IntPtr.Zero ) return;
		if ( scope.Marker > 1 && device.PassTimer is { } timer )
		{
			var timed = (int)scope.Marker - 2;
			if ( recording.Counting == timed )
			{
				CloseScope();
				recording.Counting = -1;
			}

			timer.End( List, timed );
		}

		List.EndLabel();
	}

	//
	// Drawing with s&box's shaders: every binding the variant reflects gets the material's value, the attribute
	// of its name from this context's parameters, or a default
	//

	bool DrawByReflection( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int firstInstance, int instances, int depthBias, float slopeScaledDepthBias, bool stripPixelShader, bool skinned, in IndirectDraws indirect = default )
	{
		if ( material is not BackendMaterial { Shader: { } shader } m || mesh.Geometry is not IBackendGeometry source ) return false;
		if ( (indirect.InstanceIds ?? InstanceIds?.Resource as BackendBuffer) is not { } instanceIds ) return false;

		var mode = ModeName( shader, shaderMode );
		if ( mode is null ) return false;

		var variant = Variant( m, mode );
		if ( variant?.Vertex?.Bytecode is null ) return false;
		if ( !EnsureScope() ) return false;

		var r = recording;
		var color = r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var noColor = stripPixelShader || r.Color is null;
		var depthOnly = StripsPixelShader( m, noColor, stripPixelShader );
		var geometry = GeometryFor( source, draw );
		var pipeline = device.Pipeline( variant, geometry.Layout, noColor ? Format.Unknown : color, r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth ), (r.Color ?? r.Depth).Samples, depthOnly );

		if ( r.Pipeline != pipeline )
		{
			r.List.SetPipeline( pipeline );
			r.Pipeline = pipeline;
		}

		uncompressedTangentFrame = geometry.UncompressedTangentFrame;
		BindSets( m, variant, depthOnly );
		if ( noColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
		if ( variant.State.StencilTest && r.Depth is not null && BackendDevice.FormatOf( r.Depth ).HasStencil() ) r.List.SetStencilReference( (byte)variant.State.StencilReference );

		RecordDraw( r.List, geometry, instanceIds, draw, firstInstance, instances, indirect );
		return true;
	}

	/// <summary>
	/// Whether a draw goes without its pixel shader: when Basalt strips it, or in a pass without colour (a prepass, a shadow
	/// map) for a material whose depth needs none. Alpha-tested and translucent materials keep it there, as native does when
	/// Basalt doesn't strip it: their depth comes from what it clips (alpha test, alpha to coverage).
	/// </summary>
	static bool StripsPixelShader( BackendMaterial material, bool noColor, bool stripPixelShader )
		=> stripPixelShader || (noColor && !material.IsAlphaTest && !material.IsTranslucent);

	/// <summary>Whether the draw being bound has plain normals and tangents (<c>UncompressedTangentFrame</c>).</summary>
	bool uncompressedTangentFrame = true;

	static Format FormatOfTarget( BackendTexture texture, bool srgb )
	{
		var format = BackendDevice.FormatOf( texture );
		return srgb && format.HasSrgb() ? format.Srgb() : format;
	}

	static readonly StringToken ForwardMode = new( "Forward" );

	/// <summary>
	/// The name of the shader's mode a token names, or null if it has no such mode. A shader without <c>Forward</c> draws it
	/// with <c>VrForward</c>, as the material system's mode fallback does (blendable.shader has only that).
	/// </summary>
	static string ModeName( ShaderFile shader, StringToken mode )
	{
		foreach ( var candidate in shader.Modes )
		{
			if ( Token( candidate.Name ).Value == mode.Value ) return candidate.Name;
		}

		if ( mode.Value == ForwardMode.Value )
		{
			foreach ( var candidate in shader.Modes )
			{
				if ( string.Equals( candidate.Name, "VrForward", StringComparison.OrdinalIgnoreCase ) ) return candidate.Name;
			}
		}

		return null;
	}

	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, StringToken> tokens = new();

	internal static StringToken Token( string name ) => tokens.GetOrAdd( name, n => new StringToken( n ) );

	/// <summary>
	/// The material's variant for a mode with this context's dynamic combo values (<c>D_*</c>, set as combos).
	/// </summary>
	ShaderVariant Variant( BackendMaterial material, string mode )
	{
		var key = new System.Text.StringBuilder( mode );
		foreach ( var type in (ReadOnlySpan<ShaderProgramType>)[ShaderProgramType.Vertex, ShaderProgramType.Pixel] )
		{
			foreach ( var combo in material.Shader[type]?.DynamicCombos ?? [] )
			{
				if ( LookupCombo( Token( combo.Name ) ) is int value && value != 0 ) key.Append( ';' ).Append( combo.Name ).Append( '=' ).Append( value );
			}
		}

		AppendSpecialization( key, material );
		return material.Variant( device.Shaders, mode, key.ToString(), name => LookupCombo( Token( name ) ), name => Integer( Lookup( Token( name ) ) ) );
	}

	/// <summary>The values of the material's specialization attributes that are set, for its variant key.</summary>
	void AppendSpecialization( System.Text.StringBuilder key, BackendMaterial material )
	{
		foreach ( var name in material.SpecializationAttributes )
		{
			if ( Integer( Lookup( Token( name ) ) ) is int value && value != 0 ) key.Append( ";#" ).Append( name ).Append( '=' ).Append( value );
		}
	}

	static int? Integer( object value ) => value switch
	{
		int i => i,
		uint u => (int)u,
		bool b => b ? 1 : 0,
		float f => (int)f,
		_ => null,
	};

	/// <summary>
	/// An attribute as floats, for constants: numbers, bools and vectors.
	/// </summary>
	static float[] Floats( object value ) => value switch
	{
		int i => [i],
		uint u => [u],
		float f => [f],
		bool b => [b ? 1 : 0],
		Vector2 v => [v.x, v.y],
		Vector3 v => [v.x, v.y, v.z],
		Vector4 v => [v.x, v.y, v.z, v.w],
		Color c => [c.r, c.g, c.b, c.a],
		Vector2Int v => [v.x, v.y],
		_ => null,
	};

	/// <summary>
	/// Bind a variant's sets 0 to 3 for a draw with <paramref name="material"/>.
	/// </summary>
	void BindSets( BackendMaterial material, ShaderVariant variant, bool depthOnly )
	{
		var r = recording;
		BindViewSet( r.List );
		var defaults = device.Defaults;
		Span<Binding> bindings = stackalloc Binding[64];

		for ( int set = 0; set < variant.Sets.Length; set++ )
		{
			if ( !variant.Sets[set].IsValid ) continue;
			if ( set == ShaderAbi.FragmentSet && depthOnly ) continue;

			var count = 0;
			foreach ( var resource in variant.Resources )
			{
				if ( resource.Set != set ) continue;
				if ( count == bindings.Length ) throw new InvalidOperationException( $"{variant} binds more than {bindings.Length} resources in set {set}" );

				var stage = set == ShaderAbi.FragmentSet ? variant.Pixel : variant.Compute ?? variant.Vertex;
				bindings[count++] = resource.Kind switch
				{
					ShaderResourceKind.MaterialConstants => MaterialConstants( material, stage, resource.Binding ),
					ShaderResourceKind.ConstantBuffer => ConstantBuffer( stage, resource ),
					ShaderResourceKind.Buffer => Lookup( Token( resource.Name ) ) is GpuBuffer { Resource: BackendBuffer buffer }
						? Binding.Storage( resource.Binding, buffer.Handle )
						: defaults.For( resource ),
					ShaderResourceKind.Texture => Texture( material, resource ),
					_ => defaults.For( resource ),
				};
			}

			r.List.SetBindings( set, variant.Sets[set], bindings[..count] );
		}

		// Pixel set skipped for depth-only draws: the pipeline has no pixel shader
		r.Binder = null;
	}

	/// <summary>
	/// A stage's material constants, from the material and this context's attributes over the shader's defaults, in
	/// upload memory.
	/// </summary>
	Binding MaterialConstants( BackendMaterial material, ShaderStage stage, int binding )
	{
		var size = stage.MaterialBuffer.Size;
		var allocation = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
		stage.WriteMaterialConstants( allocation.Data[..size], variable => variable.Source switch
		{
			VariableSource.Material or VariableSource.MaterialAndExpression => material.Value( variable.Name ),
			VariableSource.ShaderVersion => [material.ShaderVersion],
			// Per draw from its vertex layout, as native's BindModelDrawCall sets it
			VariableSource.Attribute when variable.Attribute == "UncompressedTangentFrame" => [uncompressedTangentFrame ? 1 : 0],
			VariableSource.Attribute => Floats( Lookup( Token( variable.Attribute ) ) ),
			_ => null,
		}, material.Features );
		return Binding.Uniform( binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>
	/// A constant buffer the engine fills, copied into upload memory at the size the shader was compiled with; zeros
	/// if it was never set.
	/// </summary>
	Binding ConstantBuffer( ShaderStage stage, in ShaderResource resource )
	{
		var data = Lookup( Token( resource.Name ) ) as byte[];
		var size = data?.Length ?? 0;
		foreach ( var layout in stage.Program.ConstantBuffers )
		{
			if ( layout.Name == resource.Name ) size = Math.Max( size, layout.Size );
		}

		if ( size == 0 ) return device.Defaults.For( resource );

		size = (size + 15) & ~15;
		var allocation = List.AllocateUpload( size, Gpu.Capabilities.UniformAlignment );
		allocation.Data.Clear();
		data?.CopyTo( allocation.Data );
		return Binding.Uniform( resource.Binding, allocation.Buffer, allocation.Offset, size );
	}

	/// <summary>
	/// A texture: the material's for material variables, else the attribute of its name, when it's ready to sample
	/// and the shape the shader expects; a default otherwise.
	/// </summary>
	Binding Texture( BackendMaterial material, in ShaderResource resource )
	{
		BackendTexture texture = null;
		if ( resource.Variable.Source == VariableSource.Material ) material.Textures.TryGetValue( resource.Variable.Name, out texture );
		else texture = Lookup( Token( resource.Name ) ) as BackendTexture;

		var expected = resource.Dimension switch
		{
			SpirvImageDimension.Cube => TextureDimension.TextureCube,
			SpirvImageDimension.CubeArray => TextureDimension.TextureCubeArray,
			SpirvImageDimension.Texture3D => TextureDimension.Texture3D,
			SpirvImageDimension.Texture2DArray => TextureDimension.Texture2DArray,
			_ => TextureDimension.Texture2D,
		};

		if ( texture is null || texture.Dimension != expected || texture.Samples > 1 || SampledLayoutOf( texture ) != ImageLayout.ShaderRead ) return device.Defaults.For( resource );

		// SrgbRead( true ) textures are compiled with render state id 1
		var srgb = resource.Variable.RenderStateId == 1 && Gpu.InfoOf( texture.Handle ).Format.HasSrgb();
		return Binding.Sampled( resource.Binding, texture.Handle, srgb );
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

		// A multisampled source into a single-sampled copy (the frame buffer copy refraction reads) is resolved
		if ( from.Samples > 1 && to.Samples <= 1 ) List.ResolveTexture( from.Handle, to.Handle );
		else List.CopyTexture( from.Handle, to.Handle, 0, Math.Max( slices, 1 ) );
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

	public override bool BindVertexCacheIds( RenderMesh mesh, in RenderMesh.Draw draw ) => false;
	public override bool BindGeometry( RenderMesh mesh, in RenderMesh.Draw draw ) => false;

	public override bool SetRenderState( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool vertexCache = false ) => false;

	public override void BindInstances( int stream, int firstInstance ) { }

	/// <summary>
	/// Run a compute shader over <paramref name="x"/> by <paramref name="y"/> by <paramref name="z"/> threads (divided
	/// by its thread group size, as native does), with its bindings from this context's parameters.
	/// </summary>
	public override void Dispatch( GpuMaterial computeShader, int x, int y, int z )
	{
		if ( recording is null || computeShader is not BackendMaterial { Shader: { } shader } m ) return;
		if ( shader[ShaderProgramType.Compute] is null || shader.Modes.Count == 0 ) return;

		var variant = Variant( m, shader.Modes[0].Name );
		if ( variant?.Compute?.Bytecode is null ) return;

		CloseScope();
		if ( device.MaterialObjects )
		{
			DispatchWithMaterialObject( m, variant, x, y, z );
			return;
		}

		var r = recording;
		var pipeline = device.ComputePipeline( variant );
		r.List.SetPipeline( pipeline );
		r.Pipeline = default;
		BindSets( m, variant, depthOnly: false );

		var (gx, gy, gz) = variant.Compute.Bytecode.ThreadGroupSize;
		r.List.Dispatch( (x + Math.Max( gx, 1 ) - 1) / Math.Max( gx, 1 ), (y + Math.Max( gy, 1 ) - 1) / Math.Max( gy, 1 ), (z + Math.Max( gz, 1 ) - 1) / Math.Max( gz, 1 ) );
	}

	public override void DrawFullscreen( GpuMaterial material, ShaderParameters parameters, StringToken shaderMode ) { }

	public override void DrawIndexedInstanced( in RenderMesh.Draw draw, int instanceCount ) { }
}
