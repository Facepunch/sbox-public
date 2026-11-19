using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Borealis behind the engine's scene renderer seam (<see cref="ISceneRenderer"/>) in a process drawing on Borealis.Gpu
/// (<c>-borealisgpu</c>): one world mirror per scene world and a render system per camera view. Registered at startup
/// (<see cref="EngineHooks"/>). A camera's frame on the device is in <c>SceneBridge.Gpu.cs</c>.
/// </summary>
internal sealed partial class SceneBridge : ISceneRenderer
{
	/// <summary>
	/// Strongly owned world/view state requires explicit disposal of native resources.
	/// Released on deletion or shutdown; unreported invalid entries are swept next frame.
	/// </summary>
	readonly Dictionary<SceneWorld, SceneMirror> mirrors = new();
	readonly Dictionary<ViewKey, CameraState> views = new();

	/// <summary>
	/// A camera's view: the camera, and the <see cref="ViewSetup.ViewHash"/> it was rendered with, as native keys a view's history
	/// (<c>ViewUniqueId</c>). A camera's main view and a capture from it keep their own history.
	/// </summary>
	readonly record struct ViewKey( int Camera, int View );

	/// <summary>
	/// Persistent per-view renderer, shadow cache and pooled state.
	/// </summary>
	sealed class CameraState : IDisposable
	{
		public readonly RenderSystem System = new();
		public readonly RenderView View = new();
		public readonly SceneObjectDrawer Drawer;
		public readonly WeakReference<SceneCamera> Camera;

		public CameraState( SceneCamera camera )
		{
			Camera = new WeakReference<SceneCamera>( camera );

			// Routes stage and custom-object Graphics.Render calls.
			Drawer = new SceneObjectDrawer( System );
		}

		/// <summary>
		/// How long the last frame's mirror sync took (<see cref="RenderStats.SyncMs"/>).
		/// </summary>
		public double SyncMs;


		/// <summary>
		/// The frustum the view's planar reflection planes are tested against.
		/// </summary>
		public PlanarFrustum PlanarFrustum;

		/// <summary>
		/// Whether this view's frame is being rendered now: a child view asked for while it draws can't be this view again.
		/// </summary>
		public bool Rendering;

		/// <summary>The engine frame this view last rendered in (<see cref="ViewIdleFrames"/>).</summary>
		public ulong LastFrame;

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

		/// <summary>
		/// What a child view draws into for an 8-bit texture (<see cref="RenderChildOnGpu"/>); a float one draws into
		/// <see cref="GpuTarget"/>.
		/// </summary>
		public GpuTexture GpuOutput;

		/// <summary>
		/// A top-level view's own attributes (its planar reflection), and what Borealis reads them as.
		/// </summary>
		public RenderAttributes GpuViewAttributes;
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
	/// Stop mirroring a deleted world, as a camera's main world or as an extra world, and the extra worlds drawn in it.
	/// </summary>
	public void Forget( SceneWorld world )
	{
		if ( world is null ) return;

		mirrors.Remove( world, out var mirror );
		foreach ( var (key, _) in extras )
		{
			if ( key.Source == world || (mirror is not null && key.Host == mirror) ) invalidExtras.Add( key );
		}

		FreeExtras();
		mirror?.Dispose();
	}

	/// <summary>
	/// An extra world (<see cref="SceneCamera.Worlds"/>, <see cref="SceneCamera.DebugWorlds"/>) mirrored into a main world's
	/// mirror, as a sub-world of it.
	/// </summary>
	readonly record struct ExtraKey( SceneWorld Source, SceneMirror Host );

	sealed class ExtraMirror
	{
		public SceneMirror Mirror;

		/// <summary>
		/// The last frame a view drew it.
		/// </summary>
		public ulong LastFrame;
	}

	readonly Dictionary<ExtraKey, ExtraMirror> extras = new();

	/// <summary>
	/// Frames an extra world stays mirrored without a view drawing it. Its objects stay in the main world meanwhile, where
	/// views that don't draw it skip them.
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
			key.Host.HostedSubWorlds &= ~SubWorldBit( extra.Mirror.SubWorld );
			extra.Mirror.Dispose();
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

	/// <summary>
	/// Frames a view keeps its renderer and targets without rendering: an editor widget that isn't shown (the game view
	/// behind the scene view) or a camera that stopped drawing gives them back, and starts again from nothing (no history,
	/// shadows drawn again) if it renders later.
	/// </summary>
	internal const ulong ViewIdleFrames = 600;

	/// <summary>
	/// Release views whose camera went away or that haven't rendered for <see cref="ViewIdleFrames"/>, and worlds deleted,
	/// without being reported.
	/// </summary>
	void FreeInvalid()
	{
		var frame = Application.FrameCount;
		foreach ( var (key, state) in views )
		{
			if ( state.IsOrphaned || (!state.Rendering && frame - state.LastFrame > ViewIdleFrames) ) invalidViews.Add( key );
		}

		FreeIdleCaptures( frame );
		MirrorContent.FreeCollected();

		foreach ( var (world, _) in mirrors )
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

		foreach ( var (_, extra) in extras )
			extra.Mirror.Dispose();
		extras.Clear();

		ShutdownCaptures();
		bitmapTarget?.Dispose();
		bitmapTarget = null;

		foreach ( var (_, mirror) in mirrors )
			mirror.Dispose();
		mirrors.Clear();
	}

	/// <summary>
	/// Make the shaders Borealis's render systems share, at startup: layers otherwise make them as they first render, and the
	/// first frames draw differently.
	/// </summary>
	public void WarmUp() => RenderSystem.WarmUp();

	/// <summary>Which envmap probes capture, and when: those the camera sees, within a budget.</summary>
	internal EnvmapScheduler Envmaps => envmaps ??= new EnvmapScheduler( this );

	EnvmapScheduler envmaps;

	/// <inheritdoc/>
	public void RenderEnvmaps( Scene scene ) => Envmaps.Run( scene );

	/// <summary>
	/// Whether envmap captures wait while Borealis.Gpu makes a loaded scene's models (on). For comparisons.
	/// </summary>
	internal static bool HoldCapturesWhileLoading { get; set; } = true;

	/// <summary>The longest captures wait for models, so a scene that keeps loading them still gets its probes.</summary>
	const long MaxCaptureHoldMs = 8000;

	long captureHeldSince;

	/// <summary>Pending mirror changes that mean a scene has just loaded rather than changing as it plays.</summary>
	const int LoadedSceneChanges = 256;

	/// <summary>
	/// While a world's mirror has a loaded scene's objects still to apply, or models load
	/// (<see cref="Borealis.Backend.BackendDevice.LoadingModels"/>), up to <see cref="MaxCaptureHoldMs"/>: a probe captured
	/// then misses them, and each of its faces rescans what it draws as they arrive, which made the frames after a map load
	/// several times native's.
	/// </summary>
	public bool HoldsCaptures
	{
		get
		{
			var loading = MirrorContent.Gpu is { LoadingModels: true };
			foreach ( var (_, mirror) in mirrors )
			{
				if ( loading ) break;
				loading = mirror.PendingChanges >= LoadedSceneChanges;
			}

			if ( !HoldCapturesWhileLoading || !loading )
			{
				captureHeldSince = 0;
				return false;
			}

			var now = Environment.TickCount64;
			if ( captureHeldSince == 0 ) captureHeldSince = now;
			return now - captureHeldSince < MaxCaptureHoldMs;
		}
	}

	SceneMirror MirrorFor( SceneWorld world )
	{
		if ( !mirrors.TryGetValue( world, out var mirror ) ) mirrors[world] = mirror = new SceneMirror( world );
		return mirror;
	}

	CameraState StateFor( SceneCamera camera, int viewHash )
	{
		var key = new ViewKey( camera._cameraId, viewHash );
		if ( !views.TryGetValue( key, out var state ) ) views[key] = state = new CameraState( camera );
		return state;
	}

	/// <summary>
	/// What a view needs that Borealis doesn't do yet, or null when it can render it. Each is a gap listed in
	/// docs/managed/render-paths.md.
	/// </summary>
	internal static string NotYetInBorealis( SceneCamera camera, in ViewSetup setup )
	{
		if ( !camera.World.IsValid() ) return "a camera without a world";

		return null;
	}

	/// <summary>
	/// Sync the world and prepare the camera's view, in <paramref name="given"/> (a cube capture's face) rather than the
	/// camera view's own state when one is given.
	/// </summary>
	(SceneMirror Mirror, CameraState State) Prepare( SceneCamera camera, in ViewSetup setup, Vector2 size, CameraState given = null )
	{
		// A child view prepares while its parent's frame records, on workers too, from the mirrors the parent synced: it
		// changes no world anyone's drawing (an unseen world's mirror is new, and synced once)
		var parent = rendering;
		if ( parent is null )
		{
			// A top-level view, on the main thread (a scene panel before the window acquires, a cube capture first in the frame):
			// the engine frame's device frame starts now if it hasn't, not in the middle of this view's recording
			Gpu?.FollowFrame();
			FreeInvalid();
		}

		// Borealis traces screen-space reflections as the graphics settings leave them (r_ssr_downsample_ratio)
		Borealis.ScreenSpaceReflectionsLayer.Enabled = ScreenSpaceReflectionsPass.IsEnabled;

		var syncStart = System.Diagnostics.Stopwatch.GetTimestamp();
		var mirror = MirrorFor( camera.World );
		if ( parent is null || !mirror.HasSynced ) mirror.Sync( camera );
		SyncSkybox( mirror, camera, setup );

		var state = given ?? StateFor( camera, setup.ViewHash );
		state.LastFrame = Application.FrameCount;
		state.Drawer.Mirror = mirror;
		state.View.SubWorlds = SyncExtraWorlds( mirror, camera, setup, state.Drawer.ExtraMirrors );
		state.SyncMs = System.Diagnostics.Stopwatch.GetElapsedTime( syncStart ).TotalMilliseconds;
		Sandbox.Rendering.ManagedSceneRendering.Report( new Sandbox.Rendering.ManagedFrameCounters { SyncMs = state.SyncMs } );
		FillView( state.View, camera, setup, size, mirror );

		// The camera's attributes under the view's, as native's camera renderer sets them on its view: what the camera
		// component and the scene's render attributes set (Rust Legacy lights its models by its time of day there)
		state.View.CameraAttributes = MirrorContent.CameraParameters( camera.Attributes, ref state.GpuCameraParameters, ref state.GpuCameraVersion );

		// A child view takes its parent's render time unless it has its own, as native's child views do
		// (CSceneSystem::AddDynamicView)
		if ( parent is not null ) state.View.Time ??= parent.View.Time;

		// A top-level view's planar reflection is its camera frame's (RenderPlanarReflectionOnGpu)
		else state.View.ViewAttributes = null;
		return (mirror, state);
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
	/// The view whose frame is being rendered on this bridge, if one is: a view asked for meanwhile is its child.
	/// </summary>
	CameraState rendering;

	/// <summary>
	/// How many child views deep the frame being drawn on this thread is.
	/// </summary>
	[ThreadStatic] internal static int ChildDepth;

	/// <summary>
	/// How deep child views go: native lets a child view render another, but a view asking for itself again, under another
	/// view hash each time, would never end.
	/// </summary>
	internal const int MaxChildDepth = 4;

	/// <summary>
	/// Marks a view's frame as being rendered, for the views asked for while it draws, until disposed.
	/// </summary>
	readonly ref struct Drawing : IDisposable
	{
		readonly SceneBridge bridge;
		readonly CameraState state, parent;

		public Drawing( SceneBridge bridge, CameraState state )
		{
			this.bridge = bridge;
			this.state = state;
			parent = bridge.rendering;
			bridge.rendering = state;
			state.Rendering = true;
		}

		public void Dispose()
		{
			state.Rendering = false;
			bridge.rendering = parent;
		}
	}

	/// <summary>
	/// Sync the camera's extra worlds into its main world's mirror, and return the sub-worlds its view draws. The worlds are
	/// <c>CameraRenderer.Configure</c>'s: <see cref="SceneCamera.Worlds"/>, then <see cref="SceneCamera.DebugWorlds"/> (gizmos,
	/// the scene's debug world) unless the view turns debug overlays off, each once and never the main world. Native draws them
	/// in the main world's view, in the same layers and depth, lit by what lights it.
	/// </summary>
	ulong SyncExtraWorlds( SceneMirror host, SceneCamera camera, in ViewSetup setup, List<SceneMirror> drawn )
	{
		drawn.Clear();
		var main = camera.World;
		ulong shown = 0;

		foreach ( var world in camera.Worlds )
		{
			if ( world is null || world == main ) continue;
			shown |= SyncExtraWorld( host, world, camera, drawn );
		}

		if ( setup.EnableDebugOverlays == false ) return shown;

		foreach ( var world in camera.DebugWorlds )
		{
			if ( world is null || world == main || camera.Worlds.Contains( world ) ) continue;
			shown |= SyncExtraWorld( host, world, camera, drawn );
		}

		return shown;
	}

	ulong SyncExtraWorld( SceneMirror host, SceneWorld world, SceneCamera camera, List<SceneMirror> drawn )
	{
		if ( !world.IsValid() ) return 0;

		var key = new ExtraKey( world, host );
		if ( !extras.TryGetValue( key, out var extra ) )
		{
			// A new extra world adds to the world its parent's frame is drawing: a child view goes without it
			if ( rendering is not null ) return 0;

			var free = ~host.HostedSubWorlds;
			if ( free == 0 )
			{
				if ( !warnedSubWorlds )
				{
					warnedSubWorlds = true;
					Log.Warning( $"Borealis: a world is drawn with more than {RenderWorld.MaxSubWorlds} extra worlds; the rest aren't drawn" );
				}

				return 0;
			}

			var subWorld = System.Numerics.BitOperations.TrailingZeroCount( free ) + 1;
			host.HostedSubWorlds |= SubWorldBit( subWorld );
			extras[key] = extra = new ExtraMirror { Mirror = new SceneMirror( world, host.World, subWorld ) };
		}

		extra.LastFrame = Application.FrameCount;

		// A child view's extra worlds were synced by its parent, or another view this frame
		if ( rendering is null || !extra.Mirror.HasSynced ) extra.Mirror.Sync( camera );
		drawn.Add( extra.Mirror );
		return SubWorldBit( extra.Mirror.SubWorld );
	}

	bool warnedSubWorlds;

	/// <summary>
	/// Mirror the world's <c>MapSkybox3D</c>, origin and scale.
	/// </summary>
	void SyncSkybox( SceneMirror mirror, SceneCamera camera, in ViewSetup setup )
	{
		var source = camera.World.GetRenderingSkybox();

		if ( source is null )
		{
			mirror.World.Skybox3D = null;
			return;
		}

		var skyMirror = MirrorFor( source.SkyboxWorld );
		if ( rendering is null || !skyMirror.HasSynced ) skyMirror.Sync( camera, skyboxView: true );

		var skybox = mirror.World.Skybox3D;
		if ( skybox?.World != skyMirror.World ) skybox = mirror.World.Skybox3D = new Borealis.Skybox3D( skyMirror.World );
		skybox.Origin = source.Origin;
		skybox.Scale = source.Scale;
		skybox.Fog = setup.Fog.HasValue ? setup.Fog.ToBorealis( mirror.TextureResolver ) : camera.EnablePostProcessing ? source.ResolveFog( camera ).ToBorealis( mirror.TextureResolver ) : null;
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
	internal static void FillView( RenderView view, SceneCamera camera, in ViewSetup setup, Vector2 size, SceneMirror mirror = null )
	{
		view.Name = camera.Name;
		view.Position = setup.Transform?.Position ?? camera.Position;
		view.Rotation = setup.Transform?.Rotation ?? camera.Rotation;
		view.FieldOfView = setup.FieldOfView ?? camera.FieldOfView;
		view.ZNear = setup.ZNear ?? camera.ZNear;
		view.ZFar = setup.ZFar ?? camera.ZFar;
		view.ClearColor = setup.ClearColor ?? camera.BackgroundColor;
		view.Fog = camera.ResolveFog( setup ).ToBorealis( mirror?.TextureResolver );

		view.ToolsView = camera.ToolsView;
		view.ToolsVisMode = ToolsVisModeOf( camera );
		view.Wireframe = camera.WireframeMode;
		// A reflection draws a depth-only prepass, and no shadows unless r.reflections.shadows is on (CameraRenderer.Configure)
		view.Reflection = setup.Reflection;
		view.ReflectionShadows = setup.Reflection && CommandList.ReflectionShadows;

		var rect = camera.Rect;
		view.Viewport = new Rect( rect.Left * size.x, rect.Top * size.y, rect.Width * size.x, rect.Height * size.y );

		// The scales on the texture sizes draws ask streaming for, which the scenesystem reads from its convars each view
		view.TextureLodScale = RenderTools.GetTextureLodScale( false );
		view.FallbackTextureLodScale = RenderTools.GetTextureLodScale( true );

		view.Orthographic = camera.Ortho;
		var aspect = view.Viewport.Height > 0 ? view.Viewport.Width / view.Viewport.Height : 1;
		view.OrthoSize = new Vector2( camera.OrthoHeight * aspect, camera.OrthoHeight );

		view.RenderTags = Tokens( setup.RenderTags ?? camera.RenderTags, view.RenderTags );
		view.ExcludeTags = Tokens( camera.ExcludeTags, setup.ExcludeTags, view.ExcludeTags );

		// The projection: the view's or the camera's own, which replaces the clip-space bounds too
		view.Projection = setup.ProjectionMatrix ?? camera.CustomProjectionMatrix;
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
		// A game window on Borealis.Gpu has no Source 2 swap chain: the camera draws into the window's frame
		if ( swapChain.self == default && BorealisGpuWindow.Drawing is { } window )
		{
			RenderInto( window, camera, size, setup );
			return;
		}

		// Anything else presents on Source 2's device
		SceneRenderer.Native.Render( camera, swapChain, size, setup );
	}

	/// <summary>
	/// The process's Borealis.Gpu device, or null in a process drawing on Source 2's, where nothing renders through Borealis.
	/// </summary>
	static BorealisGpuRenderBackend Gpu => RenderBackend.Default as BorealisGpuRenderBackend;

	/// <summary>
	/// A texture render: between blocks on Borealis.Gpu's device (<see cref="RenderIntoTexture"/>), inside one of our frames as
	/// its child (<see cref="RenderChildOnGpu"/>), or inside another block as a child copied into the texture there
	/// (<see cref="RenderInBlock"/>).
	/// </summary>
	public void RenderToTexture( SceneCamera camera, Texture target, Vector2? size, in ViewSetup setup )
	{
		if ( target is null || target.native.IsNull || Gpu is not { } gpu ) return;

		if ( !Graphics.IsActive )
		{
			RenderIntoTexture( gpu, camera, target, size ?? target.Size, setup );
			return;
		}

		// Inside one of our frames (a command list's view), as its child on the device
		if ( rendering is not null )
		{
			camera.ConfigureView( setup );
			RenderChild( camera, setup, target, null, out _ );
			return;
		}

		RenderInBlock( gpu, camera, setup, target, null, out _ );
	}

	public bool RenderToTexture( SceneCamera camera, Texture target, Texture depthTarget, ISceneView parent, in ViewSetup setup, string name, out ViewData viewData )
	{
		viewData = default;
		if ( Gpu is not { } gpu ) return false;

		// Between blocks, a texture with no depth of the caller's renders on the device
		if ( !Graphics.IsActive && parent == default && depthTarget is null && target is { native.IsNull: false } )
		{
			RenderIntoTexture( gpu, camera, target, target.Size, setup );
			return true;
		}

		// Inside one of our frames (a command list's view, water's refraction), as its child on the device
		if ( Graphics.IsActive && rendering is not null )
		{
			camera.ConfigureView( setup );
			return RenderChild( camera, setup, target, depthTarget, out viewData );
		}

		if ( target is null || target.native.IsNull ) return false;

		// Inside another block, as a child copied into the caller's textures there
		if ( Graphics.IsActive ) return RenderInBlock( gpu, camera, setup, target, depthTarget, out viewData );

		// With a depth target of the caller's outside a frame, the same in a block of its own
		using ( Graphics.Scope.CreateOn( gpu ) )
			return RenderInBlock( gpu, camera, setup, target, depthTarget, out viewData );
	}

	/// <summary>
	/// Render a view asked for while one of ours draws (a command list's <c>DrawView</c>, water's refraction) as its child: in a
	/// frame of its own, prepared, recorded and submitted now, on this thread, in the middle of the parent's recording. The
	/// parent submits after it, so what samples it (a later command, the object drawn next) reads it finished, this frame.
	/// The parent's workers carry on recording meanwhile. Shadow maps the parent drew aren't submitted yet, so the child
	/// draws its own (<c>ShadowMapper</c>'s maps in flight).
	/// </summary>
	bool RenderChild( SceneCamera camera, in ViewSetup setup, Texture target, Texture depthTarget, out ViewData viewData )
	{
		viewData = default;

		// A view can't draw itself, and views asking for views asking for views stop somewhere
		if ( ChildDepth >= MaxChildDepth ) return false;
		if ( views.TryGetValue( new ViewKey( camera._cameraId, setup.ViewHash ), out var drawing ) && drawing.Rendering ) return false;

		ChildDepth++;
		CameraState state;
		try
		{
			if ( !RenderChildOnGpu( camera, setup, target, depthTarget, out state ) ) return false;
		}
		finally
		{
			ChildDepth--;
		}

		viewData = ViewDataFor( state, target );
		return true;
	}

	/// <summary>
	/// What native's texture render hands a view's samplers (<c>CCameraRenderer::RenderToTexture</c>): the inverse reverse-Z
	/// projection, in the engine's column-vector layout, the main world's viewport and depth range, and how the colour was flipped.
	/// </summary>
	static ViewData ViewDataFor( CameraState state, Texture target )
	{
		var view = state.View;
		var size = new Vector2( target.Width, target.Height );
		var viewport = view.Viewport;
		return new ViewData
		{
			IsValid = true,
			ProjectionToView = view.ViewToProjection.Inverted.Transpose(),
			Viewport = new Vector4( viewport.Left / size.x, viewport.Top / size.y, viewport.Width / size.x, viewport.Height / size.y ),
			DepthRange = new Vector2( state.System.DepthMin, state.System.DepthMax ),
			DepthUvScaleBias = new Vector4( view.FlipX ? -1 : 1, view.FlipY ? -1 : 1, view.FlipX ? 1 : 0, view.FlipY ? 1 : 0 ),
		};
	}

	/// <summary>
	/// What the mirror of a camera view's world holds in its caches, or null before its first render.
	/// </summary>
	internal MirrorCacheCounts? CachesFor( SceneCamera camera, in ViewSetup setup )
	{
		if ( !views.TryGetValue( new ViewKey( camera._cameraId, setup.ViewHash ), out var state ) ) return null;
		return state.Drawer.Mirror?.Caches;
	}
}
