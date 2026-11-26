using NativeEngine;
using Sandbox.Engine;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Used to render to the screen using your Graphics Card, or whatever you
/// kids are using in your crazy future computers. Whatever it is I'm sure
/// it isn't fungible and everyone has free money and no-one has to ever work.
/// </summary>
[Expose]
public static partial class Graphics
{
	/// <summary>
	/// Whether the initialized renderer supports GPU operations. False before initialization,
	/// after shutdown, and when using the empty renderer.
	/// </summary>
	public static bool IsAvailable { get; private set; }

	/// <summary>
	/// Whether the process draws on a Borealis.Gpu device of its own (<c>-borealisgpu</c>, which the game and the editor add
	/// themselves: <c>AppSystem.DrawOnBorealisGpu</c>).
	/// </summary>
	internal static bool BorealisGpuRequested => Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu" );

	internal static void Initialize()
	{
		// With -borealisgpu the process renders even on Source 2's empty render device: Borealis.Gpu draws
		var borealisGpu = BorealisGpuRequested;
		// Source 2's device only describes resources: what draws is Borealis.Gpu, or nothing (HeadlessRenderBackend, the default:
		// buffers keep their bytes and textures read back from their files)
		IsAvailable = borealisGpu;

		// -borealisgpu: the process draws on a Borealis.Gpu device of its own (docs/reference/borealis-gpu-path.md, stage 2)
		if ( IsAvailable && RenderBackend.Default is HeadlessRenderBackend )
		{
			var validation = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-validation" );
			// With GPU-assisted validation as well (buffer bounds checked in the shaders; slow), for out-of-range reads that hang the GPU
			var gpuAssisted = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-gpuav" );
			// With the layer's descriptor checks too (each descriptor a shader reads: in range, written, the image in the layout
			// it's read in), the only check of what bindless arrays read. They reset RDNA4, and lost the A10 with layer 1.4.309
			// (1.4.363 runs)
			var gpuAssistedDescriptors = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-gpuav-descriptors" );
			// With synchronization validation as well (hazards between passes the barriers miss; slow)
			var syncValidation = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-syncvalidation" );
			// Progress markers for the device-loss report at each list's ends, unless -borealisgpu-nomarkers; at each pass's too
			// with -borealisgpu-passmarkers. Not with synchronization validation: markers write their words unordered on purpose
			// (only read after a loss), and the layer reports every one
			var markers = !Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-nomarkers" ) && !syncValidation;
			var passMarkers = markers && Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-passmarkers" );
			// The device's pipeline cache in game/.source2/borealispipelines, apart from native's, unless -borealisgpu-nopipelinecache.
			// A project's editor keeps its own: the driver holds the whole loaded cache in memory, and one shared by every project
			// opened grew to a GiB, the prewarmed draws of the others' with it
			var pipelineCache = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-nopipelinecache" ) ? null : System.IO.Path.Combine( EngineFileSystem.Root.GetFullPath( "/" ), ".source2", "borealispipelines" );
			if ( pipelineCache is not null && Sandbox.Utility.CommandLine.GetSwitch( "-project", "" ).TrimQuoted() is { Length: > 0 } project )
				pipelineCache = System.IO.Path.Combine( pipelineCache, $"{System.IO.Path.GetFileNameWithoutExtension( project )}-{System.IO.Path.GetFullPath( project ).ToLowerInvariant().FastHash():x8}" );

			// Slang surfaces compile on workers while their materials draw as their .shader, and the engine's start warming at
			// once, unless -wait-for-shaders (draws wait for each, as tests and captures do)
			Borealis.Backend.SurfaceShaders.CompileInBackgroundByDefault = !Sandbox.Utility.CommandLine.HasSwitch( "-wait-for-shaders" );
			_borealisGpu = BorealisGpuRenderBackend.Create( validation, m => Log.Info( $"Borealis.Gpu: {m.Severity} {m.Text}" ), presentation: true, frames: true, markers: markers, pipelineCacheFolder: pipelineCache, gpuAssisted: gpuAssisted, syncValidation: syncValidation, gpuAssistedDescriptors: gpuAssistedDescriptors, passMarkers: passMarkers );

			// Models load in the background, as Source 2 streams them, unless -borealisgpu-syncload
			_borealisGpu.Services.AsyncModels = !Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-syncload" );

			// Material textures stream with them, tails first, unless -borealisgpu-nostream; -borealisgpu-texturebudget <MiB> caps them
			_borealisGpu.Services.StreamTextures = _borealisGpu.Services.AsyncModels && !Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-nostream" );
			if ( Sandbox.Utility.CommandLine.GetSwitchInt( "-borealisgpu-texturebudget", 0 ) is > 0 and var budget ) _borealisGpu.Services.TextureBudgetBytes = (long)budget << 20;
			_borealisGpu.Services.Surfaces.Warm();
			_borealisGpu.Services.Passes.Warm();

			// The pass module stages the last runs' draws asked for, read ahead (or compiled, after an update) on workers
			_borealisGpu.Services.Passes.Prewarm();
			RenderBackend.SetDefault( _borealisGpu );
			var adapter = _borealisGpu.Device.Adapter;
			SystemInfo.SetBorealisGpu( adapter.Name, adapter.DriverVersion, adapter.DeviceLocalBytes, adapter.Integrated );
			Log.Info( $"Rendering on Borealis.Gpu ({_borealisGpu.Device.Adapter.Name}){(gpuAssisted ? ", GPU-assisted validation on" : validation ? ", validation on" : "")}; memory: {_borealisGpu.Device.DescribeMemory()}" );
		}

		if ( IsAvailable )
			Cmaa2Renderer.Initialize();
	}

	// The process's Borealis.Gpu device, with -borealisgpu
	static BorealisGpuRenderBackend _borealisGpu;

	/// <summary>The compiled content the process's Borealis.Gpu device reads, or null without one.</summary>
	internal static Borealis.Source2.ContentFiles BorealisGpuContent => _borealisGpu?.Services.Content;

	internal static Borealis.Gpu.Device BorealisGpuDevice => _borealisGpu?.Device;

	/// <summary>
	/// No more frames: what the process's Borealis.Gpu device kept of the last one's draws (their attributes and material)
	/// goes, ahead of the shutdown leak check, though the device itself stays until <see cref="Shutdown"/>.
	/// </summary>
	internal static void ReleaseFrameState() => _borealisGpu?.ReleaseFrameState();

	internal static void Shutdown()
	{
		Cmaa2Renderer.Shutdown();

		if ( _borealisGpu is not null )
		{
			// The windows' swap chains go first, once the render thread has drawn the frame handed to it
			BorealisGpuRenderBackend.JoinRenderThread();
			BorealisGpuWindow.DisposeAll();
			RenderBackend.SetDefault( null );
			_borealisGpu.Dispose();
			_borealisGpu = null;
		}

		IsAvailable = false;
	}

	public enum PrimitiveType
	{
		Points = NativeEngine.RenderPrimitiveType.RENDER_PRIM_POINTS,
		Lines = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES,
		LinesWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES_WITH_ADJACENCY,
		LineStrip = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINE_STRIP,
		LineStripWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINE_STRIP_WITH_ADJACENCY,
		Triangles = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES,
		TrianglesWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES_WITH_ADJACENCY,
		TriangleStrip = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP,
		TriangleStripWithAdjacency = NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP_WITH_ADJACENCY,
	}

	/// <summary>
	/// If true then we're currently rendering and
	/// you are safe to use the contents of this class
	/// </summary>
	public static bool IsActive => _state.active;

	/// <summary>
	/// The current layer type. This is useful to tell whether you're meant to be drawing opaque, transparent or shadow. You mainly
	/// don't need to think about this, but when you do, it's here.
	/// </summary>
	public static SceneLayerType LayerType => _state.layerType;

	struct RenderState
	{
		public bool active;
		public IFrameView view;
		public SceneLayerType layerType;
		public RenderAttributes attributes;
		public ImageFormat colorFormat;
		public MultisampleAmount msaaLevel;
		public Transform cameraTransform;
		internal RenderTarget renderTarget;
		internal float defaultMinZ;
		internal float defaultMaxZ;
		internal RenderBackend backend;
	}

	[ThreadStatic]
	private static RenderState _state;

	/// <summary>
	/// The device the block draws on, or the process's outside one.
	/// </summary>
	internal static RenderBackend Backend => _state.backend ?? RenderBackend.Default;

	/// <summary>
	/// Make <paramref name="backend"/> the device outside render blocks on this thread - where a recording that will draw
	/// on it uploads its buffers and resolves its bindless indices - until the returned one is put back. Null is the
	/// process's.
	/// </summary>
	internal static RenderBackend SetRecordingBackend( RenderBackend backend )
	{
		var previous = _state.backend;
		_state.backend = backend;
		return previous;
	}

	internal static ImageFormat IdealColorFormat => _state.colorFormat;
	internal static MultisampleAmount IdealMsaaLevel => _state.msaaLevel;

	/// <summary>
	/// When Frame grabbing, we store the result in this pooled render target. It stays checked out
	/// until the end of this render scope.
	/// </summary>
	[ThreadStatic]
	static HashSet<RenderTarget> grabbedTextures;

	/// <summary>
	/// In pixel size, where are we rendering to?
	/// </summary>
	public static Rect Viewport
	{
		get => Backend.GetViewport().Rect;
		set => Backend.SetViewport( value );
	}

	/// <summary>
	/// Access to the current render context's attributes. These will be used
	/// to set attributes in materials/shaders. This is cleared at the end of the render block.
	/// </summary>
	public static RenderAttributes Attributes => _state.attributes;

	/// <summary>
	/// The camera transform of the currently rendering view
	/// </summary>
	public static Transform CameraTransform => _state.cameraTransform;

	/// <summary>
	/// The camera position of the currently rendering view
	/// </summary>
	public static Vector3 CameraPosition => CameraTransform.Position;

	/// <summary>
	/// The camera rotation of the currently rendering view
	/// </summary>
	public static Rotation CameraRotation => CameraTransform.Rotation;

	/// <summary>
	/// GPU video memory budget in bytes, as reported by the OS (WDDM).
	/// </summary>
	public static ulong VideoMemoryBudget
	{
		get
		{
			return Graphics.IsAvailable ? BorealisGpuMemory().Budget : 0;
		}
	}

	/// <summary>
	/// GPU video memory currently used by the engine's render system in bytes.
	/// This includes textures, buffers, and all other GPU allocations tracked by the engine.
	/// </summary>
	public static ulong VideoMemoryUsed
	{
		get
		{
			return Graphics.IsAvailable ? BorealisGpuMemory().Used : 0;
		}
	}

	/// <summary>
	/// Without Source 2's device (its empty one reports nothing), the drawing device's largest device-local heap: what the
	/// process uses of it and the budget the OS gives it.
	/// </summary>
	static (ulong Used, ulong Budget) BorealisGpuMemory()
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend borealis ) return default;
		var (used, budget) = borealis.Device.DeviceLocalMemory();
		return ((ulong)Math.Max( used, 0 ), (ulong)Math.Max( budget, 0 ));
	}


	/// <summary>
	/// The field of view of the currently rendering camera view, in degrees.
	/// </summary>
	public static float FieldOfView => ViewFrustum.GetCameraFOV();

	/// <summary>
	/// The frustum of the currently rendering camera view.
	/// </summary>
	public static Frustum Frustum
	{
		get
		{
			AssertRenderBlock();
			return FrustumOf( ViewFrustum );
		}
	}

	/// <summary>A native frustum's planes in world space, as <see cref="Frustum"/> gives a view's.</summary>
	internal static Frustum FrustumOf( CFrustum cf )
	{
		// Native planes are camera-relative.
		cf.GetPlane( 0, out var rn, out var rd );
		cf.GetPlane( 1, out var ln, out var ld );
		cf.GetPlane( 2, out var tn, out var td );
		cf.GetPlane( 3, out var bn, out var bd );
		cf.GetPlane( 4, out var nn, out var nd );
		cf.GetPlane( 5, out var fn, out var fd );

		var origin = cf.GetCameraPosition();
		return new Frustum(
			right: new Plane( rn, rd + origin.Dot( rn ) ),
			left: new Plane( ln, ld + origin.Dot( ln ) ),
			top: new Plane( tn, td + origin.Dot( tn ) ),
			bottom: new Plane( bn, bd + origin.Dot( bn ) ),
			near: new Plane( nn, nd + origin.Dot( nn ) ),
			far: new Plane( fn, fd + origin.Dot( fn ) )
		);
	}


	/// <summary>
	/// The most multisampling up to <paramref name="wanted"/> the device renders with: Borealis.Gpu's answer when it's the
	/// device, else Source 2's.
	/// </summary>
	internal static RenderMultisampleType BestMultisampleType( RenderMultisampleType wanted )
		=> RenderBackend.Default is BorealisGpuRenderBackend borealis ? borealis.BestMultisampleType( wanted ) : RenderDeviceManager.GetBestMultisampleType( wanted );

	internal static int RenderMultiSampleToNum( RenderMultisampleType msaaLevel )
	{
		switch ( msaaLevel )
		{
			case RenderMultisampleType.RENDER_MULTISAMPLE_NONE:
				return 1;
			case RenderMultisampleType.RENDER_MULTISAMPLE_2X:
				return 2;
			case RenderMultisampleType.RENDER_MULTISAMPLE_4X:
				return 4;
			case RenderMultisampleType.RENDER_MULTISAMPLE_6X:
				return 6;
			case RenderMultisampleType.RENDER_MULTISAMPLE_8X:
				return 8;
			case RenderMultisampleType.RENDER_MULTISAMPLE_16X:
				return 16;
			default:
				throw new System.Exception( "Unknown multisample amount" );
		}
	}

	/// <summary>
	/// Creates a scope where Graphics is safe to use.
	/// </summary>
	internal ref struct Scope
	{
		RenderState _previous;
		RenderAttributes _attributes;
		RenderBackend _backend;

		/// <summary>
		/// Begins rendering with no scene view on the process's device: a block of its own with managed attributes, submitted
		/// on disposal.
		/// </summary>
		internal static Scope Create() => CreateOn( RenderBackend.Default );

		/// <summary>
		/// Begins rendering with no scene view on <paramref name="backend"/>'s device: the block records into its own list, with
		/// managed attributes, and disposal submits it.
		/// </summary>
		internal static Scope CreateOn( RenderBackend backend ) => CreateOnView( backend, null );

		/// <summary>
		/// A block on <paramref name="backend"/>'s device drawing into a camera's frame: what the code drawing in it asks a view
		/// (post processing on, the frustum, binding a render target and going back to the frame's) is <paramref name="view"/>'s.
		/// </summary>
		internal static Scope CreateOnView( RenderBackend backend, BorealisGpuView view )
		{
			ThreadSafe.AssertIsMainThread();

			if ( IsActive )
				throw new InvalidOperationException( "Standalone rendering cannot overlap another render scope." );

			var managed = backend.BeginBlock();
			var block = new Scope { _previous = _state, _attributes = managed, _backend = backend };
			// Its targets are single-sampled (a Borealis.Gpu frame hands over its resolved colour), unless the view says
			_state = new RenderState { active = true, attributes = managed, backend = backend, msaaLevel = view?.Msaa ?? MultisampleAmount.MultisampleNone };
			if ( view is not null )
			{
				_state.view = view;
				_state.layerType = view.LayerType;

				// What a frame grab or a temporary target of the default format is made with
				if ( view.Target?.ColorTarget is { } color ) _state.colorFormat = color.ImageFormat;
				_state.cameraTransform = new Transform( view.Frustum.GetCameraPosition(), view.Frustum.GetCameraAngles() );
			}

			return block;
		}

		/// <summary>The block's attributes.</summary>
		internal RenderAttributes Attributes => _attributes;

		public void Dispose()
		{
			if ( _backend is null ) return;

			try
			{
				_backend.EndBlock();
			}
			finally
			{
				_state = _previous;
				ReturnGrabbedTextures();
			}
		}

		static void ReturnGrabbedTextures()
		{
			if ( grabbedTextures is not null )
			{
				// return to the pool
				foreach ( var tex in grabbedTextures )
				{
					tex.Dispose();
				}
				grabbedTextures.Clear();
			}
		}
	}

	/// <summary>
	/// A block in which a game's drawing is recorded rather than drawn (<see cref="Rendering.GameDrawExtract"/>): <see cref="Backend"/>
	/// is the recording backend, which keeps every call in the recording, the view is a camera's (as its block was when last
	/// drawn), the attributes are the recording's own, and a temporary target the drawing grabs or gives back stays with the
	/// recording until it's replayed.
	/// </summary>
	internal ref struct RecordingScope
	{
		RenderState _previous;
		HashSet<RenderTarget> _grabbed;
		Rendering.DrawRecording _recording;
		bool _active;

		internal static RecordingScope Create( Rendering.RecordingRenderBackend backend, Rendering.RecordingFrameView view, Rendering.DrawRecording into, SceneLayerType layerType, ImageFormat colorFormat, MultisampleAmount msaa )
		{
			ThreadSafe.AssertIsMainThread();

			if ( IsActive )
				throw new InvalidOperationException( "A recorded drawing cannot overlap another render scope." );

			var scope = new RecordingScope { _previous = _state, _grabbed = grabbedTextures, _recording = recording, _active = true };
			grabbedTextures = into.Grabbed;
			recording = into;
			_state = new RenderState
			{
				active = true,
				view = view,
				layerType = layerType,
				attributes = into.BlockAttributes,
				backend = backend,
				colorFormat = colorFormat,
				msaaLevel = msaa,
				cameraTransform = new Transform( view.Frustum.GetCameraPosition(), view.Frustum.GetCameraAngles() ),
			};

			return scope;
		}

		public void Dispose()
		{
			if ( !_active ) return;
			_state = _previous;
			grabbedTextures = _grabbed;
			recording = _recording;
		}
	}

	[ThreadStatic]
	static Rendering.DrawRecording recording;

	/// <summary>The recording a game's drawing on this thread goes into (<see cref="RecordingScope"/>), or null.</summary>
	internal static Rendering.DrawRecording Recording => recording;

	/// <summary>Uses private attributes for a drawing pass, preserving its caller's attributes.</summary>
	internal readonly ref struct AttributeScope
	{
		readonly RenderAttributes previous;
		internal AttributeScope( RenderAttributes attributes )
		{
			previous = _state.attributes;
			_state.attributes = attributes;
		}
		public void Dispose() => _state.attributes = previous;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal static void AssertRenderBlock()
	{
		if ( !IsActive )
			throw new System.Exception( "Tried to render outside of rendering block" );
	}

	/// <summary>
	/// Setup the lighting attributes for this current object. Place them in the targetAttributes
	/// </summary>
	public static void SetupLighting( SceneObject obj, RenderAttributes targetAttributes = null )
	{
		targetAttributes ??= Attributes;

		Assert.NotNull( targetAttributes );
		Assert.IsValid( obj );

		// A standalone block has no view to light from
		if ( _state.view is { } view )
			view.SetupLighting( obj, targetAttributes );
	}

	/// <summary>
	/// Grabs the current viewport's color texture and stores it in targetName on renderAttributes.
	/// </summary>
	public static RenderTarget GrabFrameTexture( string targetName = "FrameTexture", RenderAttributes renderAttributes = null, DownsampleMethod downsampleMethod = DownsampleMethod.None, int maxMips = 0 )
		=> GrabFrameTexture( targetName, renderAttributes, downsampleMethod, maxMips, engineOwned: false );

	/// <summary>
	/// As above, with <paramref name="engineOwned"/> taking the copy from the engine's own pool, for grabs of the menu UI.
	/// </summary>
	internal static RenderTarget GrabFrameTexture( string targetName, RenderAttributes renderAttributes, DownsampleMethod downsampleMethod, int maxMips, bool engineOwned )
	{
		renderAttributes ??= Attributes;

		AssertRenderBlock();

		bool withMips = downsampleMethod != DownsampleMethod.None;
		var numMips = 1;
		if ( withMips )
		{
			var fullMips = (int)Math.Log2( Math.Max( Viewport.Width, Viewport.Height ) );
			numMips = maxMips > 0 ? Math.Min( maxMips, fullMips ) : fullMips;
		}

		// Grab a new one - which may very well be the one we just returned
		var frameTexture = engineOwned
			? RenderTarget.GetEngineTemporary( 1, ImageFormat.Default, ImageFormat.None, MultisampleAmount.MultisampleNone, numMips, targetName )
			: RenderTarget.GetTemporary( 1, ImageFormat.Default, ImageFormat.None, msaa: MultisampleAmount.MultisampleNone, numMips: numMips, targetName );

		Backend.ResolveColor( frameTexture.ColorTarget, Viewport );

		// Generate a mip chain if we want one
		if ( withMips ) GenerateMipMaps( frameTexture.ColorTarget, downsampleMethod );

		grabbedTextures ??= new();
		grabbedTextures.Add( frameTexture );

		renderAttributes.Set( targetName, frameTexture.ColorTarget );

		return frameTexture;
	}

	[Obsolete( "Use GrabFrameTexture with DownsampleMethod instead" )]
	public static void GrabFrameTexture( string targetName, RenderAttributes renderAttributes, bool withMips )
	{
		GrabFrameTexture( targetName, renderAttributes, withMips ? DownsampleMethod.GaussianBlur : DownsampleMethod.None );
	}

	/// <summary>
	/// Grabs the current depth texture and stores it in targetName on renderAttributes.
	/// </summary>
	public static RenderTarget GrabDepthTexture( string targetName = "DepthTexture", RenderAttributes renderAttributes = null )
	{
		renderAttributes ??= Attributes;

		AssertRenderBlock();

		// Grab a new one - which may very well be the one we just returned
		var grabbedTexture = RenderTarget.GetTemporary( 1, ImageFormat.None, ImageFormat.Default, msaa: MultisampleAmount.MultisampleScreen, targetName: targetName );

		Backend.ResolveDepth( grabbedTexture.DepthTarget, Viewport );

		grabbedTextures ??= new();
		grabbedTextures.Add( grabbedTexture );

		renderAttributes.Set( targetName, grabbedTexture.DepthTarget );

		return grabbedTexture;
	}





	/// <summary>
	/// Get or set the current render target. Setting this will bind the render target and change the viewport to match it.
	/// </summary>
	public static RenderTarget RenderTarget
	{
		get => _state.renderTarget;
		set
		{
			if ( _state.renderTarget == value )
				return;

			var view = _state.view;
			// A standalone block takes a depth target with its colour (a Borealis frame's, which post processing reads); it
			// draws to the colour
			if ( view is null && value?.ColorTarget is null )
				throw new InvalidOperationException( "Standalone rendering requires a color target." );

			// Going from default render target to custom render target
			if ( _state.renderTarget == null )
			{
				var viewport = Backend.GetViewport();
				// Save off min/max Z values here, so we can restore them later.
				_state.defaultMinZ = viewport.MinZ;
				_state.defaultMaxZ = viewport.MaxZ;
			}

			_state.renderTarget = value;

			// Resetting to default render target
			if ( _state.renderTarget == null )
			{
				view?.RestoreTargets( _state.defaultMinZ, _state.defaultMaxZ );
				return;
			}

			if ( view is not null )
			{
				view.BindTarget( _state.renderTarget );
			}
			else
			{
				_state.colorFormat = _state.renderTarget.ColorTarget.ImageFormat;
				Backend.BindRenderTarget( _state.renderTarget );
			}
			Viewport = new Rect( 0, 0, _state.renderTarget.Width, _state.renderTarget.Height );
		}
	}

	/// <summary>
	/// Clear the current drawing context to given color.
	/// </summary>
	/// <param name="color">Color to clear to.</param>
	/// <param name="clearColor">Whether to clear the color buffer at all.</param>
	/// <param name="clearDepth">Whether to clear the depth buffer.</param>
	/// <param name="clearStencil">Whether to clear the stencil buffer.</param>
	public static void Clear( Color color, bool clearColor = true, bool clearDepth = true, bool clearStencil = true )
	{
		Backend.Clear( color, clearColor, clearDepth, clearStencil );
	}

	/// <summary>
	/// Clear the current drawing context to given color.
	/// </summary>
	/// <param name="clearColor">Whether to clear the color buffer to transparent color.</param>
	/// <param name="clearDepth">Whether to clear the depth buffer.</param>
	public static void Clear( bool clearColor = true, bool clearDepth = true )
	{
		Clear( Color.Transparent, clearColor, clearDepth, false );
	}

	/// <summary>
	/// Legacy camera render entry point. This overload is no longer supported and always returns false.
	/// </summary>
	[Obsolete( "Use CameraComponent.RenderToTexture" )]
	public static bool RenderToTexture( SceneCamera camera, Texture target )
	{
		return false;
	}

	/// <summary>
	/// Copies pixel data from one texture to another on the GPU.
	/// This does not automatically resize or scale the texture, format and size should be equal.
	/// </summary>
	public static void CopyTexture( Texture srcTexture, Texture dstTexture )
	{
		if ( srcTexture == null ) throw new ArgumentNullException( nameof( srcTexture ) );
		if ( dstTexture == null ) throw new ArgumentNullException( nameof( dstTexture ) );
		if ( srcTexture.ImageFormat != dstTexture.ImageFormat ) throw new ArgumentException( "Source and destination texture format must match!" );
		if ( srcTexture.Size != dstTexture.Size ) throw new ArgumentException( "Source and destination texture size must match!" );

		Backend.CopyTexture( srcTexture, dstTexture, 0, 0, 0, 0 );
	}
	/// <summary>Compatibility overload that copies one subresource. The legacy mip-count arguments are ignored.</summary>
	[Obsolete( "Use the CopyTexture overload without 'srcMipLevels' and 'dstMipLevels' parameters instead." )]
	public static void CopyTexture( Texture srcTexture, Texture dstTexture, int srcMipSlice = 0, int srcArraySlice = 0, int srcMipLevels = 1, int dstMipSlice = 0, int dstArraySlice = 0, int dstMipLevels = 1 )
	{
		CopyTexture( srcTexture, dstTexture, srcMipSlice, srcArraySlice, dstMipSlice, dstArraySlice );
	}

	/// <summary>
	/// Copies pixel data from one texture to another on the GPU.
	/// This does not automatically resize or scale the texture, format and size should be equal.
	/// This one lets you copy to/from arrays / specific mips.
	/// </summary>
	public static void CopyTexture( Texture srcTexture, Texture dstTexture, int srcMipSlice = 0, int srcArraySlice = 0, int dstMipSlice = 0, int dstArraySlice = 0 )
	{
		if ( srcTexture == null ) throw new ArgumentNullException( nameof( srcTexture ) );
		if ( dstTexture == null ) throw new ArgumentNullException( nameof( dstTexture ) );

		if ( srcTexture.ImageFormat != dstTexture.ImageFormat ) throw new ArgumentException( "Source and destination texture format must match!" );

		if ( srcMipSlice < 0 || srcMipSlice >= srcTexture.Mips ) throw new ArgumentException( $"{nameof( srcMipSlice )} out of bounds" );
		if ( dstMipSlice < 0 || dstMipSlice >= dstTexture.Mips ) throw new ArgumentException( $"{nameof( dstMipSlice )} out of bounds" );

		var srcDesc = srcTexture.Desc;
		var dstDesc = dstTexture.Desc;

		if ( srcArraySlice < 0 || srcArraySlice >= srcDesc.ArrayCount ) throw new ArgumentException( $"{nameof( srcArraySlice )} out of bounds" );
		if ( dstArraySlice < 0 || dstArraySlice >= dstDesc.ArrayCount ) throw new ArgumentException( $"{nameof( dstArraySlice )} out of bounds" );

		var srcMipWidth = Math.Max( 1, srcTexture.Width >> srcMipSlice );
		var srcMipHeight = Math.Max( 1, srcTexture.Height >> srcMipSlice );
		var dstMipWidth = Math.Max( 1, dstTexture.Width >> dstMipSlice );
		var dstMipHeight = Math.Max( 1, dstTexture.Height >> dstMipSlice );

		if ( srcMipWidth != dstMipWidth || srcMipHeight != dstMipHeight )
			throw new ArgumentException( "Source and destination texture mip level sizes must match!" );

		Backend.CopyTexture( srcTexture, dstTexture, srcMipSlice, srcArraySlice, dstMipSlice, dstArraySlice );
	}

	/// <summary>
	/// Forces the GPU to flush all pending commands and wait for completion.
	/// Useful when you need to ensure GPU work is finished before proceeding.
	/// Can be called outside of a render block.
	/// </summary>
	public static void FlushGPU()
	{
		if ( !Graphics.IsAvailable )
			return;

		(RenderBackend.Default as BorealisGpuRenderBackend)?.WaitIdle();
	}
}
