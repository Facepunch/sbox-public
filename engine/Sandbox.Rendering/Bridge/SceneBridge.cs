using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Borealis behind the engine's scene renderer seam (<see cref="ISceneRenderer"/>) in a process drawing on Borealis.Gpu
/// (<c>-borealisgpu</c>): one world renderer per scene world and a render system per camera view. Registered at startup
/// (<see cref="EngineHooks"/>). A camera's frame on the device is in <c>SceneBridge.Gpu.cs</c>.
/// </summary>
internal sealed partial class SceneBridge : ISceneRenderer
{
	/// <summary>
	/// Strongly owned world/view state requires explicit disposal of native resources.
	/// Released on deletion or shutdown; unreported invalid entries are swept next frame.
	/// </summary>
	readonly Dictionary<SceneWorld, WorldRender> renders = new();
	readonly Dictionary<ViewKey, ViewStates> views = new();

	/// <summary>
	/// A camera's view: the camera, and the <see cref="ViewSetup.ViewHash"/> it was rendered with, as native keys a view's history
	/// (<c>ViewUniqueId</c>). A camera's main view and captures from it keep separate history.
	/// </summary>
	readonly record struct ViewKey( int Camera, int View, bool DirectCapture = false );

	sealed class ViewStates : IDisposable
	{
		public CameraState Main;
		public List<CameraState> Captures;

		public bool IsEmpty => Main is null && Captures is not { Count: > 0 };

		public void Dispose()
		{
			Main?.Dispose();
			if ( Captures is null ) return;
			foreach ( var state in Captures ) state.Dispose();
		}
	}

	/// <summary>
	/// Persistent per-view renderer, shadow cache and pooled state.
	/// </summary>
	sealed class CameraState : IDisposable
	{
		public readonly RenderSystem System = new();
		public readonly RenderView View = new();
		public readonly SceneObjectDrawer Drawer;
		public readonly WeakReference<SceneCamera> Camera;

		public CameraState( SceneCamera camera, bool directCapture = false )
		{
			Camera = new WeakReference<SceneCamera>( camera );
			DirectCapture = directCapture;

			// Routes stage and custom-object Graphics.Render calls.
			Drawer = new SceneObjectDrawer( System );
		}

		/// <summary>
		/// How long preparing the last frame's world took (<see cref="RenderStats.SyncMs"/>).
		/// </summary>
		public double SyncMs;


		/// <summary>
		/// The frustum the view's planar reflection planes are tested against.
		/// </summary>
		public PlanarFrustum PlanarFrustum;

		/// <summary>
		/// Whether this view is in its drawing scope, and which view's drawing asked for it, if any.
		/// </summary>
		public bool Rendering;
		public CameraState Parent;
		public bool DirectCapture { get; }
		public int ViewHash;

		// A published capture can be sampled after recording. Its destinations don't stay alive for the renderer's cache.
		WeakReference<object> destination;
		WeakReference<Texture> captureDepth;

		public object Destination => destination is not null && destination.TryGetTarget( out var owner ) ? owner : null;
		public Texture CaptureDepth => captureDepth is not null && captureDepth.TryGetTarget( out var depth ) ? depth : null;

		public void SetDestinations( object owner, Texture depth )
		{
			if ( destination is null ) destination = new( owner );
			else destination.SetTarget( owner );
			if ( captureDepth is not null ) captureDepth.SetTarget( depth );
			else if ( depth is not null ) captureDepth = new( depth );
		}

		public bool Available( ulong frame ) => LastFrame != frame && !Rendering && !System.IsUnfinished;
		public bool CanFree( ulong frame ) => !Rendering && !System.IsUnfinished
			&& (IsOrphaned || CaptureIdlePolicy.Expired( frame, LastFrame, LastTicks ));

		/// <summary>The engine frame this view last rendered in, and when.</summary>
		public ulong LastFrame;
		public long LastTicks;

		/// <summary>
		/// Whether the camera this is for was disposed or collected without saying so.
		/// </summary>
		public bool IsOrphaned => !Camera.TryGetTarget( out var camera ) || camera.IsDisposed;

		/// <summary>
		/// What the camera's frame renders into, and that frame handed to the engine's drawing on the device for its stages
		/// (<see cref="RenderInto"/>).
		/// </summary>
		public ViewTarget GpuTarget;
		public BorealisGpuFrame GpuFrame;
		public GpuCameraStages GpuStages;

		/// <summary>The camera's upscaler on Borealis.Gpu, made the first time its frame can upscale.</summary>
		public BorealisGpuUpscaler GpuUpscaler;
		public Borealis.Backend.AutoExposure Exposure;

		/// <summary>
		/// What a child view resolves into for an 8-bit texture; a float one draws straight into
		/// <see cref="GpuTarget"/>.
		/// </summary>
		public GpuTexture GpuOutput;

		/// <summary>
		/// A top-level view's own attributes (its planar reflection), and what Borealis reads them as.
		/// </summary>
		public ShaderParameters GpuViewParameters;

		/// <summary>The camera's attributes as Borealis reads them, and the version of them last written there.</summary>
		public ShaderParameters GpuCameraParameters;
		public int GpuCameraVersion;

		/// <summary>
		/// Release owned resources.
		/// </summary>
		public void Dispose()
		{
			System.Dispose();
			GpuFrame?.Dispose();
			GpuFrame = null;
			GpuStages?.Dispose();
			GpuStages = null;
			Exposure?.Dispose();
			GpuUpscaler?.Dispose();
			GpuUpscaler = null;
			GpuTarget?.Dispose();
			GpuTarget = null;
			GpuOutput?.Dispose();
			GpuOutput = null;
		}
	}

	/// <summary>
	/// Free a disposed camera's renderers, targets and views.
	/// </summary>
	public void Forget( SceneCamera camera )
	{
		if ( camera is null ) return;

		foreach ( var (key, _) in views )
		{
			if ( key.Camera == camera._cameraId ) invalidViews.Add( key );
		}

		FreeViews();
	}

	/// <summary>
	/// Stop drawing a deleted world, as a camera's main world or as an extra world, and stop drawing it in other worlds or
	/// them in it.
	/// </summary>
	public void Forget( SceneWorld world )
	{
		if ( world is null ) return;

		renders.Remove( world, out var render );
		foreach ( var (key, _) in extras )
		{
			if ( key.Source == world || (render is not null && key.Host == render) ) invalidExtras.Add( key );
		}

		FreeExtras();
		render?.Dispose();
	}

	/// <summary>
	/// An extra world (<see cref="SceneCamera.Worlds"/>, <see cref="SceneCamera.DebugWorlds"/>) drawn in a main world's views:
	/// its own render world, included in the main world's (<see cref="RenderWorld.Include"/>) under a sub-world number.
	/// </summary>
	readonly record struct ExtraKey( SceneWorld Source, WorldRender Host );

	sealed class ExtraWorld
	{
		public WorldRender Render;

		/// <summary>The sub-world it has in the host's world.</summary>
		public int SubWorld;

		/// <summary>
		/// The last frame a view drew it.
		/// </summary>
		public ulong LastFrame;
	}

	readonly Dictionary<ExtraKey, ExtraWorld> extras = new();

	/// <summary>
	/// Frames an extra world stays in a main world without a view drawing it there. Its objects stay in the main world
	/// meanwhile, where views that don't draw it skip them.
	/// </summary>
	const ulong ExtraWorldIdleFrames = 120;


	// Reused sweep buffers avoid per-frame allocations.
	readonly List<ViewKey> invalidViews = new();
	readonly List<SceneWorld> invalidWorlds = new();
	readonly List<ExtraKey> invalidExtras = new();

	void FreeExtras()
	{
		foreach ( var key in invalidExtras )
		{
			if ( !extras.Remove( key, out var extra ) ) continue;
			key.Host.World.Exclude( extra.Render.World );
		}

		invalidExtras.Clear();
	}

	static ulong SubWorldBit( int subWorld ) => 1UL << (subWorld - 1);

	void FreeViews()
	{
		foreach ( var key in invalidViews )
		{
			if ( views.Remove( key, out var state ) ) state.Dispose();
		}

		invalidViews.Clear();
	}

	// The frame FreeInvalid last ran in, and how often BeginFrame runs it while no view is prepared
	ulong freedFrame;
	const ulong FreeIdleEveryFrames = 60;

	/// <summary>
	/// Release orphaned or idle views, and deleted worlds that went away without being reported.
	/// </summary>
	void FreeInvalid()
	{
		var frame = Application.FrameCount;
		freedFrame = frame;
		foreach ( var (key, states) in views )
		{
			if ( states.Main is { } main && main.CanFree( frame ) )
			{
				main.Dispose();
				states.Main = null;
			}

			if ( states.Captures is { } captures )
			{
				for ( int i = captures.Count - 1; i >= 0; i-- )
				{
					if ( !captures[i].CanFree( frame ) ) continue;
					captures[i].Dispose();
					captures.RemoveAt( i );
				}
			}

			if ( states.IsEmpty ) invalidViews.Add( key );
		}

		FreeIdleCaptures( frame );

		foreach ( var (world, _) in renders )
		{
			if ( !world.IsValid() ) invalidWorlds.Add( world );
		}

		FreeViews();
		foreach ( var world in invalidWorlds ) Forget( world );
		invalidWorlds.Clear();

		foreach ( var (key, extra) in extras )
		{
			if ( !key.Source.IsValid() || frame - extra.LastFrame > ExtraWorldIdleFrames ) invalidExtras.Add( key );
		}

		FreeExtras();
	}

	/// <summary>
	/// Release all resources, before native shutdown.
	/// </summary>
	public void Shutdown()
	{
		foreach ( var (_, state) in views )
			state.Dispose();
		views.Clear();

		extras.Clear();

		ShutdownCaptures();
		mainOutput?.Dispose();
		renderOutput?.Dispose();
		mainOutput = renderOutput = null;

		foreach ( var (_, render) in renders )
			render.Dispose();
		renders.Clear();

		// What every view's custom objects and stages draw with on Borealis.Gpu
		BorealisGpuCustomDraw.Shutdown();
		BorealisGpuFrame.ReleaseViewTextures();

		// The shadow caches every view shares, which keep the last views, lights and worlds that drew them
		Borealis.Shadows.ShadowMapper.ResetCache();
	}

	/// <summary>
	/// Make the shaders Borealis's render systems share, at startup: layers otherwise make them as they first render, and the
	/// first frames draw differently.
	/// </summary>
	public void WarmUp()
	{
		// Material creation is main-thread only (native asserts otherwise), and frames draw on the render thread: every material
		// a frame's drawing uses is made here. The native material is immutable; each draw has its own attributes.
		copyMaterial ??= Material.FromShader( "shaders/copytexture.shader" );
		uberMaterial ??= Material.FromShader( "shaders/postprocess/pp_uber.shader" );
		BorealisGpuFrame.WarmUp();
		BorealisGpuUpscaler.WarmUp();
		RenderSystem.WarmUp();
	}

	/// <summary>Which envmap probes capture, and when: those the camera sees, within a budget.</summary>
	internal EnvmapScheduler Envmaps => envmaps ??= new EnvmapScheduler( this );

	EnvmapScheduler envmaps;

	/// <inheritdoc/>
	public void RenderEnvmaps( Scene scene ) => Envmaps.Run( scene );

	/// <summary>The longest captures wait for models, so a scene that keeps loading them still gets its probes.</summary>
	const long MaxCaptureHoldMs = 8000;

	long captureHeldSince;

	/// <summary>Objects and changes waiting for a world's frame that mean a scene has just loaded rather than changing as it plays.</summary>
	const int LoadedSceneChanges = 256;

	/// <summary>
	/// While a world has a loaded scene's objects still to make, or models load
	/// (<see cref="Borealis.Backend.BackendDevice.LoadingModels"/>), up to <see cref="MaxCaptureHoldMs"/>: a probe captured
	/// then misses them, and each of its faces rescans what it draws as they arrive, which made the frames after a map load
	/// several times native's.
	/// </summary>
	public bool HoldsCaptures
	{
		get
		{
			var loading = MirrorContent.Gpu is { LoadingModels: true };
			foreach ( var (_, render) in renders )
			{
				if ( loading ) break;
				loading = render.PendingChanges >= LoadedSceneChanges;
			}

			if ( !loading )
			{
				captureHeldSince = 0;
				return false;
			}

			// The captures waiting keep the faces' render systems made for them (KeepCaptures)
			var now = Environment.TickCount64;
			if ( captureHeldSince == 0 ) captureHeldSince = now;
			KeepCaptures();
			return now - captureHeldSince < MaxCaptureHoldMs;
		}
	}

	/// <summary>
	/// Unseen renders of a game scene nothing has shown yet (<see cref="BeginFrame"/>), at most: one a game only ever draws
	/// into textures isn't rendered unseen for ever.
	/// </summary>
	const int MaxUnseenRenders = 20;

	/// <summary>Frames after the game's load that a scene it hasn't shown yet is still loaded ahead in.</summary>
	const int LoadAheadFramesAfterLoad = 300;

	/// <inheritdoc/>
	public void BeginFrame()
	{
		if ( rendering is null && MirrorContent.HasCollected && Gpu is { } collector )
		{
			collector.FollowFrame();
			MirrorContent.FreeCollected();
		}
		// Views and worlds are let go of as views are prepared; with none prepared (the menu has no scene of its own) a
		// closed game's worlds and views would stay until the next scene renders
		if ( rendering is null && Gpu is { } device && Application.FrameCount - freedFrame > FreeIdleEveryFrames )
		{
			device.FollowFrame();
			FreeInvalid();
			// The shadow caches evict as views render, which none does now
			Borealis.Shadows.ShadowMapper.Update();
			Borealis.Shadows.ShadowMapper.ForgetStaleCascades();

			mainOutput?.ClearRouting();
			renderOutput?.ClearRouting();
		}

		// Until the game first draws its scene (behind the loading screen, and in the frames the game starts in after its load)
		// nothing renders the scene's world, so nothing would ask for its models until the first frame that does: its
		// objects are applied each frame instead, as they arrive, so their models are read and made, within the device's
		// loading budget, before the first frame shows them
		if ( Application.IsEditor || Gpu is not { } gpu || MirrorContent.Gpu is not { AsyncModels: true } services ) return;

		// The game's scene, whichever context is current: the frame's output runs outside the game's
		if ( Sandbox.Engine.GlobalContext.Game?.ActiveScene is not { IsValid: true } scene || scene.SceneWorld is not { } world || !world.IsValid() ) return;
		var render = RenderFor( world );
		if ( render.Shown ) return;

		// While the game loads, and a few seconds after: a scene a game never shows in the window (one it only draws into
		// textures) isn't prepared with loading budgets for ever
		var frame = Application.FrameCount;
		if ( GameLoading( scene ) ) render.LoadedFrame = 0;
		else if ( render.LoadedFrame == 0 ) render.LoadedFrame = frame;
		else if ( frame - render.LoadedFrame > LoadAheadFramesAfterLoad ) return;

		gpu.FollowFrame();
		services.Loading = true;
		render.PrepareFrame();

		// Once its models are made, its camera's view renders unseen every few frames: the first frame after the load finds
		// the view's resident lists, child views (water's refraction), pipelines and targets made, and its textures streamed.
		// Its shaders compile on workers meanwhile (nothing waits for them unseen); it goes on past its cap until they're done,
		// so one more render makes the pipelines they draw with
		if ( gpu.Services.LoadingModels || Application.FrameCount < warmFrame ) return;
		if ( render.UnseenRenders >= MaxUnseenRenders )
		{
			if ( GameLoading( scene ) && gpu.Services.CompilingShaders ) render.UnseenOwed = true;
			else if ( render.UnseenOwed ) render.UnseenOwed = false;
			else return;
		}

		if ( !WarmTarget( out var size, out var samples ) || ShownCamera( scene ) is not { } camera ) return;

		warmFrame = Application.FrameCount + WarmEveryFrames;
		render.UnseenRenders++;
		using var zone = Borealis.Zones.LoadWarm.Start();
		using var context = new Sandbox.Engine.GlobalContext.GlobalContextScope( Sandbox.Engine.GlobalContext.Game );
		warmSize = size;
		warmSamples = samples;
		warming = true;
		try
		{
			using var unseen = gpu.Services.Passes.Unseen();
			camera.AddToRenderList( default, size );
		}
		finally
		{
			warming = false;
			BorealisGpuRenderBackend.MarkUnseenRendered( scene );
		}
	}

	/// <summary>
	/// The camera a scene would show in the window: its main camera when that's active, else its active camera drawing into
	/// the window that's marked main or has the highest priority (the main camera isn't worked out while it loads).
	/// </summary>
	static CameraComponent ShownCamera( Scene scene )
	{
		if ( scene.Camera is { IsValid: true, Active: true, RenderTarget: null, IsSceneEditorCamera: false } main ) return main;
		CameraComponent best = null;
		foreach ( var camera in scene.GetAllComponents<CameraComponent>() )
		{
			if ( !camera.Active || camera.RenderTarget is not null || camera.IsSceneEditorCamera ) continue;
			if ( best is null || (camera.IsMainCamera && !best.IsMainCamera) || (camera.IsMainCamera == best.IsMainCamera && camera.Priority > best.Priority) ) best = camera;
		}

		return best;
	}

	/// <summary>
	/// The size and samples an unseen render draws at: the game window's, as its first frame will draw; else the last window a
	/// camera drew into. The menu draws no scene, so before a game's first frame no camera has.
	/// </summary>
	bool WarmTarget( out Vector2 size, out int samples )
	{
		var window = Sandbox.Engine.GameWindow.Current?.Borealis;
		return WarmTarget( window?.Size, window?.Samples ?? 1, warmSize, warmSamples, out size, out samples );
	}

	/// <summary>
	/// <see cref="WarmTarget(out Vector2, out int)"/> from the game window's size and samples (null without one) and the last
	/// window a camera drew into. False with neither.
	/// </summary>
	internal static bool WarmTarget( Vector2? window, int windowSamples, Vector2 last, int lastSamples, out Vector2 size, out int samples )
	{
		if ( window is { x: >= 1, y: >= 1 } shown )
		{
			size = shown;
			samples = Math.Max( windowSamples, 1 );
			return true;
		}

		size = last;
		samples = Math.Max( lastSamples, 1 );
		return size.x >= 1 && size.y >= 1;
	}

	/// <summary>Frames between a loading scene's unseen renders.</summary>
	const int WarmEveryFrames = 4;

	ulong warmFrame;

	// The game window's size and samples, from its last render, for unseen renders; whether one is under way
	Vector2 warmSize;
	int warmSamples = 1;
	bool warming;

	/// <summary>
	/// Whether the game is loading (<see cref="BorealisGpuRenderBackend.GameLoading"/>). Its scene's camera draws over the
	/// loading screen then (<c>Game.Render</c>), which doesn't count as showing the scene.
	/// </summary>
	static bool GameLoading( Scene scene ) => BorealisGpuRenderBackend.GameLoading( scene );

	WorldRender RenderFor( SceneWorld world )
	{
		if ( !renders.TryGetValue( world, out var render ) ) renders[world] = render = new WorldRender( world );
		return render;
	}

	ViewStates StatesFor( SceneCamera camera, int hash, bool directCapture = false )
	{
		var key = new ViewKey( camera._cameraId, hash, directCapture );
		if ( !views.TryGetValue( key, out var states ) ) views[key] = states = new();
		return states;
	}

	CameraState StateFor( SceneCamera camera, int hash ) => StatesFor( camera, hash ).Main ??= new( camera );

	/// <summary>
	/// Reserve one state per destination use in the frame, preferring the same destination for its temporal history.
	/// An aliased capture may only replace a state whose old output is no longer sampled.
	/// </summary>
	CameraState CaptureStateFor( SceneCamera camera, int hash, object destination, Texture depth, bool directCapture )
	{
		var captures = StatesFor( camera, hash, directCapture ).Captures ??= [];
		var frame = Application.FrameCount;
		CameraState reusable = null;
		foreach ( var state in captures )
		{
			if ( !state.Available( frame ) ) continue;
			var old = state.Destination;
			var oldDepth = state.CaptureDepth;
			if ( ReferenceEquals( old, destination ) && ReferenceEquals( oldDepth, depth ) ) return state;
			if ( directCapture && reusable is null && CanReplace( old, destination ) && CanReplace( oldDepth, depth ) )
				reusable = state;
		}

		if ( reusable is null )
		{
			reusable = new CameraState( camera, directCapture );
			captures.Add( reusable );
		}
		reusable.SetDestinations( destination, depth );
		return reusable;

		static bool CanReplace( object old, object next )
			=> old is null || ReferenceEquals( old, next ) || old is Texture texture && (texture.native.IsNull || !texture.DrawnByView);
	}

	/// <summary>
	/// Prepare the world and a view: the camera's default view, or the destination's or cube face's <paramref name="given"/> state.
	/// </summary>
	(WorldRender Render, CameraState State) Prepare( SceneCamera camera, in ViewSetup setup, Vector2 size, CameraState given = null, bool fullTarget = false )
	{
		// A child view prepares while its parent's frame records, on workers too, from the worlds the parent prepared: it
		// changes no world anyone's drawing (an unseen world's is new, and prepared once)
		var parent = rendering;
		var state = given ?? StateFor( camera, setup.ViewHash );
		state.ViewHash = setup.ViewHash;
		state.LastFrame = Application.FrameCount;
		state.LastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
		if ( parent is null )
		{
			// A top-level view, on the main thread (a scene panel before the window acquires, a cube capture first in the frame):
			// the engine frame's device frame starts now if it hasn't, not in the middle of this view's recording
			Gpu?.FollowFrame();
			if ( freedFrame != state.LastFrame ) FreeInvalid();
		}

		// Borealis traces screen-space reflections as the graphics settings leave them (r_ssr_downsample_ratio)
		Borealis.ScreenSpaceReflectionsLayer.Enabled = ScreenSpaceReflectionsPass.IsEnabled;

		var prepareStart = System.Diagnostics.Stopwatch.GetTimestamp();
		var render = RenderFor( camera.World );
		if ( parent is null || !render.HasPrepared ) render.Prepare();

		state.Drawer.Render = render;
		state.View.SubWorlds = PrepareExtraWorlds( render, camera, setup, state.Drawer.ExtraWorlds );
		state.SyncMs = System.Diagnostics.Stopwatch.GetElapsedTime( prepareStart ).TotalMilliseconds;
		Sandbox.Rendering.ManagedSceneRendering.Report( new Sandbox.Rendering.ManagedFrameCounters { SyncMs = state.SyncMs } );
		FillView( state.View, camera, setup, size, render, fullTarget );
		render.FillViewLighting( state.View, camera, setup );
		PrepareSkybox( render, camera, setup, state.View );

		// The camera's attributes under the view's, as native's camera renderer sets them on its view: what the camera
		// component and the scene's render attributes set (Rust Legacy lights its models by its time of day there)
		state.View.CameraAttributes = MirrorContent.CameraParameters( camera.Attributes, ref state.GpuCameraParameters, ref state.GpuCameraVersion );

		// The same attributes as the engine holds them, which custom objects drawn in the view read (BorealisGpuCustomDraw)
		state.View.HostView = camera.Attributes;

		// A child view takes its parent's render time unless it has its own, as native's child views do
		// (CSceneSystem::AddDynamicView)
		if ( parent is not null ) state.View.Time ??= parent.View.Time;
		state.View.ViewAttributes = null;
		return (render, state);
	}

	/// <summary>
	/// A view's frustum, made only when a reflection plane needs testing against it, from a copy of the view: updating the view
	/// itself would lose last frame's projection, which it keeps for reprojection.
	/// </summary>
	internal sealed class PlanarFrustum
	{
		readonly RenderView copy = new();
		RenderView view;

		public void Of( RenderView view ) => this.view = view;

		public bool Sees( in BBox bounds )
		{
			if ( view is not null )
			{
				copy.Position = view.Position;
				copy.Rotation = view.Rotation;
				copy.FieldOfView = view.FieldOfView;
				copy.ZNear = view.ZNear;
				copy.ZFar = view.ZFar;
				copy.Viewport = view.Viewport;
				copy.Projection = view.Projection;
				copy.ClipSpaceBounds = view.ClipSpaceBounds;
				copy.Update();
				view = null;
			}

			return copy.Frustum.Intersects( bounds.Center, bounds.Extents );
		}
	}

	/// <summary>
	/// A plane's reflecting area against a view's frustum.
	/// </summary>
	readonly struct Visibility( PlanarFrustum frustum ) : PlanarReflections.IVisibility
	{
		public bool Sees( in BBox bounds ) => frustum.Sees( bounds );
	}

	/// <summary>
	/// The view whose frame is being rendered on this bridge for this thread's drawing thread, if one is: a view asked for
	/// meanwhile is its child. The main thread and the render thread each have their own (<see cref="DrawerFrames{T}"/>): the
	/// editor's tool views draw on the main thread while the render thread draws the scene view's frame, and a view of the
	/// main thread's isn't a child of the render thread's.
	/// </summary>
	CameraState rendering => drawingFrames.Current;

	readonly DrawerFrames<CameraState> drawingFrames = new();

	/// <summary>
	/// How many child views deep the frame being drawn on this thread is.
	/// </summary>
	[ThreadStatic] internal static int ChildDepth;

	/// <summary>
	/// How deep child views go: native lets a child view render another, but a view asking for itself again, under another
	/// view hash each time, would never end.
	/// </summary>
	internal const int MaxChildDepth = 4;

	public bool TryGetCurrentView( out ActiveSceneView view )
	{
		view = default;
		if ( rendering is not { } state || !state.Camera.TryGetTarget( out var camera ) ) return false;

		var current = state.View;
		// A 3D skybox pass has a transformed eye and its own world. It cannot take a capture from the main camera's world.
		if ( BridgeDrawCallbacks.CurrentView is { } drawn && !ReferenceEquals( drawn, current ) ) return false;
		view = new ActiveSceneView( camera, new Transform( current.Position, current.Rotation ), current.FieldOfView,
			current.ZNear, current.ZFar, current.Viewport.Size, current.Orthographic, current.Projection,
			current.Capture || current.Reflection || state.DirectCapture );
		return true;
	}

	// Recursion is about the logical view, not which of its capture destinations or slots it draws into.
	bool IsDrawing( SceneCamera camera, int viewHash )
	{
		for ( var state = rendering; state is not null; state = state.Parent )
		{
			if ( state.ViewHash == viewHash && state.Camera.TryGetTarget( out var active ) && ReferenceEquals( active, camera ) ) return true;
		}

		return false;
	}

	/// <summary>
	/// Marks a view's frame as being rendered, for the views asked for while it draws, until disposed.
	/// </summary>
	readonly ref struct Drawing : IDisposable
	{
		readonly CameraState state;
		readonly DrawerFrames<CameraState>.Scope scope;

		public Drawing( SceneBridge bridge, CameraState state )
		{
			this.state = state;
			state.Parent = bridge.rendering;
			state.Rendering = true;
			scope = bridge.drawingFrames.Enter( state );
		}

		public void Dispose()
		{
			scope.Dispose();
			state.Rendering = false;
			state.Parent = null;
		}
	}

	/// <summary>
	/// Prepare the camera's extra worlds, included in its main world's, and return the sub-worlds its view draws. The worlds are
	/// <c>CameraRenderer.Configure</c>'s: <see cref="SceneCamera.Worlds"/>, then <see cref="SceneCamera.DebugWorlds"/> (gizmos,
	/// the scene's debug world) unless the view turns debug overlays off, each once and never the main world. Native draws them
	/// in the main world's view, in the same layers and depth, lit by what lights it.
	/// </summary>
	ulong PrepareExtraWorlds( WorldRender host, SceneCamera camera, in ViewSetup setup, List<WorldRender> drawn )
	{
		drawn.Clear();
		var main = camera.World;
		ulong shown = 0;

		foreach ( var world in camera.Worlds )
		{
			if ( world is null || world == main ) continue;
			shown |= PrepareExtraWorld( host, world, camera, drawn );
		}

		if ( setup.EnableDebugOverlays == false ) return shown;

		foreach ( var world in camera.DebugWorlds )
		{
			if ( world is null || world == main || camera.Worlds.Contains( world ) ) continue;
			shown |= PrepareExtraWorld( host, world, camera, drawn );
		}

		return shown;
	}

	ulong PrepareExtraWorld( WorldRender host, SceneWorld world, SceneCamera camera, List<WorldRender> drawn )
	{
		if ( !world.IsValid() ) return 0;

		var key = new ExtraKey( world, host );
		if ( !extras.TryGetValue( key, out var extra ) )
		{
			// A new extra world adds to the world its parent's frame is drawing: a child view goes without it
			if ( rendering is not null ) return 0;

			var render = RenderFor( world );
			var subWorld = host.World.Include( render.World );
			if ( subWorld == 0 )
			{
				if ( !warnedSubWorlds )
				{
					warnedSubWorlds = true;
					Log.Warning( $"Borealis: a world is drawn with more than {RenderWorld.MaxSubWorlds} extra worlds; the rest aren't drawn" );
				}

				return 0;
			}

			extras[key] = extra = new ExtraWorld { Render = render, SubWorld = subWorld };
		}

		extra.LastFrame = Application.FrameCount;

		// A child view's extra worlds were prepared by its parent, or another view this frame
		if ( rendering is null || !extra.Render.HasPrepared ) extra.Render.PrepareFrame();
		drawn.Add( extra.Render );
		return SubWorldBit( extra.SubWorld );
	}

	bool warnedSubWorlds;

	/// <summary>
	/// The world's <c>MapSkybox3D</c>, origin and scale.
	/// </summary>
	void PrepareSkybox( WorldRender render, SceneCamera camera, in ViewSetup setup, RenderView view )
	{
		var source = camera.World.GetRenderingSkybox();
		view.SkyboxFog = camera.ResolveFogForSkybox( setup, source ).ToBorealis( render.TextureResolver );
		// The shared descriptor is geometry only. A child of this world uses what the parent prepared.
		if ( rendering is not null && render.World.InRender ) return;

		if ( source is null )
		{
			render.World.Skybox3D = null;
			return;
		}

		var skyRender = RenderFor( source.SkyboxWorld );
		if ( rendering is null || !skyRender.HasPrepared ) skyRender.Prepare();

		var skybox = render.World.Skybox3D;
		if ( skybox?.World != skyRender.World ) skybox = render.World.Skybox3D = new Borealis.Skybox3D( skyRender.World );
		skybox.Origin = source.Origin;
		skybox.Scale = source.Scale;
		skybox.Rotation = source.Rotation;
	}

	/// <summary>A camera's debug view: <c>mat_toolsvis</c> over its own mode, as <c>CameraRenderer.Configure</c> takes them.</summary>
	internal static int ToolsVisModeOf( SceneCamera camera )
	{
		var toolsVis = DebugOverlay.ToolsVisualization.mat_toolsvis;
		return (int)(toolsVis != SceneCameraDebugMode.Normal ? toolsVis : camera.DebugMode);
	}

	/// <summary>
	/// Copy the native camera, including applied modifiers/shake and horizontal FOV, with a view's overrides on top, as
	/// <c>CameraRenderer.Configure</c> applies them.
	/// </summary>
	internal static void FillView( RenderView view, SceneCamera camera, in ViewSetup setup, Vector2 size, WorldRender render = null, bool fullTarget = false )
	{
		view.Name = camera.Name;
		view.Position = setup.Transform?.Position ?? camera.Position;
		view.Rotation = setup.Transform?.Rotation ?? camera.Rotation;
		view.Viewport = PixelViewport( camera, size, fullTarget );
		var aspect = view.Viewport.Height > 0 ? view.Viewport.Width / view.Viewport.Height : 1;
		view.FieldOfView = setup.FieldOfView ?? camera.FieldOfViewForAspect( aspect );
		view.ZNear = setup.ZNear ?? camera.ZNear;
		view.ZFar = setup.ZFar ?? camera.ZFar;
		view.ClearColor = setup.ClearColor ?? camera.BackgroundColor;
		view.Fog = camera.ResolveFog( setup ).ToBorealis( render?.TextureResolver );

		view.ToolsView = camera.ToolsView;
		view.ToolsVisMode = ToolsVisModeOf( camera );
		view.Wireframe = camera.WireframeMode;
		// A reflection draws a depth-only prepass, and no shadows unless r.reflections.shadows is on (CameraRenderer.Configure)
		view.Reflection = setup.Reflection;
		view.ReflectionShadows = setup.Reflection && CommandList.ReflectionShadows;

		// The scales on the texture sizes draws ask streaming for, which the scenesystem reads from its convars each view
		view.TextureLodScale = RenderTools.GetTextureLodScale( false );
		view.FallbackTextureLodScale = RenderTools.GetTextureLodScale( true );

		view.Orthographic = camera.Ortho;
		view.OrthoSize = new Vector2( camera.OrthoHeight * aspect, camera.OrthoHeight );

		view.RenderTags = Tokens( setup.RenderTags ?? camera.RenderTags, view.RenderTags );
		view.ExcludeTags = Tokens( camera.ExcludeTags, setup.ExcludeTags, view.ExcludeTags );

		// The projection: the view's or the camera's own, which replaces the clip-space bounds too
		view.Projection = setup.ProjectionMatrix ?? camera.CustomProjectionMatrix;
		view.ClipPlane = setup.ProjectionMatrix is null ? null : setup.ClipPlane;
		view.ClipSpaceBounds = setup.ClipSpaceBounds ?? RenderView.FullClipSpace;

		// The view's own ambient light, from the world's and the camera's, as CameraRenderer.Configure works it out: the world's
		// lighting is shared by every view of it
		var ambient = camera.AmbientLightColor;
		if ( camera.World is { } world ) ambient += world.AmbientLightColor;
		if ( setup.AmbientLightTint is Color tint ) ambient *= tint;
		if ( setup.AmbientLightAdd is Color add ) ambient += add;
		ambient.a = ambient.a.Clamp( 0.0f, 1.0f );
		view.AmbientLight = ambient;

		view.GradientFog = setup.GradientFog is { } gradientFog ? gradientFog.ToBorealis() : null;
		view.LightModulators = setup.EnableLightModulators != false;
		view.Time = setup.Time;

		// Only a texture render's final copy is flipped (RenderPixels clears them for bitmaps)
		view.FlipX = setup.FlipX == true;
		view.FlipY = setup.FlipY == true;
	}

	static Rect PixelViewport( SceneCamera camera, Vector2 size, bool fullTarget = false )
	{
		if ( fullTarget ) return new Rect( Vector2.Zero, size );
		var rect = camera.Rect;
		return new Rect( rect.Left * size.x, rect.Top * size.y, rect.Width * size.x, rect.Height * size.y );
	}

	/// <summary>
	/// Convert camera tags, reusing the current array when unchanged.
	/// </summary>
	static uint[] Tokens( ITagSet tags, uint[] current ) => Tokens( tags, null, current );

	/// <summary>
	/// Convert the union of two tag sets, reusing the current array when unchanged.
	/// </summary>
	static uint[] Tokens( ITagSet tags, ITagSet more, uint[] current )
	{
		var tokens = tags.GetTokens();
		var extra = more?.GetTokens() is { Count: > 0 } added ? added : null;
		if ( SameTokens( tokens, extra, current ) ) return current;

		if ( extra is null ) return tokens.Count == 0 ? [] : [.. tokens];
		var union = new HashSet<uint>( tokens );
		union.UnionWith( extra );
		return [.. union];
	}

	/// <summary>
	/// Whether <paramref name="current"/> (tokens without repeats) holds just the tokens of <paramref name="tokens"/> and
	/// <paramref name="extra"/>, looked at without allocating when they're hash sets (tag sets' are); false when it can't tell.
	/// </summary>
	static bool SameTokens( IReadOnlySet<uint> tokens, IReadOnlySet<uint> extra, uint[] current )
	{
		var count = tokens.Count;
		if ( extra is HashSet<uint> more )
		{
			foreach ( var token in more ) count += tokens.Contains( token ) ? 0 : 1;
		}
		else if ( extra is not null ) return false;

		if ( count != current.Length ) return false;
		foreach ( var token in current )
		{
			if ( !tokens.Contains( token ) && extra?.Contains( token ) != true ) return false;
		}

		return true;
	}

	public void Render( SceneCamera camera, SwapChainHandle_t swapChain, Vector2? size, in ViewSetup setup )
	{
		// An unseen render of a loading scene's view (BeginFrame): its frame is made and let go, nothing is drawn with it
		if ( warming && Gpu is { } warmGpu )
		{
			RenderUnseen( warmGpu, camera, size ?? warmSize, warmSamples, setup );
			return;
		}

		// A game window on Borealis.Gpu: the camera draws into the window's frame. Nothing draws into a Source 2 swap chain
		if ( swapChain.self == default && BorealisGpuWindow.Drawing is { } window )
			RenderInto( window, camera, size, setup );
	}

	/// <summary>
	/// The process's Borealis.Gpu device, or null in a process drawing on Source 2's, where nothing renders through Borealis.
	/// </summary>
	static BorealisGpuRenderBackend Gpu => RenderBackend.Default as BorealisGpuRenderBackend;

	/// <summary>
	/// A texture render can draw on the device between blocks, as a child of the scene recording, or in a standalone
	/// block that receives a copy of the capture.
	/// </summary>
	public void RenderToTexture( SceneCamera camera, Texture target, Vector2? size, in ViewSetup setup )
		=> RenderTexture( camera, target, null, size, setup, out _ );

	public bool RenderToTexture( SceneCamera camera, Texture target, Texture depthTarget, in ViewSetup setup, out ViewData viewData )
		=> RenderTexture( camera, target, depthTarget, null, setup, out viewData );

	bool RenderTexture( SceneCamera camera, Texture target, Texture depthTarget, Vector2? size, in ViewSetup setup, out ViewData viewData )
	{
		viewData = default;
		if ( target is null || target.native.IsNull || !camera.World.IsValid() || Gpu is not { } gpu ) return false;
		if ( depthTarget is not null && depthTarget.native.IsNull ) return false;

		if ( !Graphics.IsActive && depthTarget is null )
			return RenderIntoTexture( gpu, camera, target, size ?? target.Size, setup, out viewData );

		if ( !Graphics.IsActive || rendering is not null )
			return RenderAliasedCapture( camera, setup, target, depthTarget, out viewData );

		return RenderInBlock( gpu, camera, setup, target, depthTarget, out viewData );
	}

	/// <summary>
	/// Render a standalone capture or a child of the current scene. Publish the matching GPU targets for subsequent sampling.
	/// A child records while its parent continues recording. The parent's shadows submit first so the child can reuse them;
	/// the parent's sampling lists submit after the child.
	/// </summary>
	bool RenderAliasedCapture( SceneCamera camera, in ViewSetup setup, Texture target, Texture depthTarget, out ViewData viewData )
	{
		viewData = default;

		if ( !RenderCapture( camera, setup, target, depthTarget, out var output ) ) return false;
		if ( !MirrorContent.CaptureMatches( target, output.Color )
			|| (depthTarget is not null && !MirrorContent.CaptureMatches( depthTarget, output.Depth )) ) return false;
		MirrorContent.SetCapture( target, output.Color );
		if ( depthTarget is not null ) MirrorContent.SetCapture( depthTarget, output.Depth );
		viewData = ViewDataFor( output.State, target );
		return true;
	}

	bool RenderCapture( SceneCamera camera, in ViewSetup setup, Texture target, Texture depth, out CaptureOutput output, object destination = null )
	{
		output = default;
		if ( ChildDepth >= MaxChildDepth || IsDrawing( camera, setup.ViewHash ) ) return false;

		var parent = rendering;
		ChildDepth++;
		try
		{
			using ( Borealis.Zones.ChildView.Start() )
				return RenderOnGpu( camera, setup, target, depth, parent?.View.ToneMapScalar ?? 1.0f, out output, parent is not null, destination );
		}
		finally
		{
			ChildDepth--;
		}
	}

	/// <summary>
	/// What native's texture render hands a view's samplers (<c>CCameraRenderer::RenderToTexture</c>): the inverse reverse-Z
	/// projection, in the engine's column-vector layout, the main world's viewport and depth range, and how the colour was flipped.
	/// </summary>
	static ViewData ViewDataFor( CameraState state, Texture target, bool? flipX = null, bool? flipY = null )
	{
		var view = state.View;
		var size = new Vector2( target.Width, target.Height );
		var viewport = view.Viewport;
		bool x = flipX ?? view.FlipX;
		bool y = flipY ?? view.FlipY;
		return new ViewData
		{
			IsValid = true,
			ProjectionToView = view.ViewToProjection.Inverted.Transpose(),
			Viewport = new Vector4( viewport.Left / size.x, viewport.Top / size.y, viewport.Width / size.x, viewport.Height / size.y ),
			DepthRange = new Vector2( state.System.DepthMin, state.System.DepthMax ),
			DepthUvScaleBias = new Vector4( x ? -1 : 1, y ? -1 : 1, x ? 1 : 0, y ? 1 : 0 ),
		};
	}

	/// <summary>
	/// What a camera view's world holds in its caches, or null before its first render.
	/// </summary>
	internal WorldCacheCounts? CachesFor( SceneCamera camera, in ViewSetup setup )
	{
		var key = new ViewKey( camera._cameraId, setup.ViewHash );
		if ( !views.TryGetValue( key, out var states ) && !views.TryGetValue( key with { DirectCapture = true }, out states ) ) return null;
		var state = states.Main ?? (states.Captures is { Count: > 0 } captures ? captures[0] : null);
		return state?.Drawer.Render?.Caches;
	}
}
