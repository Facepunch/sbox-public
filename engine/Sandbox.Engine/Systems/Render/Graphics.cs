using NativeEngine;
using Sandbox.Engine;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Used to render to the screen using your Graphics Card, or whatever you
/// kids are using in your crazy future computers. Whatever it is I'm sure
/// it isn't fungible and everyone has free money and no-one has to ever work.
/// </summary>
public static partial class Graphics
{
	/// <summary>
	/// Whether the initialized renderer supports GPU operations. False before initialization,
	/// after shutdown, and when using the empty renderer.
	/// </summary>
	public static bool IsAvailable { get; private set; }

	/// <summary>
	/// Whether Source 2's render device is a real one. Without it (its empty render system) Source 2 makes no GPU
	/// resources: its textures, buffers and meshes are descriptions, and what draws is <see cref="RenderBackend.Default"/>.
	/// </summary>
	internal static bool HasSource2Device { get; set; }

	/// <summary>Whether the process draws on a Borealis.Gpu device of its own (<c>-borealisgpu</c>).</summary>
	internal static readonly bool BorealisGpuRequested = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu" );

	internal static void Initialize()
	{
		// With -borealisgpu the process renders even on Source 2's empty render device: Borealis.Gpu draws
		var borealisGpu = BorealisGpuRequested;
		HasSource2Device = g_pRenderDevice.GetRenderDeviceAPI() != RenderDeviceAPI_t.RENDER_DEVICE_API_EMPTY;
		IsAvailable = HasSource2Device || borealisGpu;

		// -borealisgpu: the process draws on a Borealis.Gpu device of its own (docs/managed/borealis-gpu-path.md, stage 2)
		if ( IsAvailable && borealisGpu && RenderBackend.Default is Source2RenderBackend )
		{
			var validation = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-validation" );
			// Progress markers for the device-loss report, unless -borealisgpu-nomarkers
			var markers = !Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-nomarkers" );
			// The device's pipeline cache in game/.source2/borealispipelines, apart from native's, unless -borealisgpu-nopipelinecache
			var pipelineCache = Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-nopipelinecache" ) ? null : System.IO.Path.Combine( EngineFileSystem.Root.GetFullPath( "/" ), ".source2", "borealispipelines" );
			_borealisGpu = BorealisGpuRenderBackend.Create( validation, m => Log.Info( $"Borealis.Gpu: {m.Severity} {m.Text}" ), presentation: true, frames: true, markers: markers, pipelineCacheFolder: pipelineCache );

			// Models load in the background, as Source 2 streams them, unless -borealisgpu-syncload
			_borealisGpu.Services.AsyncModels = !Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-syncload" );

			// Material textures stream with them, tails first, unless -borealisgpu-nostream; -borealisgpu-texturebudget <MiB> caps them
			_borealisGpu.Services.StreamTextures = _borealisGpu.Services.AsyncModels && !Sandbox.Utility.CommandLine.HasSwitch( "-borealisgpu-nostream" );
			if ( Sandbox.Utility.CommandLine.GetSwitchInt( "-borealisgpu-texturebudget", 0 ) is > 0 and var budget ) _borealisGpu.Services.TextureBudgetBytes = (long)budget << 20;
			RenderBackend.SetDefault( _borealisGpu );
			Log.Info( $"Rendering on Borealis.Gpu ({_borealisGpu.Device.Adapter.Name}){(validation ? ", validation on" : "")}; memory: {_borealisGpu.Device.DescribeMemory()}" );
		}

		_mipMapGeneratorShader = IsAvailable ? new ComputeShader( "downsample_cs" ) : null;
		_spdShader = IsAvailable ? new ComputeShader( "downsample_spd" ) : null;

		if ( IsAvailable )
			Cmaa2Renderer.Initialize();
	}

	// The process's Borealis.Gpu device, with -borealisgpu
	static BorealisGpuRenderBackend _borealisGpu;

	internal static void Shutdown()
	{
		Cmaa2Renderer.Shutdown();

		if ( _borealisGpu is not null )
		{
			RenderBackend.SetDefault( null );
			_borealisGpu.Dispose();
			_borealisGpu = null;
		}

		IsAvailable = false;
		_mipMapGeneratorShader = null;
		_spdShader = null;
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
		public IRenderContext renderContext;
		public IFrameView view;
		public SceneLayerType layerType;
		public RenderAttributes attributes;
		internal SceneSystemPerFrameStats_t stats;
		public ImageFormat colorFormat;
		public MultisampleAmount msaaLevel;
		public Transform cameraTransform;
		internal RenderTarget renderTarget;
		internal float defaultMinZ;
		internal float defaultMaxZ;
		internal bool computeQueue;
		internal RenderBackend backend;
	}

	[ThreadStatic]
	private static RenderState _state;

	internal static IRenderContext Context => _state.renderContext;

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

	/// <summary>
	/// Whether the block records into an async compute context: compute and copies only.
	/// </summary>
	internal static bool OnComputeQueue => _state.computeQueue;
	internal static ImageFormat IdealColorFormat => _state.colorFormat;
	internal static MultisampleAmount IdealMsaaLevel => _state.msaaLevel;
	internal static SceneSystemPerFrameStats_t Stats => _state.stats;

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
	/// Access to the current frame's attributes.
	/// </summary>
	[Obsolete( "Frame attributes are deprecated, try to use Pipeline Texture Sets." )]
	internal static RenderAttributes FrameAttributes => Attributes;

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
			if ( !Graphics.IsAvailable ) return 0;
			g_pRenderDevice.GetVideoMemoryInfo( out var budget, out _, out _ );
			return budget;
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
			if ( !Graphics.IsAvailable ) return 0;
			g_pRenderDevice.GetVideoMemoryInfo( out _, out _, out var rsUsage );
			return rsUsage;
		}
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

			var cf = ViewFrustum;

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
	}


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
		bool standalone;
		IRenderContext _context;
		RenderAttributes _attributes;
		RenderBackend _backend;

		/// <summary>
		/// Begins rendering with no scene view: a fresh render context and attributes with the frame's bindless
		/// texture set bound, as CSceneSystem::InitializeRenderAttributes does for a view. Disposal submits the context.
		/// </summary>
		internal static Scope Create() => CreateOn( RenderBackend.Default );

		/// <summary>
		/// Begins rendering with no scene view on <paramref name="backend"/>'s device. Source 2's is as
		/// <see cref="Create()"/>; another's block records into its own list, with managed attributes, and disposal submits it.
		/// </summary>
		internal static Scope CreateOn( RenderBackend backend ) => CreateOnView( backend, null );

		/// <summary>
		/// A block on <paramref name="backend"/>'s device (not Source 2's) drawing into a camera's frame: what the code drawing
		/// in it asks a view (post processing on, the frustum, binding a render target and going back to the frame's) is
		/// <paramref name="view"/>'s.
		/// </summary>
		internal static Scope CreateOnView( RenderBackend backend, BorealisGpuView view )
		{
			ThreadSafe.AssertIsMainThread();

			if ( backend is not Source2RenderBackend )
			{
				if ( IsActive )
					throw new InvalidOperationException( "Standalone rendering cannot overlap another render scope." );

				var managed = backend.BeginBlock();
				var block = new Scope { _previous = _state, standalone = true, _attributes = managed, _backend = backend };
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

			if ( view is not null ) throw new ArgumentException( "Source 2's blocks take native views", nameof( view ) );

			if ( !IsAvailable )
				throw new InvalidOperationException( "Graphics is unavailable." );

			if ( IsActive || CSceneSystem.IsRenderingBusy() )
				throw new InvalidOperationException(
					"Standalone rendering cannot overlap another render scope or scene recording." );

			// The global descriptor ring relies on present throttling. Standalone
			// submissions must finish previous GPU work before reusing a slot.
			g_pRenderDevice.ForceFlushGPU( default );

			var context = g_pRenderDevice.CreateRenderContext( 0 );
			var attributes = new RenderAttributes();
			attributes.Get().SetGlobalBindlessDescriptorSet();

			var scope = new Scope { _previous = _state, standalone = true, _context = context, _attributes = attributes };
			_state = new RenderState { active = true, renderContext = context, attributes = attributes, backend = Source2RenderBackend.Instance };
			return scope;
		}

		/// <summary>The standalone render context, or default inside a scene view.</summary>
		internal IRenderContext Context => _context;

		/// <summary>The standalone attributes, or null inside a scene view.</summary>
		internal RenderAttributes Attributes => _attributes;

		public Scope( in ManagedRenderSetup_t setup )
		{
			_previous = _state;

			_state = new RenderState();

			var frustum = setup.sceneView.GetFrustum();

			_state.active = true;
			_state.view = NativeFrameView.Get( setup.sceneView, setup.sceneLayer );
			_state.renderContext = setup.renderContext;
			_state.layerType = setup.sceneLayer.LayerEnum;
			_state.colorFormat = setup.colorImageFormat;
			_state.msaaLevel = setup.msaaLevel.FromEngine();
			_state.cameraTransform = new Transform( frustum.GetCameraPosition(), frustum.GetCameraAngles() );
			_state.stats = setup.stats;
			_state.computeQueue = (setup.sceneLayer.m_nLayerFlags & Rendering.LayerFlags.AsyncComputeContext) != 0;
			_state.backend = Source2RenderBackend.Instance;

			_state.attributes = ObjectPool<RenderAttributes>.Get();
			_state.attributes.Set( setup.renderContext.GetAttributesPtrForModify() );
		}

		public void Dispose()
		{
			if ( standalone && _backend is not null )
			{
				try
				{
					_backend.EndBlock();
				}
				finally
				{
					_state = _previous;
					ReturnGrabbedTextures();
				}
				return;
			}

			if ( standalone )
			{
				try
				{
					g_pRenderDevice.BeginSubmittingDisplayLists();
					_context.Submit();
				}
				finally
				{
					try
					{
						g_pRenderDevice.ReleaseRenderContext( _context );
						_attributes.Clear();
					}
					finally
					{
						_state = _previous;
					}
				}
				return;
			}

			if ( _state.attributes is not null )
			{
				_state.attributes.Set( default( CRenderAttributes ) );
				ObjectPool<RenderAttributes>.Return( _state.attributes );
			}

			if ( _state.view is NativeFrameView native ) native.Return();

			_state = _previous;

			ReturnGrabbedTextures();
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

		if ( _state.view is { } view )
		{
			view.SetupLighting( obj, targetAttributes );
			return;
		}

		// A standalone block has no layer to light from
		NativeEngine.CSceneSystem.SetupPerObjectLighting( targetAttributes.Get(), obj, default );
	}

	/// <summary>
	/// Grabs the current viewport's color texture and stores it in targetName on renderAttributes.
	/// </summary>
	public static RenderTarget GrabFrameTexture( string targetName = "FrameTexture", RenderAttributes renderAttributes = null, DownsampleMethod downsampleMethod = DownsampleMethod.None, int maxMips = 0 )
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
		var frameTexture = RenderTarget.GetTemporary( 1, ImageFormat.Default, ImageFormat.None, msaa: MultisampleAmount.MultisampleNone, numMips: numMips, targetName );

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
			// A Borealis.Gpu block takes a depth target with its colour (a Borealis frame's, which post processing reads); it
			// draws to the colour
			if ( view is null && (value?.ColorTarget is null || (value.DepthTarget is not null && Backend is Source2RenderBackend)) )
				throw new InvalidOperationException( "Standalone rendering requires a color target without a depth target." );

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
				view?.RestoreTargets( Context, _state.defaultMinZ, _state.defaultMaxZ );
				return;
			}

			if ( view is not null )
			{
				view.BindTarget( Context, _state.renderTarget );
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
	/// Render this camera to the specified texture target
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

		g_pRenderDevice.ForceFlushGPU( default );
	}
}
