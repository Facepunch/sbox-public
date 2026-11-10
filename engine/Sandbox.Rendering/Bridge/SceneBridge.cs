using NativeEngine;
using System.Linq;
using System.Runtime.InteropServices;
using Sandbox.Rendering;
using Sandbox.VR;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Basalt behind the engine's scene renderer seam (<see cref="ISceneRenderer"/>, <c>r_managed_scene</c>): one world mirror per
/// scene world and a render system per camera view. Registered at startup (<see cref="EngineHooks"/>);
/// <see cref="CameraStages"/> runs camera effects and UI at native hook points. What Basalt doesn't do yet goes to
/// <see cref="SceneRenderer.Native"/> from here (<see cref="NotYetInBasalt"/>, <see cref="NotYetOnSwapChain"/>).
/// </summary>
internal sealed class SceneBridge : ISceneRenderer, IManagedSceneCompare
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
		public readonly CameraStages Stages = new();
		public readonly SceneObjectDrawer Drawer;
		public readonly WeakReference<SceneCamera> Camera;
		public ViewTarget TextureTarget;

		public CameraState( SceneCamera camera )
		{
			Camera = new WeakReference<SceneCamera>( camera );

			// Route stage and custom-object Graphics.Render calls.
			Drawer = new SceneObjectDrawer( System );
			Stages.RenderSceneObject = Drawer.Render;
		}

		/// <summary>
		/// How long the last frame's mirror sync took (<see cref="RenderStats.SyncMs"/>).
		/// </summary>
		public double SyncMs;
		public Texture TextureTargetColor;
		public Texture TextureTargetCallerDepth;
		public Texture TextureTargetDepth;

		/// <summary>
		/// Whether the camera this is for was disposed or collected without saying so.
		/// </summary>
		public bool IsOrphaned => !Camera.TryGetTarget( out var camera ) || camera.IsDisposed;

		/// <summary>
		/// Release owned resources; colour targets and caller depth remain the caller's.
		/// </summary>
		public void Dispose()
		{
			System.Dispose();
			Stages.Dispose();
			Drawer.Dispose();
			TextureTargetDepth?.Dispose();
			TextureTargetDepth = null;
			TextureTarget = null;
			TextureTargetColor = null;
			TextureTargetCallerDepth = null;
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
	/// Stop mirroring a deleted world.
	/// </summary>
	public void Forget( SceneWorld world )
	{
		if ( world is null || !mirrors.Remove( world, out var mirror ) ) return;
		mirror.Dispose();
	}

	// Reused sweep buffers avoid per-frame allocations.
	readonly List<ViewKey> invalidViews = new();
	readonly List<SceneWorld> invalidWorlds = new();

	void FreeViews()
	{
		foreach ( var key in invalidViews )
		{
			if ( views.Remove( key, out var state ) ) state.Dispose();
		}

		invalidViews.Clear();
	}

	/// <summary>
	/// Release views whose camera went away, and worlds deleted, without being reported.
	/// </summary>
	void FreeInvalid()
	{
		foreach ( var (key, state) in views )
		{
			if ( state.IsOrphaned ) invalidViews.Add( key );
		}

		foreach ( var (world, _) in mirrors )
		{
			if ( !world.IsValid() ) invalidWorlds.Add( world );
		}

		FreeViews();
		foreach ( var world in invalidWorlds ) Forget( world );
		invalidWorlds.Clear();
	}

	/// <summary>
	/// Release all resources: before native shutdown, or when <c>r_managed_scene</c> goes off.
	/// </summary>
	public void Shutdown()
	{
		foreach ( var (_, state) in views )
			state.Dispose();
		views.Clear();

		foreach ( var (_, mirror) in mirrors )
			mirror.Dispose();
		mirrors.Clear();
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
	/// What a view needs that Basalt doesn't do yet, or null when it can render it. Those views render natively, so the switch
	/// still covers every path, and each is a gap listed in docs/managed/render-paths.md.
	/// </summary>
	internal static string NotYetInBasalt( SceneCamera camera, in ViewSetup setup )
	{
		if ( !camera.World.IsValid() ) return "a camera without a world";

		// Not yet in Basalt: the reflection flag (planar reflections)
		if ( setup.Reflection ) return "planar reflections";

		return null;
	}

	/// <summary>
	/// What a swap chain render needs that Basalt doesn't do yet, or null.
	/// </summary>
	static string NotYetOnSwapChain( SceneCamera camera )
	{
		// Not yet in Basalt: VR's eyes (per-eye projection and clip bounds, stereo targets, the compositor submit)
		if ( VRSystem.IsActive && camera.TargetEye != StereoTargetEye.None ) return "VR";

		// Upscalers aren't in Basalt yet either, but an upscaled game window stays here: Basalt renders it at full resolution,
		// so the switch flips the main view too (the owner's call, 2026-11-09)

		return null;
	}

	/// <summary>
	/// Sync the world and prepare camera, target and stages, optionally advancing exposure.
	/// </summary>
	(SceneMirror Mirror, CameraState State) Prepare( SceneCamera camera, in ViewSetup setup, Vector2 size, bool updateExposure = true )
	{
		FreeInvalid();

		var syncStart = System.Diagnostics.Stopwatch.GetTimestamp();
		var mirror = MirrorFor( camera.World );
		mirror.Sync( camera );
		SyncSkybox( mirror, camera, setup );

		var state = StateFor( camera, setup.ViewHash );
		state.Drawer.Mirror = mirror;
		state.SyncMs = System.Diagnostics.Stopwatch.GetElapsedTime( syncStart ).TotalMilliseconds;
		Sandbox.Rendering.ManagedSceneRendering.Report( new Sandbox.Rendering.ManagedFrameCounters { SyncMs = state.SyncMs } );
		FillView( state.View, camera, setup, size );
		state.View.ToneMapScalar = state.Stages.BeginFrame( camera, state.View, setup, updateExposure );
		return (mirror, state);
	}

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
		skyMirror.Sync( camera, skyboxView: true );

		var skybox = mirror.World.Skybox3D;
		if ( skybox?.World != skyMirror.World ) skybox = mirror.World.Skybox3D = new Basalt.Skybox3D( skyMirror.World );
		skybox.Origin = source.Origin;
		skybox.Scale = source.Scale;
		skybox.Fog = setup.Fog.HasValue ? setup.Fog.ToBasalt() : camera.EnablePostProcessing ? source.ResolveFog( camera ).ToBasalt() : null;
	}

	/// <summary>
	/// Copy the native camera, including applied modifiers/shake and horizontal FOV, with a view's overrides on top, as
	/// <c>CameraRenderer.Configure</c> applies them.
	/// </summary>
	internal static void FillView( RenderView view, SceneCamera camera, in ViewSetup setup, Vector2 size )
	{
		view.Name = camera.Name;
		view.Position = setup.Transform?.Position ?? camera.Position;
		view.Rotation = setup.Transform?.Rotation ?? camera.Rotation;
		view.FieldOfView = setup.FieldOfView ?? camera.FieldOfView;
		view.ZNear = setup.ZNear ?? camera.ZNear;
		view.ZFar = setup.ZFar ?? camera.ZFar;
		view.ClearColor = setup.ClearColor ?? camera.BackgroundColor;
		view.CameraAttributes = Source2Parameters.From( camera.Attributes );
		view.Fog = camera.ResolveFog( setup ).ToBasalt();

		// mat_toolsvis overrides camera debug mode (CameraRenderer.Configure).
		view.ToolsView = camera.ToolsView;
		var toolsVis = DebugOverlay.ToolsVisualization.mat_toolsvis;
		view.ToolsVisMode = (int)(toolsVis != SceneCameraDebugMode.Normal ? toolsVis : camera.DebugMode);
		view.DepthNormals = camera.WantsDepthNormals;

		var rect = camera.Rect;
		view.Viewport = new Rect( rect.Left * size.x, rect.Top * size.y, rect.Width * size.x, rect.Height * size.y );

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

		view.GradientFog = setup.GradientFog is { } gradientFog ? gradientFog.ToBasalt() : null;
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
		if ( more is not null && more.GetTokens() is { Count: > 0 } extra )
		{
			var union = new HashSet<uint>( tokens );
			union.UnionWith( extra );
			tokens = union;
		}

		if ( tokens is HashSet<uint> set && set.Count == current.Length )
		{
			var same = true;
			foreach ( var token in current ) same &= set.Contains( token );
			if ( same ) return current;
		}

		return tokens.Count == 0 ? [] : [.. tokens];
	}

	public void Render( SceneCamera camera, SwapChainHandle_t swapChain, Vector2? size, in ViewSetup setup )
	{
		// A camera that only draws UI has no scene: native's UI-only path draws its panels, outside the seam
		if ( camera.UIOnly || NotYetOnSwapChain( camera ) is not null || NotYetInBasalt( camera, setup ) is not null )
		{
			SceneRenderer.Native.Render( camera, swapChain, size, setup );
			return;
		}

		if ( swapChain.self == default ) return;

		var info = g_pRenderDevice.GetSwapChainInfo( swapChain );
		var renderSize = size ?? new Vector2( info.m_DisplayMode.m_nWidth, info.m_DisplayMode.m_nHeight );
		if ( renderSize.x < 1 || renderSize.y < 1 ) return;

		// As native's swap chain render: the camera's size, and the world's map streaming (OnPreRender)
		camera.OnPreRender( renderSize );
		var (mirror, state) = Prepare( camera, setup, renderSize );
		state.System.Render( mirror.World, state.View, new GpuSwapChain( swapChain ), renderSize, state.Stages );
	}

	public void RenderToTexture( SceneCamera camera, Texture target, Vector2? size, in ViewSetup setup )
	{
		if ( target is null || target.native.IsNull ) return;

		// Not yet in Basalt: child views. Inside a frame (a command list's DrawView, a stage) native renders it as a child of
		// the view being rendered, and in a Basalt frame as a view of its own (render-paths.md, row 6)
		if ( Graphics.IsActive || NotYetInBasalt( camera, setup ) is not null )
		{
			SceneRenderer.Native.RenderToTexture( camera, target, size, setup );
			return;
		}

		var renderSize = size ?? target.Size;
		if ( renderSize.x <= 0 || renderSize.y <= 0 ) return;

		// As native's texture render: the world's map streaming, without touching the camera's size
		camera.ConfigureView( setup );
		RenderToTexture( camera, setup, target, null );
	}

	public bool RenderToTexture( SceneCamera camera, Texture target, Texture depthTarget, ISceneView parent, in ViewSetup setup, string name, out ViewData viewData )
	{
		// Not yet in Basalt: child views (see above), and a caller's depth target that Basalt can't draw into with the colour
		// target directly
		if ( parent != default || Graphics.IsActive || NotYetInBasalt( camera, setup ) is not null || !CanDrawInto( target, depthTarget ) )
			return SceneRenderer.Native.RenderToTexture( camera, target, depthTarget, parent, setup, name, out viewData );

		// Only a child view's sampler reads the view's projection back; a top-level view has none to give
		viewData = default;

		camera.ConfigureView( setup );
		return RenderToTexture( camera, setup, target, depthTarget );
	}

	/// <summary>
	/// A caller's depth target is drawn into with the colour directly, so the colour must be a float target with its sample count.
	/// </summary>
	static bool CanDrawInto( Texture target, Texture depthTarget )
	{
		if ( target is null || target.native.IsNull ) return false;
		if ( depthTarget is null ) return true;

		return Source2Formats.From( target.ImageFormat ).IsFloat() && Source2Texture.From( target ).Samples == Source2Texture.From( depthTarget ).Samples;
	}

	bool RenderToTexture( SceneCamera camera, in ViewSetup setup, Texture target, Texture depthTarget )
	{
		if ( target.Width < 1 || target.Height < 1 ) return false;

		var (mirror, state) = Prepare( camera, setup, target.Size );

		// Non-float outputs need HDR blending before the copy.
		if ( depthTarget is null && !Source2Formats.From( target.ImageFormat ).IsFloat() )
		{
			state.System.Render( mirror.World, state.View, Source2Texture.From( target ), 1, state.Stages );
			return true;
		}

		// Float outputs render directly, with the caller's depth or one of our own.
		if ( state.TextureTargetColor != target || state.TextureTargetCallerDepth != depthTarget )
		{
			state.TextureTargetDepth?.Dispose();
			state.TextureTargetDepth = null;

			var depth = depthTarget ?? (state.TextureTargetDepth = Texture.CreateRenderTarget().WithSize( target.Width, target.Height ).WithFormat( ImageFormat.D24S8 ).Create( "r_managed_scene depth" ));
			state.TextureTarget = new ViewTarget( Source2Texture.From( target ), Source2Texture.From( depth ) );
			state.TextureTargetColor = target;
			state.TextureTargetCallerDepth = depthTarget;
		}

		state.System.Render( mirror.World, state.View, state.TextureTarget, state.Stages );
		return true;
	}

	public void RenderToBitmap( SceneCamera camera, Bitmap bitmap, in ViewSetup setup )
	{
		if ( !bitmap.IsValid() ) return;
		if ( bitmap.Width <= 1 || bitmap.Height <= 1 ) return;

		// Native only ever writes 8-bit pixels, whatever the bitmap holds; a float bitmap keeps what it gets now
		if ( bitmap.BytesPerPixel != 4 || NotYetInBasalt( camera, setup ) is not null )
		{
			SceneRenderer.Native.RenderToBitmap( camera, bitmap, setup );
			return;
		}

		camera.OnPreRender( bitmap.Size );
		var pixels = RenderPixels( camera, setup, bitmap.Size, updateExposure: true, out _, out _, out _ );

		unsafe
		{
			MemoryMarshal.AsBytes( pixels.AsSpan() ).CopyTo( new Span<byte>( bitmap.GetPointer(), bitmap.ByteCount ) );
		}
	}

	/// <summary>
	/// Not yet in Basalt: cube captures (six faces into array slices, and the GGX filter hooked to the last face's native view,
	/// <c>CubemapRendering</c>). They render natively.
	/// </summary>
	public void RenderToCube( SceneCamera camera, Texture cube, in ViewSetup setup ) => SceneRenderer.Native.RenderToCube( camera, cube, setup );

	/// <summary>
	/// Last managed-frame statistics for a camera's view, or null before its first render.
	/// </summary>
	internal RenderStats? StatsFor( SceneCamera camera, in ViewSetup setup )
	{
		if ( !views.TryGetValue( new ViewKey( camera._cameraId, setup.ViewHash ), out var state ) ) return null;

		var stats = state.System.Stats;
		stats.SyncMs = state.SyncMs;
		return stats;
	}

	/// <summary>
	/// The last <c>Compare</c>'s result.
	/// </summary>
	internal FrameComparison.Parity? LastParity { get; private set; }

	public string Compare( CameraComponent camera, string folder ) => Compare( camera, folder, null );

	/// <summary>
	/// The camera rendered natively into an 8-bit bitmap, effects and UI included - the compare's reference. Without
	/// <paramref name="prepare"/>, the caller already ran this frame's <c>PreCameraRender</c> and <c>InitializeRendering</c>.
	/// Native by name, whatever <c>r_managed_scene</c> says.
	/// </summary>
	internal static Color32[] RenderNative( CameraComponent camera, Vector2Int size, bool prepare = true )
	{
		using var bitmap = new Bitmap( size.x, size.y );
		using ( camera.Scene.Push() )
		{
			if ( prepare )
			{
				camera.Scene.PreCameraRender();
				camera.InitializeRendering();
			}

			camera.SceneCamera.OnPreRender( bitmap.Size );
			SceneRenderer.Native.RenderToBitmap( camera.SceneCamera, bitmap, default );
		}

		return bitmap.GetPixels32();
	}

	/// <summary>
	/// The camera's main view rendered by this renderer into an 8-bit texture at the swap chain's MSAA, as native renders a
	/// bitmap (<c>CCameraRenderer::RenderToLayer</c>) - the compare's managed frame.
	/// </summary>
	internal Color32[] RenderManaged( CameraComponent camera, Vector2Int size, bool updateExposure )
	{
		camera.SceneCamera.OnPreRender( size );
		return RenderPixels( camera.SceneCamera, camera.SceneCamera.MainView, size, updateExposure, out _, out _, out _ );
	}

	Color32[] RenderPixels( SceneCamera camera, in ViewSetup setup, Vector2Int size, bool updateExposure, out SceneMirror mirror, out CameraState state, out MultisampleAmount msaa )
	{
		using var output = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( ImageFormat.RGBA8888 ).Create( "r_managed_scene bitmap" );
		(mirror, state) = Prepare( camera, setup, size, updateExposure );

		// Native flips a texture render's copy, never a bitmap's (CCameraRenderer::RenderToBitmap)
		state.View.FlipX = state.View.FlipY = false;
		msaa = ((RenderMultisampleType)CSceneSystem.GetMainSwapChainMultisampleType()).FromEngine();
		state.System.Render( mirror.World, state.View, Source2Texture.From( output ), Source2Formats.Samples( msaa.ToEngine() ), state.Stages );
		return output.GetPixels();
	}

	/// <summary>
	/// Compare frames using an optional filename override.
	/// </summary>
	internal string Compare( CameraComponent camera, string folder, string fileName )
	{
		var size = new Vector2Int( 1280, 720 );
		var name = fileName ?? new string( (camera.GameObject?.Name ?? "camera").Where( char.IsLetterOrDigit ).ToArray() ).ToLowerInvariant();
		var managedPath = System.IO.Path.Combine( folder, $"{name}.png" );

		// Native reference includes camera effects and UI; the managed frame reuses native's exposure update
		var native = RenderNative( camera, size );
		camera.SceneCamera.OnPreRender( size );
		// The mirror and camera state are the bridge's, kept per world and camera, not made for this call
#pragma warning disable CA2000 // Dispose objects before losing scope
		var managed = RenderPixels( camera.SceneCamera, camera.SceneCamera.MainView, size, updateExposure: false, out var mirror, out var state, out var msaa );
#pragma warning restore CA2000 // Dispose objects before losing scope

		var parity = FrameComparison.Compare( managed, native );
		LastParity = parity;
		FrameComparison.SavePng( managed, size.x, size.y, managedPath );
		FrameComparison.SavePng( native, size.x, size.y, System.IO.Path.ChangeExtension( managedPath, ".native.png" ) );
		FrameComparison.SavePng( FrameComparison.Difference( managed, native ), size.x, size.y, System.IO.Path.ChangeExtension( managedPath, ".diff.png" ) );

		var unsupported = mirror.Unsupported.Count == 0 ? "none"
			: string.Join( ", ", mirror.Unsupported.OrderByDescending( x => x.Value ).Select( x => $"{x.Value} {x.Key}" ) );
		var stats = state.System.Stats;
		Log.Info( $"r_managed_scene_compare: mirrored {mirror.Describe()}" );

		var skybox = "";
		if ( mirror.World.Skybox3D is { } sky && state.System.SkyboxStats is { } skyStats )
		{
			foreach ( var (_, candidate) in mirrors )
			{
				if ( candidate.World == sky.World ) Log.Info( $"r_managed_scene_compare: 3D skybox (origin {sky.Origin}, scale {sky.Scale}) mirrored {candidate.Describe()}" );
			}

			skybox = $" (3D skybox: {skyStats.ObjectsVisible}/{sky.World.Count} objects in {skyStats.Draws} draws, scale {sky.Scale})";
		}

		return $"{parity} | camera at {state.View.Position} angles {state.View.Rotation.Angles()} fov {state.View.FieldOfView:0.#}, {msaa} | {stats.ObjectsVisible}/{mirror.World.Count} objects drawn{skybox} in {stats.Draws} draws ({stats.DepthDraws} depth, {stats.TranslucentDraws} translucent), {stats.CustomDraws} custom draws, {stats.Lights} lights, exposure {state.View.ToneMapScalar:0.###} | not drawn by the managed renderer: {unsupported} | frames in {folder}";
	}
}
