using Borealis.Culling;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// Borealis's recording context on a <see cref="CommandList"/>. Borealis binds targets and leaves the rest to the device, as
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

		/// <summary>The scope's second colour target, the opaque pass's normals and roughness (<see cref="StageTarget.Normals"/>).</summary>
		public BackendTexture Normals;
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
		readonly Dictionary<BackendTexture, (ImageLayout First, ImageLayout Current)> layouts = new();
		public readonly List<(BufferHandle Buffer, int Bytes, GpuReadback Callback)> Readbacks = new();

		/// <summary>The list's own scope in <see cref="BackendDevice.PassTimer"/>, -1 if it's not timed.</summary>
		public int TimerScope = -1;

		/// <summary>The timed scope counting statistics in this list, -1 if none: they don't nest.</summary>
		public int Counting = -1;

		/// <summary>Start again on a new list: a context keeps one session and reuses it every frame.</summary>
		public void Reset( CommandList list, bool ownLayouts )
		{
			List = list;
			Rendering = false;
			Color = Depth = null;
			Slice = 0;
			Srgb = false;
			Pipeline = default;
			Binder = Active = null;
			BindVersion = -1;
			layouts.Clear();
			Layouts = ownLayouts ? layouts : null;
			Readbacks.Clear();
			TimerScope = Counting = -1;
		}
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

	// Carrying a rendering scope across lists (CarryRendering): the first scope may resume the list before's, and scopes
	// suspend for the next list to resume
	bool resumeFirst, suspendScopes;

	// This context's own session, reused each time it records a list of its own
	Session session;

	// The layout plan (PlanLayouts): where each texture this context's list used was when the list began, the last time the
	// frame had this shape. A list recorded beside others starts from these, moving textures in the list itself, rather than
	// leaving the moves to a fix-up list at submission.
	readonly Dictionary<BackendTexture, ImageLayout> plannedLayouts = new();
	int planShape;
	bool planned;

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

	public override ToolsPass ToolsPass { get; set; }

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
			// A segment with a list of its own may record while others do: its layouts are its own until it's submitted
			var queue = AsyncCompute && Gpu.Capabilities.AsyncCompute ? QueueType.Compute : QueueType.Graphics;
			recording = session ??= new Session();
			recording.Reset( Gpu.BeginCommandList( queue, frame is null ? "Borealis frame" : "Borealis segment" ), ownLayouts: frame is not null );
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
		}

		wait = signal = default;
		boundValid = false;
		bound = default;
		clearColor = clearDepth = false;
		resumeFirst = suspendScopes = false;
		planned = false;
		viewConstants = null;
		passStateCount = 0;
		if ( frame is null ) viewSetWritten = false;
		OverlayStencil = OverlayStencil.None;
		ToolsPass = ToolsPass.None;
		SetObjectAttributes( null );
		recording.BindVersion = -1;
		recording.Active = this;
		CensusBegin();
	}

	public override void Submit( GpuSwapChain swapChain = default ) => SubmitNext();

	public override void SubmitNext()
	{
		if ( recording is null ) return;
		if ( Census ) CensusScope( null, pop: false );

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
			if ( recording.Layouts is not null ) SubmitLayoutFixUps( recording );
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

	internal override void CarryRendering( bool fromPrevious, bool intoNext )
	{
		// Only lists of their own, submitted together: a suspended scope can't cross a submission
		var carry = device.CarryScopes && device.BatchSubmits && !borrowed && recording is not null;
		resumeFirst = carry && fromPrevious;
		suspendScopes = carry && intoNext;
	}

	internal override void PlanLayouts( int shape )
	{
		if ( borrowed || recording?.Layouts is null ) return;

		// Another shape of frame: what the lists before leave where is learnt again at submission
		if ( shape != planShape || !device.LayoutPlans ) plannedLayouts.Clear();
		planShape = shape;
		planned = device.LayoutPlans;
	}

	/// <summary>
	/// The layout the plan says <paramref name="texture"/> is in when this list begins, for a list recorded beside others
	/// that hasn't used it yet: where the lists before left it the last time the frame had this shape.
	/// </summary>
	bool PlannedLayout( BackendTexture texture, out ImageLayout layout )
	{
		layout = default;
		return planned && plannedLayouts.TryGetValue( texture, out layout );
	}

	/// <summary>
	/// Before a segment's own list: move every texture it used from where the lists submitted before left it to what
	/// this one needs at its start, then remember where this one leaves it. Called in submission order. A planned list
	/// (<see cref="PlanLayouts"/>) moved its textures itself from where the plan said they'd be, so it needs no fix-ups
	/// while the plan holds; where they were is the plan for the next frame of this shape.
	/// </summary>
	void SubmitLayoutFixUps( Session session )
	{
		if ( planned ) plannedLayouts.Clear();

		CommandList fixUps = null;
		var timed = -1;
		foreach ( var (texture, (first, current)) in session.Layouts )
		{
			if ( planned && texture.Layout != ImageLayout.Undefined ) plannedLayouts[texture] = texture.Layout;

			if ( texture.Layout != first )
			{
				if ( fixUps is null )
				{
					fixUps = Gpu.BeginCommandList( session.List.Queue, "Borealis layouts" );
					timed = device.PassTimer?.Begin( fixUps, fixUps.Name, PassTimer.ScopeKind.List ) ?? -1;
				}

				fixUps.Barrier( LayoutBarrier( texture.Handle, texture.Layout, first ) );
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
	/// lists before leave it (as planned, for a list recorded beside others).
	/// </summary>
	internal ImageLayout LayoutOf( BackendTexture texture )
	{
		if ( recording?.Layouts is { } local && local.TryGetValue( texture, out var known ) ) return known.Current;
		return PlannedLayout( texture, out var planned ) ? planned : texture.Layout;
	}

	/// <summary>
	/// A list recorded beside others using <paramref name="texture"/> for the first time: what it records from. Planned, it's
	/// where the lists before leave it; else nothing's known, and the list needs it in <paramref name="start"/> at its start.
	/// </summary>
	(ImageLayout First, ImageLayout Current) FirstUse( Dictionary<BackendTexture, (ImageLayout First, ImageLayout Current)> local, BackendTexture texture, ImageLayout start )
	{
		var known = PlannedLayout( texture, out var planned ) ? (planned, planned) : (start, start);
		local[texture] = known;
		return known;
	}

	/// <summary>
	/// Hand the list this context records into to another recorder on the device (the engine's drawing, in a camera's
	/// stage) to record into next, in order: what's open is closed and pending clears are done. Textures are where
	/// <see cref="LendLayout"/> says; tell this context where the other left them with <see cref="SetLayout"/>, then
	/// <see cref="Reclaim"/> it. The frame's view set is bound on it, so what's drawn there lights in the view: a segment's
	/// list of its own binds it only with its first mesh, and custom objects' segments may have none.
	/// </summary>
	internal CommandList Lend()
	{
		CloseScope();
		BindViewSet( List );
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
		return FirstUse( local, texture, start ).Current;
	}

	/// <summary>Where another recorder left <paramref name="texture"/> in the list it was lent (<see cref="Lend"/>).</summary>
	internal void SetLayout( BackendTexture texture, ImageLayout layout )
	{
		if ( recording?.Layouts is { } local ) local[texture] = (local.TryGetValue( texture, out var known ) ? known.First : PlannedLayout( texture, out var planned ) ? planned : texture.Layout, layout);
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
		plannedLayouts.Clear();
		session = null;
	}

	//
	// The view set
	//

	// A frame context's view set (ShaderAbi.ViewSet): a new one each frame, the last destroyed once the GPU is done with it
	BindingSetHandle viewSet;
	bool viewSetWritten;
	Binding[] viewBindings;

	/// <summary>
	/// Write the view set from this frame context's attributes - the environment's blocks copied into the frame's pass
	/// constants, buffers by name, placeholders for what isn't set - and bind it for everything this frame records after.
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
			if ( name == ShaderAbi.ViewEnvironment )
			{
				// The environment's blocks, each from the constant buffer set by its name, back to back
				var offset = cache.AllocatePassConstants( ShaderAbi.ViewEnvironmentSize, out var target );
				target.Clear();
				foreach ( var (block, blockOffset, blockSize) in ShaderAbi.ViewEnvironmentBlocks )
				{
					if ( attributes.Get( Token( block ) ) is byte[] data )
						data.AsSpan( 0, Math.Min( data.Length, blockSize ) ).CopyTo( target[blockOffset..] );
				}

				// Then the lookup textures' bindless indices, read through the bindless set
				var lookups = MemoryMarshal.Cast<byte, uint>( target[ShaderAbi.ViewEnvironmentLookupsOffset..] );
				for ( int l = 0; l < ShaderAbi.ViewEnvironmentLookups.Length; l++ )
				{
					lookups[l] = attributes.Get( Token( ShaderAbi.ViewEnvironmentLookups[l] ) ) is BackendTexture texture
						? (uint)texture.BindlessIndex
						: (uint)Math.Max( Gpu.BindlessIndex( device.Defaults.Texture( SpirvImageDimension.Texture2D ) ), 0 );
				}

				viewBindings[i] = Binding.Uniform( binding, cache.PassBuffer, offset, ShaderAbi.ViewEnvironmentSize );
				continue;
			}

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
				viewBindings[i] = value is GpuBuffer { Resource: BackendBuffer { Live: true } buffer } ? Binding.Storage( binding, buffer.Handle ) : Binding.Storage( binding, device.Defaults.Storage );
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
		if ( boundValid && target.Color == bound.Color && target.Depth == bound.Depth && target.Normals == bound.Normals && target.DepthSlice == bound.DepthSlice && target.SrgbWrite == bound.SrgbWrite )
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
		return r.Rendering && r.Color == bound.Color && r.Depth == bound.Depth && r.Normals == bound.Normals && r.Slice == bound.DepthSlice && r.Srgb == bound.SrgbWrite;
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

		// The opaque pass's normals go beside colour only
		var normals = color is null ? null : bound.Normals as BackendTexture;
		if ( color is not null ) Transition( color, ImageLayout.ColorTarget );
		if ( normals is not null ) Transition( normals, ImageLayout.ColorTarget );
		if ( depth is not null ) Transition( depth, ImageLayout.DepthTarget );

		Span<ColorTarget> colors = stackalloc ColorTarget[2];
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

		// Cleared with colour, to nothing written
		if ( normals is not null )
		{
			colors[colorCount++] = new ColorTarget
			{
				Texture = normals.Handle,
				Load = clearColor ? LoadOp.Clear : LoadOp.Load,
				Store = StoreOp.Store,
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

		// A scope that clears begins anew; one carried from the list before resumes it
		var flags = (resumeFirst && !clearColor && !clearDepth ? RenderingFlags.Resume : RenderingFlags.None) | (suspendScopes ? RenderingFlags.Suspend : RenderingFlags.None);
		resumeFirst = false;
		clearColor = clearDepth = false;
		r.List.BeginRendering( colors[..colorCount], depthTarget, flags );
		r.Rendering = true;
		r.Color = color;
		r.Depth = depth;
		r.Normals = normals;
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

		// A list recorded beside others: its first use of a texture is what it needs, moved there before it's submitted,
		// unless the plan says where the lists before leave it
		var local = recording.Layouts;
		var known = default( (ImageLayout First, ImageLayout Current) );
		if ( local is not null && !local.TryGetValue( texture, out known ) )
		{
			if ( !PlannedLayout( texture, out var planned ) )
			{
				local[texture] = (layout, layout);
				return;
			}

			local[texture] = known = (planned, planned);
		}

		var from = local is null ? texture.Layout : known.Current;
		if ( from == layout && !always ) return;
		if ( recording.Rendering ) CloseScope();

		// A barrier can't sit between a suspended scope and its resumption: this list's scopes begin anew
		resumeFirst = false;
		recording.List.Barrier( LayoutBarrier( texture.Handle, from, layout ) );
		if ( local is null ) texture.Layout = layout;
		else local[texture] = (known.First, layout);
	}

	const MemoryAccess Writes = MemoryAccess.ShaderWrite | MemoryAccess.ColorTargetWrite | MemoryAccess.DepthTargetWrite | MemoryAccess.TransferWrite | MemoryAccess.HostWrite | MemoryAccess.MemoryWrite;
	const PipelineStage Shaders = PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader;
	const PipelineStage DepthTests = PipelineStage.EarlyFragmentTests | PipelineStage.LateFragmentTests;

	/// <summary>
	/// The stages that use a texture in <paramref name="layout"/>, and how: what a barrier moving it out of the layout waits
	/// for (of which the writes are made available), and what one moving it in blocks (the accesses made visible).
	/// </summary>
	static (PipelineStage Stages, MemoryAccess Access) UseOf( ImageLayout layout ) => layout switch
	{
		ImageLayout.ColorTarget => (PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetRead | MemoryAccess.ColorTargetWrite),
		ImageLayout.DepthTarget => (DepthTests, MemoryAccess.DepthTargetRead | MemoryAccess.DepthTargetWrite),
		ImageLayout.DepthReadOnly => (DepthTests | Shaders, MemoryAccess.DepthTargetRead | MemoryAccess.ShaderRead),
		ImageLayout.ShaderRead => (Shaders, MemoryAccess.ShaderRead),
		ImageLayout.General => (Shaders, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite),
		ImageLayout.CopySource => (PipelineStage.Transfer, MemoryAccess.TransferRead),
		ImageLayout.CopyDestination => (PipelineStage.Transfer, MemoryAccess.TransferWrite),
		_ => (PipelineStage.None, MemoryAccess.None),
	};

	/// <summary>
	/// Move a texture between layouts: with <see cref="BackendDevice.ExactBarriers"/>, waiting for the stages of its use in
	/// <paramref name="from"/> and their writes, and blocking those of its use in <paramref name="to"/>; else waiting for
	/// everything. From <see cref="ImageLayout.Undefined"/> it waits for everything (a texture's first use); from
	/// <see cref="ImageLayout.Present"/> for the stages it's moved for, which the swap chain's acquire wait covers.
	/// </summary>
	Borealis.Gpu.TextureBarrier LayoutBarrier( TextureHandle texture, ImageLayout from, ImageLayout to )
	{
		if ( !device.ExactBarriers )
			return Borealis.Gpu.TextureBarrier.Transition( texture, from, to, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );

		var (dstStages, dstAccess) = UseOf( to );
		var (srcStages, srcAccess) = from switch
		{
			ImageLayout.Undefined => (PipelineStage.AllCommands, MemoryAccess.None),
			ImageLayout.Present => (dstStages, MemoryAccess.None),
			_ => UseOf( from ),
		};

		return Borealis.Gpu.TextureBarrier.Transition( texture, from, to, srcStages, srcAccess & Writes, dstStages, dstAccess );
	}

	/// <summary>As <see cref="LayoutBarrier"/>, for one mip of the first layer.</summary>
	Borealis.Gpu.TextureBarrier MipBarrier( TextureHandle texture, int mip, ImageLayout from, ImageLayout to ) => LayoutBarrier( texture, from, to ) with { BaseMip = mip, MipCount = 1, LayerCount = 1 };

	static PipelineStage Map( GpuStage stage )
	{
		var stages = PipelineStage.None;
		if ( (stage & GpuStage.DrawIndirect) != 0 ) stages |= PipelineStage.DrawIndirect;
		if ( (stage & GpuStage.VertexInput) != 0 ) stages |= PipelineStage.VertexInput;
		if ( (stage & GpuStage.PreRasterizationShaders) != 0 ) stages |= PipelineStage.PreRasterizationShaders;
		if ( (stage & GpuStage.FragmentShader) != 0 ) stages |= PipelineStage.FragmentShader;
		if ( (stage & GpuStage.EarlyFragmentTests) != 0 ) stages |= PipelineStage.EarlyFragmentTests;
		if ( (stage & GpuStage.LateFragmentTests) != 0 ) stages |= PipelineStage.LateFragmentTests;
		if ( (stage & GpuStage.ColorAttachmentOutput) != 0 ) stages |= PipelineStage.ColorTargetOutput;
		if ( (stage & GpuStage.ComputeShader) != 0 ) stages |= PipelineStage.ComputeShader;
		if ( (stage & GpuStage.Transfer) != 0 ) stages |= PipelineStage.Transfer;
		if ( (stage & GpuStage.AllGraphics) != 0 ) stages |= PipelineStage.AllGraphics;
		return stages;
	}

	static MemoryAccess Map( GpuAccess access )
	{
		var accesses = MemoryAccess.None;
		if ( (access & GpuAccess.IndirectCommandRead) != 0 ) accesses |= MemoryAccess.IndirectRead;
		if ( (access & GpuAccess.IndexRead) != 0 ) accesses |= MemoryAccess.IndexRead;
		if ( (access & GpuAccess.VertexAttributeRead) != 0 ) accesses |= MemoryAccess.VertexRead;
		if ( (access & GpuAccess.UniformRead) != 0 ) accesses |= MemoryAccess.UniformRead;
		if ( (access & (GpuAccess.ShaderRead | GpuAccess.InputAttachmentRead)) != 0 ) accesses |= MemoryAccess.ShaderRead;
		if ( (access & GpuAccess.ShaderWrite) != 0 ) accesses |= MemoryAccess.ShaderWrite;
		if ( (access & GpuAccess.ColorAttachmentRead) != 0 ) accesses |= MemoryAccess.ColorTargetRead;
		if ( (access & GpuAccess.ColorAttachmentWrite) != 0 ) accesses |= MemoryAccess.ColorTargetWrite;
		if ( (access & GpuAccess.DepthStencilAttachmentRead) != 0 ) accesses |= MemoryAccess.DepthTargetRead;
		if ( (access & GpuAccess.DepthStencilAttachmentWrite) != 0 ) accesses |= MemoryAccess.DepthTargetWrite;
		if ( (access & GpuAccess.TransferRead) != 0 ) accesses |= MemoryAccess.TransferRead;
		if ( (access & GpuAccess.TransferWrite) != 0 ) accesses |= MemoryAccess.TransferWrite;
		return accesses;
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
		if ( texture is not BackendTexture t ) return;
		Barrier( t, Map( layout, t ) );

		// The caller's stages too, beyond what the layouts imply (a compute write left in General): a memory barrier beside it
		if ( !device.ExactBarriers || recording is null ) return;
		var (src, dst) = (Map( srcStage ), Map( dstStage ));
		if ( src != PipelineStage.None && dst != PipelineStage.None ) List.Barrier( src, Map( srcAccess ) & Writes, dst, Map( dstAccess ) );
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
		var (src, dst) = (Map( srcStage ), Map( dstStage ));
		if ( device.ExactBarriers && src != PipelineStage.None && dst != PipelineStage.None ) List.Barrier( src, Map( srcAccess ) & Writes, dst, Map( dstAccess ) );
		else List.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
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
		ToolsPass = ToolsPass.None;
	}

	public override void SetConstants<T>( StringToken name, in T value ) => attributes.SetData( name, value );

	public override void SetConstantBuffer<T>( StringToken name, IntPtr buffer, in T value ) => attributes.SetData( name, value );

	public override GpuScope BeginGpuScope( string name )
	{
		if ( recording is null ) return default;
		if ( Census ) CensusScope( name, pop: false );
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
		if ( Census ) CensusScope( null, pop: true );
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
		if ( material is not BackendMaterial { Shader: { } shader } m || mesh.Geometry is not IBackendGeometry source || !source.Use() ) return false;
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
		var geometry = DepthGeometry( GeometryFor( source, draw ), variant, depthOnly );
		var pipeline = device.Pipeline( variant, geometry.Layout, noColor ? Format.Unknown : color, r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth ), (r.Color ?? r.Depth).Samples, depthOnly, normals: !noColor && r.Normals is not null, tools: noColor ? ToolsPass.None : ToolsPass );

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
	/// Whether a draw goes without its pixel shader: when Borealis strips it, or in a pass without colour (a prepass, a shadow
	/// map) for a material whose depth needs none. Alpha-tested and translucent materials keep it there, as native does when
	/// Borealis doesn't strip it: their depth comes from what it clips (alpha test, alpha to coverage).
	/// </summary>
	static bool StripsPixelShader( BackendMaterial material, bool noColor, bool stripPixelShader )
		=> stripPixelShader || (noColor && !material.IsAlphaTest && !material.IsTranslucent);

	/// <summary>
	/// A depth-only draw fetches its vertices from the positions-only copy, when the model has one, if that changes nothing
	/// the vertex shader reads: every input but the position is one the mesh's own vertices don't have either (the instance
	/// and vertex id streams, or the default stream).
	/// </summary>
	DrawGeometry DepthGeometry( in DrawGeometry geometry, ShaderVariant variant, bool depthOnly )
	{
		if ( !depthOnly || !device.PositionStreams || !geometry.Positions.IsValid || !ReadsOnlyPositions( variant, geometry.Layout ) ) return geometry;
		return geometry.PositionsOnly;
	}

	/// <summary>Whether a vertex shader reads nothing of these vertices but their positions.</summary>
	static bool ReadsOnlyPositions( ShaderVariant variant, VertexLayout layout )
	{
		// By index: a foreach over the list would allocate its enumerator every draw
		var inputs = variant.VertexInputs;
		for ( int i = 0; i < inputs.Count; i++ )
		{
			var input = inputs[i];
			if ( input.Semantic != VertexSemantic.Position && layout.Find( input.Semantic, input.SemanticIndex ) is not null ) return false;
		}

		return true;
	}

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
	/// with <c>VrForward</c>, as the material system's mode fallback does (blendable.shader has only that), and the error
	/// shader every mode with its one.
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

		// The error shader draws every mode with its one (ShaderFile.Mode)
		return shader.IsErrorShader && shader.Modes.Count > 0 ? shader.Modes[0].Name : null;
	}

	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, StringToken> tokens = new();

	internal static StringToken Token( string name ) => tokens.GetOrAdd( name, n => new StringToken( n ) );

	/// <summary>
	/// The material's variant for a mode with this context's dynamic combo values (<c>D_*</c>, set as combos).
	/// </summary>
	ShaderVariant Variant( BackendMaterial material, string mode )
	{
		// The key DrawWithMaterialObject uses, made once per values; the lookups made once per context
		comboLookup ??= name => LookupCombo( Token( name ) );
		specializationLookup ??= name => Integer( Lookup( Token( name ) ) );
		return material.Variant( device.Shaders, mode, ComboKeyByValue( material, mode ), comboLookup, specializationLookup );
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
	/// An attribute as floats, for constants: numbers, bools, vectors and matrices (in the order view constants hold them).
	/// </summary>
	/// <summary>
	/// <see cref="Floats"/> into an array this thread reuses for the count, so reading attributes every pass allocates
	/// nothing: the caller reads it before its next call.
	/// </summary>
	internal static float[] ScratchFloats( object value )
	{
		switch ( value )
		{
			case int i: return Scratch( 1, i );
			case uint u: return Scratch( 1, u );
			case float f: return Scratch( 1, f );
			case bool b: return Scratch( 1, b ? 1 : 0 );
			case Vector2 v: return Scratch( 2, v.x, v.y );
			case Vector3 v: return Scratch( 3, v.x, v.y, v.z );
			case Vector4 v: return Scratch( 4, v.x, v.y, v.z, v.w );
			case Color c: return Scratch( 4, c.r, c.g, c.b, c.a );
			case Vector2Int v: return Scratch( 2, v.x, v.y );
			case Matrix m:
				var a = Scratch( 16 );
				(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7]) = (m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24);
				(a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15]) = (m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
				return a;
			default: return Floats( value );
		}
	}

	/// <summary>
	/// An attribute's value as floats on <paramref name="into"/>: in the arrays it owns for one to four (<see cref="ShaderAttributes.Set(string, float)"/>),
	/// so setting it every dispatch allocates nothing; a matrix in an array of its own.
	/// </summary>
	static void SetFloats( ShaderAttributes into, string name, object value )
	{
		if ( ScratchFloats( value ) is not { } f ) return;
		switch ( f.Length )
		{
			case 1: into.Set( name, f[0] ); break;
			case 2: into.Set( name, f[0], f[1] ); break;
			case 3: into.Set( name, f[0], f[1], f[2] ); break;
			case 4: into.Set( name, f[0], f[1], f[2], f[3] ); break;
			default: into.Set( name, (float[])f.Clone() ); break;
		}
	}

	[ThreadStatic] static float[][] scratchFloats;

	static float[] Scratch( int count, float x = 0, float y = 0, float z = 0, float w = 0 )
	{
		scratchFloats ??= new float[17][];
		var array = scratchFloats[count] ??= new float[count];
		if ( count <= 4 )
		{
			array[0] = x;
			if ( count > 1 ) array[1] = y;
			if ( count > 2 ) array[2] = z;
			if ( count > 3 ) array[3] = w;
		}

		return array;
	}

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
		Matrix m => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44],
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
			foreach ( var resource in variant.ResourceSpan )
			{
				if ( resource.Set != set ) continue;
				if ( count == bindings.Length ) throw new InvalidOperationException( $"{variant} binds more than {bindings.Length} resources in set {set}" );

				var stage = set == ShaderAbi.FragmentSet ? variant.Pixel : variant.Compute ?? variant.Vertex;
				bindings[count++] = resource.Kind switch
				{
					ShaderResourceKind.MaterialConstants => MaterialConstants( material, stage, resource.Binding ),
					ShaderResourceKind.ConstantBuffer => ConstantBuffer( stage, resource ),
					ShaderResourceKind.Buffer => Lookup( Token( resource.Name ) ) is GpuBuffer { Resource: BackendBuffer { Live: true } buffer }
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

		if ( texture is not { IsValid: true } || texture.Dimension != expected || texture.Samples > 1 || SampledLayoutOf( texture ) != ImageLayout.ShaderRead ) return device.Defaults.For( resource );

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

		var readback = Gpu.CreateBuffer( new BufferInfo { Size = bytes, Memory = MemoryLocation.Readback, Name = "Borealis readback" } );
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

	/// <summary>Recorded with the rest of the work, rather than the device's clear, which waits for the GPU.</summary>
	public override void ClearTexture( GpuTexture texture, Color color )
	{
		if ( recording is null || texture is not BackendTexture t ) return;
		CloseScope();
		Transition( t, ImageLayout.CopyDestination, always: true );
		List.ClearTexture( t.Handle, new System.Numerics.Vector4( color.r, color.g, color.b, color.a ) );
	}

	public override void CopyTextureRegion( GpuTexture source, GpuTexture destination, int sourceX, int sourceY, int destinationX, int destinationY, int width, int height )
	{
		if ( recording is null || source is not BackendTexture from || destination is not BackendTexture to ) return;
		CloseScope();
		Transition( from, ImageLayout.CopySource, always: true );
		Transition( to, ImageLayout.CopyDestination, always: true );
		List.CopyTextureRegion( from.Handle, to.Handle, sourceX, sourceY, destinationX, destinationY, width, height );
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

		// Its mips for blurred refraction, then readable by the draws after it: they bind only what's readable
		if ( frameBufferCopy is BackendTexture copy )
		{
			if ( device.FrameBufferCopyMips ) GenerateFrameBufferCopyMips( copy, viewport );
			Transition( copy, ImageLayout.ShaderRead );
		}

		attributes.Set( FrameBufferCopyTexture, frameBufferCopy );
		attributes.Set( FrameBufferCopyRectangle, new Vector4( 0, 0, viewport.Width / frameBufferCopy.Width, viewport.Height / frameBufferCopy.Height ) );
	}

	//
	// What only Source 2 has: skipped, as the headless device skips a mesh with no geometry
	//

	public override bool BindVertexCacheIds( RenderMesh mesh, in RenderMesh.Draw draw ) => false;
	public override bool BindGeometry( RenderMesh mesh, in RenderMesh.Draw draw ) => false;

	public override bool SetRenderState( GpuMaterial material, StringToken shaderMode, RenderMesh mesh, in RenderMesh.Draw draw, int depthBias = 0, float slopeScaledDepthBias = 0, bool stripPixelShader = false, bool vertexCache = false ) => false;

	/// <summary>
	/// Where stream 1's instance ids start, as a custom object's run binds them before it draws: draws record their own ids,
	/// so only draws through the engine's graphics context into this context's list read it (<see cref="LentInstances"/>).
	/// </summary>
	public override void BindInstances( int stream, int firstInstance )
	{
		if ( stream == 1 ) boundFirstInstance = firstInstance;
	}

	int boundFirstInstance;

	/// <summary>
	/// The frame's transform buffer, and its instance ids from where <see cref="BindInstances"/> last put stream 1: what a
	/// custom object drawn through the engine's graphics context reads its transform with (<c>g_TransformBuffer</c>,
	/// indexed by <c>TEXCOORD13</c>). Default handles before a frame has uploaded its transforms.
	/// </summary>
	internal (BufferHandle Transforms, BufferHandle Ids, long Offset) LentInstances
	{
		get
		{
			var transforms = attributes.Get( TransformBufferName ) is GpuBuffer { Resource: BackendBuffer buffer } ? buffer.Handle : default;
			if ( InstanceIds?.Resource is not BackendBuffer ids ) return (transforms, default, 0);
			return (transforms, ids.Handle, boundFirstInstance * (long)sizeof( uint ));
		}
	}

	static readonly StringToken TransformBufferName = new( "g_TransformBuffer" );

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

	readonly List<BackendTexture> fullscreenTextures = new();

	/// <summary>
	/// Draw <paramref name="material"/> over the bound target, as <c>Graphics.Blit</c> does: a quad in clip space
	/// (<see cref="BackendDevice.FullscreenQuad"/>) with <paramref name="parameters"/> over this context's, in
	/// <paramref name="shaderMode"/> (else the material's first mode). A frame's flipped output is copied this way.
	/// </summary>
	public override void DrawFullscreen( GpuMaterial material, ShaderParameters parameters, StringToken shaderMode )
	{
		if ( recording is null || material is not BackendMaterial { Shader: { } shader } || shader.Modes.Count == 0 ) return;

		var mode = ModeName( shader, shaderMode ) is null ? Token( shader.Modes[0].Name ) : shaderMode;

		// What it samples is read where it was drawn: a frame's colour, moved there first, as Source 2's blit does
		if ( parameters is BackendParameters own )
		{
			fullscreenTextures.Clear();
			own.TexturesInto( fullscreenTextures );
			foreach ( var texture in fullscreenTextures ) Transition( texture, ImageLayout.ShaderRead );
			fullscreenTextures.Clear();
		}

		var quad = device.FullscreenQuad;
		SetObjectAttributes( parameters );
		DrawModel( material, mode, quad, quad.DrawsForLod( 0 )[0], 0, 1 );
		SetObjectAttributes( null );
	}

	public override void DrawIndexedInstanced( in RenderMesh.Draw draw, int instanceCount ) { }
}
