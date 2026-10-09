using NativeEngine;
using System.Linq;
using Sandbox.Rendering;
using System.Runtime.CompilerServices;

namespace Sandbox.SceneRenderer.Bridge;

/// <summary>
/// <c>r_managed_scene</c> backend: one world mirror per scene and render system per camera.
/// Registered on assembly load; <see cref="CameraStages"/> runs camera effects and UI at native hook points.
/// </summary>
internal sealed class SceneBridge : IManagedSceneRenderer
{
#pragma warning disable CA2255 // Register on demand when the engine loads this assembly.
	[ModuleInitializer]
	internal static void Register() => ManagedSceneRendering.Renderer = new SceneBridge();
#pragma warning restore CA2255

	/// <summary>
	/// Strongly owned world/camera state requires explicit disposal of native resources.
	/// Released on deletion or shutdown; unreported invalid entries are swept next frame.
	/// </summary>
	readonly Dictionary<SceneWorld, SceneMirror> mirrors = new();
	readonly Dictionary<CameraComponent, CameraState> cameras = new();

	/// <summary>
	/// Persistent per-camera renderer, shadow cache and pooled state.
	/// </summary>
	sealed class CameraState : IDisposable
	{
		public readonly RenderSystem System = new();
		public readonly RenderView View = new();
		public readonly CameraStages Stages = new();
		public readonly SceneObjectDrawer Drawer;
		public ViewTarget TextureTarget;

		public CameraState()
		{
			// Route stage and custom-object Graphics.Render calls.
			Drawer = new SceneObjectDrawer( System );
			Stages.RenderSceneObject = Drawer.Render;
		}

		/// <summary>
		/// How long the last frame's mirror sync took (<see cref="RenderStats.SyncMs"/>).
		/// </summary>
		public double SyncMs;
		public Texture TextureTargetColor;
		public Texture TextureTargetDepth;

		/// <summary>
		/// Release owned resources; colour targets remain camera-owned.
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
		}
	}

	/// <summary>
	/// Free a destroyed camera's renderer, targets and view.
	/// </summary>
	public void Forget( CameraComponent camera )
	{
		if ( camera is null || !cameras.Remove( camera, out var state ) ) return;
		state.Dispose();
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
	readonly List<CameraComponent> invalidCameras = new();
	readonly List<SceneWorld> invalidWorlds = new();

	/// <summary>
	/// Release invalid cameras/worlds whose deletion was not reported.
	/// </summary>
	void FreeInvalid()
	{
		foreach ( var (camera, _) in cameras )
		{
			if ( !camera.IsValid ) invalidCameras.Add( camera );
		}

		foreach ( var (world, _) in mirrors )
		{
			if ( !world.IsValid() ) invalidWorlds.Add( world );
		}

		foreach ( var camera in invalidCameras ) Forget( camera );
		foreach ( var world in invalidWorlds ) Forget( world );
		invalidCameras.Clear();
		invalidWorlds.Clear();
	}

	/// <summary>
	/// Release all resources before native shutdown.
	/// </summary>
	public void Shutdown()
	{
		foreach ( var (_, state) in cameras )
			state.Dispose();
		cameras.Clear();

		foreach ( var (_, mirror) in mirrors )
			mirror.Dispose();
		mirrors.Clear();
	}

	SceneMirror MirrorFor( SceneWorld world )
	{
		if ( !mirrors.TryGetValue( world, out var mirror ) ) mirrors[world] = mirror = new SceneMirror( world );
		return mirror;
	}

	CameraState StateFor( CameraComponent camera )
	{
		if ( !cameras.TryGetValue( camera, out var state ) ) cameras[camera] = state = new CameraState();
		return state;
	}

	/// <summary>
	/// Sync the world and prepare camera, target and stages, optionally advancing exposure.
	/// </summary>
	(SceneMirror Mirror, CameraState State) Prepare( CameraComponent camera, Vector2 size, bool updateExposure = true )
	{
		FreeInvalid();

		var sceneCamera = camera.SceneCamera;
		var syncStart = System.Diagnostics.Stopwatch.GetTimestamp();
		var mirror = MirrorFor( sceneCamera.World );
		mirror.Sync( sceneCamera );
		SyncSkybox( mirror, sceneCamera );

		var state = StateFor( camera );
		state.Drawer.Mirror = mirror;
		state.SyncMs = System.Diagnostics.Stopwatch.GetElapsedTime( syncStart ).TotalMilliseconds;
		Sandbox.Rendering.ManagedSceneRendering.Report( new Sandbox.Rendering.ManagedFrameCounters { SyncMs = state.SyncMs } );
		FillView( state.View, sceneCamera, size );
		state.View.ToneMapScalar = state.Stages.BeginFrame( sceneCamera, state.View, updateExposure );
		return (mirror, state);
	}

	/// <summary>
	/// Mirror the world's <c>MapSkybox3D</c>, origin and scale.
	/// </summary>
	void SyncSkybox( SceneMirror mirror, SceneCamera camera )
	{
		SceneSkybox3D source = null;
		foreach ( var candidate in camera.World.InternalSkyboxWorlds )
		{
			if ( candidate is not null && candidate.IsValid )
			{
				source = candidate;
				break;
			}
		}

		if ( source is null )
		{
			mirror.World.Skybox3D = null;
			return;
		}

		var skyMirror = MirrorFor( source.SkyboxWorld );
		skyMirror.Sync( camera );

		var skybox = mirror.World.Skybox3D;
		if ( skybox?.World != skyMirror.World ) skybox = mirror.World.Skybox3D = new Skybox3D( skyMirror.World );
		skybox.Origin = source.Origin;
		skybox.Scale = source.Scale;
	}

	/// <summary>
	/// Copy the native camera, including applied modifiers/shake and horizontal FOV.
	/// </summary>
	internal static void FillView( RenderView view, SceneCamera camera, Vector2 size )
	{
		view.Name = camera.Name;
		view.Position = camera.Position;
		view.Rotation = camera.Rotation;
		view.FieldOfView = camera.FieldOfView;
		view.ZNear = camera.ZNear;
		view.ZFar = camera.ZFar;
		view.ClearColor = camera.BackgroundColor;
		view.CameraAttributes = camera.Attributes;

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

		view.RenderTags = Tokens( camera.RenderTags, view.RenderTags );
		view.ExcludeTags = Tokens( camera.ExcludeTags, view.ExcludeTags );
	}

	/// <summary>
	/// Convert camera tags, reusing the current array when unchanged.
	/// </summary>
	static uint[] Tokens( ITagSet tags, uint[] current )
	{
		var tokens = tags.GetTokens();
		if ( tokens is HashSet<uint> set && set.Count == current.Length )
		{
			var same = true;
			foreach ( var token in current ) same &= set.Contains( token );
			if ( same ) return current;
		}

		return tokens.Count == 0 ? [] : [.. tokens];
	}

	public bool Render( CameraComponent camera, SwapChainHandle_t swapChain, Vector2 size )
	{
		if ( size.x < 1 || size.y < 1 ) return false;

		var (mirror, state) = Prepare( camera, size );
		state.System.Render( mirror.World, state.View, swapChain, size, state.Stages );
		return true;
	}

	public bool RenderToTexture( CameraComponent camera, Texture target )
	{
		if ( target.Width < 1 || target.Height < 1 ) return false;

		var (mirror, state) = Prepare( camera, target.Size );

		// Non-float outputs need HDR blending before the copy.
		if ( !ViewTarget.IsFloatFormat( target.ImageFormat ) )
		{
			state.System.Render( mirror.World, state.View, target, MultisampleAmount.MultisampleNone, state.Stages );
			return true;
		}

		// Float outputs render directly with owned depth.
		if ( state.TextureTargetColor != target )
		{
			state.TextureTargetDepth?.Dispose();
			var depth = Texture.CreateRenderTarget().WithSize( target.Width, target.Height ).WithFormat( ImageFormat.D24S8 ).Create( "r_managed_scene depth" );
			state.TextureTarget = new ViewTarget( target, depth );
			state.TextureTargetColor = target;
			state.TextureTargetDepth = depth;
		}

		state.System.Render( mirror.World, state.View, state.TextureTarget, state.Stages );
		return true;
	}

	/// <summary>
	/// Last managed-frame statistics, or null before first render.
	/// </summary>
	internal RenderStats? StatsFor( CameraComponent camera )
	{
		if ( !cameras.TryGetValue( camera, out var state ) ) return null;

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
			camera.SceneCamera.RenderToBitmap( bitmap );
		}

		return bitmap.GetPixels32();
	}

	/// <summary>
	/// The camera rendered by this renderer into an 8-bit texture at the swap chain's MSAA, as native renders a bitmap
	/// (<c>CCameraRenderer::RenderToLayer</c>) - the compare's managed frame.
	/// </summary>
	internal Color32[] RenderManaged( CameraComponent camera, Vector2Int size, bool updateExposure ) => RenderManaged( camera, size, updateExposure, out _, out _, out _ );

	Color32[] RenderManaged( CameraComponent camera, Vector2Int size, bool updateExposure, out SceneMirror mirror, out CameraState state, out MultisampleAmount msaa )
	{
		using var output = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( ImageFormat.RGBA8888 ).Create( "r_managed_scene_compare" );
		(mirror, state) = Prepare( camera, size, updateExposure );
		msaa = ((RenderMultisampleType)CSceneSystem.GetMainSwapChainMultisampleType()).FromEngine();
		state.System.Render( mirror.World, state.View, output, msaa, state.Stages );
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
		// The mirror and camera state are the bridge's, kept per world and camera, not made for this call
#pragma warning disable CA2000 // Dispose objects before losing scope
		var managed = RenderManaged( camera, size, updateExposure: false, out var mirror, out var state, out var msaa );
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
