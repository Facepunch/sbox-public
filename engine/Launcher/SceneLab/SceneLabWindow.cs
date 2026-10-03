using PanelWindow = Editor.PanelWindow;
using NativeEngine;
using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using Sandbox.UI;

namespace Sandbox.SceneLab;

/// <summary>
/// The Scene Lab window. The world is drawn straight into the swap chain in
/// <see cref="OnRenderBackground"/> - by the managed renderer, or for comparison by the native
/// scenesystem from a mirror of the same world - then the panels draw over it: a title bar with the
/// menus, and a transparent viewport that takes the orbit input.
/// </summary>
internal sealed partial class SceneLabWindow : PanelWindow
{
	public enum RendererKind
	{
		Managed,
		Native,
	}

	readonly RenderSystem renderer = new();
	readonly RenderView view = new() { Name = "Scene Lab", ClearColor = new Color( 0.08f, 0.09f, 0.11f ) };
	readonly Dictionary<string, (Model Model, RenderMesh Mesh)> meshes = new();
	readonly List<LightObject> orbitingLights = new();
	readonly FrameTimer timer = new();

	/// <summary>
	/// Native's r_size_cull_threshold default, 0.25%.
	/// </summary>
	const float SizeCullThreshold = 0.0025f;

	RenderWorld world = new();
	NativeScene nativeScene;

	// Made once, like the models - a frame still on the GPU can be using it
	Texture sky;

	// Where skinned objects are posed - never rendered, only animated
	SceneWorld animationWorld;
	Label stats;
	SceneViewport viewport;

	float lightRingRadius;
	float lightHeight;
	Vector3 focus;
	float defaultDistance;

	public float Yaw { get; set; } = 35;
	public float Pitch { get; set; } = 25;
	public float Distance { get; set; }

	/// <summary>
	/// The managed renderer draws into a <see cref="ViewTarget"/> rather than the swap chain, and the
	/// viewport panel shows its resolved colour. Captures read the texture back. <c>-target</c>, with
	/// <c>-msaa N</c> for a multisampled target and <c>-targetformat</c> for another colour format.
	/// </summary>
	public bool RenderToTexture
	{
		get;
		set
		{
			field = value;
			timer.Clear();
		}
	} = Args.Has( "-target" );

	readonly MultisampleAmount targetMsaa = Args.Int( "-msaa", 1 ) switch
	{
		2 => MultisampleAmount.Multisample2x,
		4 => MultisampleAmount.Multisample4x,
		8 => MultisampleAmount.Multisample8x,
		_ => MultisampleAmount.MultisampleNone,
	};

	readonly ImageFormat targetFormat = Enum.TryParse<ImageFormat>( Args.String( "-targetformat" ), true, out var format ) ? format : ImageFormat.RGBA16161616F;

	ViewTarget target;

	/// <summary>
	/// A multisampled capture's native reference, queued to render with the frame's views and read back
	/// once it has.
	/// </summary>
	(NativeScene Scene, Texture Texture, Color32[] Managed, string Path, int ReadFrame)? pendingReference;

	/// <summary>
	/// Shadow maps on, in both renderers.
	/// </summary>
	public bool Shadows
	{
		get => renderer.Shadows;
		set
		{
			renderer.Shadows = value;

			// The native reference is built with them on or off
			nativeScene?.Dispose();
			nativeScene = null;
			timer.Clear();
		}
	}

	/// <summary>
	/// Whether the managed renderer lays depth down before the forward pass.
	/// </summary>
	public bool DepthPrepass
	{
		get => renderer.DepthPrepass;
		set => renderer.DepthPrepass = value;
	}

	/// <summary>
	/// Objects in the world, lights included.
	/// </summary>
	public int ObjectCount => game?.SceneWorld.SceneObjects.Count ?? world.Count;

	/// <summary>
	/// The managed renderer's numbers for the last frame: the game camera's, or the lab's own renderer's.
	/// </summary>
	public SceneRenderer.RenderStats ManagedStats => GameStats() ?? renderer.Stats;

	/// <summary>
	/// The scene on screen.
	/// </summary>
	public SceneLabScene Scene { get; private set; }

	/// <summary>
	/// Which renderer draws the world.
	/// </summary>
	public RendererKind Renderer
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			timer.Clear();
		}
	}

	/// <summary>
	/// Set by a capture or benchmark from the command line - the app closes the window after this frame.
	/// </summary>
	public bool Finished { get; private set; }

	readonly string captureArgument = Args.String( "-capture" );
	readonly int captureFrame = Math.Max( 1, Args.Int( "-capture-frame", 30 ) );
	string compareRequested;
	Benchmark benchmark;
	ParityRun parity;
	int frame;

	/// <summary>
	/// Where this capture goes: <c>-capture</c>'s path, or the parity run's for the preset it's comparing.
	/// </summary>
	string CapturePath => parity?.CapturePath ?? captureArgument;

	// A result shown in place of the stats for a while
	string message;
	RealTimeUntil messageTime;
	RealTimeSince statsUpdated;

	/// <summary>
	/// <c>-swapchain-msaa N</c>: a multisampled window, which the managed renderer draws into through a multisampled HDR target,
	/// and native's bitmap renders - the GameObject presets' compare - take the sample count of. 4x unless it says otherwise;
	/// <c>-swapchain-msaa 1</c> turns it off.
	/// </summary>
	internal override RenderMultisampleType SwapChainMultisample => Args.Int( "-swapchain-msaa", 4 ) switch
	{
		2 => RenderMultisampleType.RENDER_MULTISAMPLE_2X,
		4 => RenderMultisampleType.RENDER_MULTISAMPLE_4X,
		8 => RenderMultisampleType.RENDER_MULTISAMPLE_8X,
		_ => RenderMultisampleType.RENDER_MULTISAMPLE_NONE,
	};

	public SceneLabWindow( SceneLabScene scene ) : base( "Scene Lab", new Vector2( Args.Int( "-width", 1280 ), Args.Int( "-height", 760 ) ), null, true, vsync: !Args.Has( "-novsync" ) )
	{
		ClearsBackground = false;
		AlwaysFullFrameRate = true;

		// The render device only records GPU frame times for the main window's swap chain
		g_pRenderDevice.SetSwapChainIsMainWindow( Window.SwapChain, true );
		renderer.TimingSwapChain = Window.SwapChain;

		renderer.GetFeature<MeshRenderFeature>().UseMeshMaterials = !Args.Has( "-debugshader" );
		renderer.DepthPrepass = !Args.Has( "-noprepass" );
		renderer.Shadows = !Args.Has( "-noshadows" );

		// Record passes on worker threads, or all on the main thread to compare - for the bridge's render systems too
		RenderSystem.ParallelRecordingDefault = !Args.Has( "-serialrecording" );
		renderer.ParallelRecording = RenderSystem.ParallelRecordingDefault;
		if ( Args.Has( "-nosizecull" ) ) view.SizeCullThreshold = 0;
		if ( Args.Has( "-native-renderer" ) ) Renderer = RendererKind.Native;

		Root.AddChild( BuildUI() );
		Load( scene );

		if ( Args.Has( "-benchmark" ) )
		{
			VSync = false;
			benchmark = new Benchmark( Args.String( "-benchmark" ) );
		}

		if ( Args.Has( "-parity" ) )
		{
			VSync = false;
			parity = new ParityRun( Args.String( "-parity" ) );
			parity.Start( this );
		}
	}

	/// <summary>
	/// Load a preset for the parity run to capture, counting frames from zero.
	/// </summary>
	internal void StartCapture( SceneLabScene scene )
	{
		pendingReference?.Texture.Dispose();
		pendingReference?.Scene.Dispose();
		pendingReference = null;
		Load( scene );
		frame = 0;
	}

	/// <summary>
	/// Drop the loaded GameObject scene and draw nothing, while the parity run idles between passes.
	/// </summary>
	internal void Unload()
	{
		UnloadGame();
	}

	/// <summary>
	/// A capture is done: the parity run goes on to its next preset, anything else closes the window.
	/// </summary>
	void CaptureDone()
	{
		if ( parity is not null && parity.Advance( this ) ) return;
		if ( parity is not null ) FinishParity();
		else Finished = true;
	}

	/// <summary>
	/// The parity run has compared everything: its table, and the exit code.
	/// </summary>
	internal void FinishParity()
	{
		if ( parity.Summarise() > 0 ) Environment.ExitCode = 1;
		parity = null;
		Finished = true;
	}

	Panel BuildUI()
	{
		var root = new Panel();
		root.AddClass( "editor-window scenelab" );
		root.StyleSheet.Load( "/styles/editor.scss" );
		root.StyleSheet.Add( StyleSheet.FromString( Styles, "/styles/scenelab.scss" ) );

		var bar = root.Add.Panel( "titlebar window-drag" );
		bar.Add.Label( "Scene Lab", "scenelab-title" );

		var menus = bar.AddChild( new MenuBar() );
		menus.AddClass( "window-nodrag" );

		var file = menus.AddMenu( "File" );
		file.AddOption( "Quit", "logout", RequestClose ).Shortcut = "Alt+F4";

		// A submenu per group, in the order the presets list them
		var scenes = menus.AddMenu( "Scene" );
		foreach ( var group in SceneLabScene.Presets.GroupBy( x => x.Group ) )
		{
			var submenu = scenes.AddMenu( group.Key );
			foreach ( var preset in group )
			{
				// A plain option rather than a toggle, so picking one closes the menu
				var option = submenu.AddOption( preset.Name, () => Load( preset ) );
				submenu.AboutToShow += m => option.Checked = Scene == preset;
			}
		}

		var viewMenu = menus.AddMenu( "View" );

		var rendererMenu = viewMenu.AddMenu( "Renderer", "compare_arrows" );
		foreach ( var kind in Enum.GetValues<RendererKind>() )
		{
			var option = rendererMenu.AddOption( kind.ToString(), () => Renderer = kind );
			rendererMenu.AboutToShow += m => option.Checked = Renderer == kind;
		}

		var mesh = renderer.GetFeature<MeshRenderFeature>();
		var debug = viewMenu.AddOption( "Debug Shader", on => mesh.UseMeshMaterials = !on );
		viewMenu.AboutToShow += m => debug.Checked = !mesh.UseMeshMaterials;
		var prepass = viewMenu.AddOption( "Depth Prepass", on => { renderer.DepthPrepass = on; timer.Clear(); } );
		viewMenu.AboutToShow += m => prepass.Checked = renderer.DepthPrepass;
		var sizeCull = viewMenu.AddOption( "Size Culling", on => view.SizeCullThreshold = on ? SizeCullThreshold : 0 );
		viewMenu.AboutToShow += m => sizeCull.Checked = view.SizeCullThreshold > 0;
		var shadows = viewMenu.AddOption( "Shadows", on => Shadows = on );
		viewMenu.AboutToShow += m => shadows.Checked = Shadows;
		var texture = viewMenu.AddOption( "Render To Texture", on => RenderToTexture = on );
		viewMenu.AboutToShow += m => texture.Checked = RenderToTexture;
		var vsync = viewMenu.AddOption( "VSync", on => VSync = on );
		viewMenu.AboutToShow += m => vsync.Checked = VSync;
		viewMenu.AddOption( "Reset Camera", "center_focus_strong", ResetCamera );
		viewMenu.AddSeparator();
		viewMenu.AddOption( "Compare With Native", "compare", () => compareRequested = OutputFolder() );
		var planOption = viewMenu.AddOption( "Frame Plan", on => planOverlay.Level = on ? Math.Max( 1, planOverlay.Level ) : 0 );
		viewMenu.AboutToShow += m => planOption.Checked = planOverlay.Level > 0;
		var planAll = viewMenu.AddOption( "Frame Plan: Every Layer", on => planOverlay.Level = on ? 2 : planOverlay.Level > 0 ? 1 : 0 );
		viewMenu.AboutToShow += m => planAll.Checked = planOverlay.Level > 1;

		bar.Add.Panel( "grow" );

		WindowButton( bar, "remove", null, Minimize );
		WindowButton( bar, "crop_square", null, ToggleMaximized );
		WindowButton( bar, "close", "close", RequestClose );

		viewport = root.AddChild( new SceneViewport( this ) );

		// Over the corner of the scene rather than in the title bar, where it didn't fit
		stats = viewport.Add.Label( "", "scenelab-stats" );
		planOverlay = viewport.AddChild( new PlanOverlay { Level = Args.Has( "-plan" ) ? Math.Max( 1, Args.Int( "-plan", 1 ) ) : 0 } );

		return root;
	}

	static void WindowButton( Panel bar, string icon, string classname, Action onClick )
	{
		var button = bar.Add.Panel( "windowbutton window-nodrag" );
		if ( classname is not null ) button.AddClass( classname );

		button.Add.Icon( icon, "icon" );
		button.AddEventListener( "onclick", onClick );
	}

	const string Styles = """
		@import "_palette.scss";

		// The world shows through everywhere but the title bar
		.editor-window.scenelab { flex-direction: column; background-color: transparent; }
		.scenelab .titlebar { height: 40px; padding: 0 6px 0 16px; gap: 8px; justify-content: flex-start; background-color: $surface; border-bottom: 1px solid $line; }
		.scenelab .titlebar .grow { flex-grow: 1; }
		.scenelab .scenelab-title { font-weight: 600; color: $text-muted; }
		.scenelab .sceneviewport { flex-grow: 1; position: relative; pointer-events: all; }

		// Monospaced and fixed-width fields, so the numbers stay put as they change
		.scenelab .scenelab-plan { position: absolute; top: 0; left: 0; right: 0; bottom: 0; pointer-events: none; }
		.scenelab .scenelab-plan.hidden { display: none; }
		.scenelab .scenelab-stats { position: absolute; top: 10px; left: 10px; padding: 8px 10px; border-radius: 4px; background-color: rgba( 0, 0, 0, 0.55 ); font-family: Roboto Mono; font-size: 11px; color: #d8dce4; white-space: pre; pointer-events: none; }
		""";

	/// <summary>
	/// Replace the world with a new scene. Models are kept for the life of the window - a frame
	/// still on the GPU can be reading the old scene's buffers.
	/// </summary>
	public void Load( SceneLabScene scene )
	{
		Scene = scene;
		if ( LoadGame( scene ) ) return;

		world = new RenderWorld();
		orbitingLights.Clear();

		nativeScene?.Dispose();
		nativeScene = null;

		var models = scene.Models.Select( GetMesh ).ToArray();
		var first = models[0];
		var spacing = models.Max( m => MathF.Max( m.Bounds.Size.x, m.Bounds.Size.y ) ) * 1.5f;
		var offset = (scene.Grid - 1) * spacing * 0.5f;

		var material = scene.Material is null ? null : Material.Load( scene.Material );
		var checker = scene.Checker.Select( x => (Material: x.Material is null ? null : Material.Load( x.Material ), x.Alpha) ).ToArray();

		for ( int x = 0; x < scene.Grid; x++ )
		{
			for ( int y = 0; y < scene.Grid; y++ )
			{
				var position = new Vector3( x * spacing - offset, y * spacing - offset, 0 );
				var obj = new MeshObject( models[(x + y) % models.Length], new Transform( position ) ) { IsStatic = scene.Static, MaterialOverride = material };

				var cell = (x + y) % (checker.Length + 1) - 1;
				if ( cell >= 0 )
				{
					obj.MaterialOverride = checker[cell].Material ?? material;
					obj.Tint = Color.White.WithAlpha( checker[cell].Alpha );
				}

				if ( scene.Sequences.Length > 0 && obj.Mesh.IsSkinned )
					Pose( obj, scene.Sequences[(x * scene.Grid + y) % scene.Sequences.Length], Args.Float( "-sequence-time", scene.SequenceTime ) );

				world.Add( obj );
			}
		}

		if ( scene.EnvMap )
		{
			// A box around the grid, tall as it's wide - a room for the reflections to project onto
			var half = MathF.Max( scene.Grid * spacing, spacing * 2 ) * 0.8f;
			var bounds = new BBox( new Vector3( -half, -half, first.Bounds.Mins.z ), new Vector3( half, half, half ) );
			world.Add( new EnvMapObject( Texture.Load( "textures/cubemaps/default.vtex" ), bounds, Transform.Zero ) );
		}

		if ( scene.Sky is not null )
		{
			// What SkyBox2D sets up: the sky, and a probe of its cubemap around everything, at the lowest priority
			var sky = Material.Load( scene.Sky );
			world.Lighting.SkyMaterial = sky;
			world.Add( new EnvMapObject( sky.GetTexture( "g_tSkyTexture" ), BBox.FromPositionAndSize( Vector3.Zero, int.MaxValue ), Transform.Zero )
			{
				Priority = -5,
				Feathering = 0.01f,
			} );
		}

		if ( scene.ImageBasedAmbient )
			world.Lighting.AmbientColor = world.Lighting.AmbientColor.WithAlpha( 0 );

		// Fog scaled to the grid: clear close up, thick at its far edge, thinning above the objects
		var size = scene.Grid * spacing;
		if ( scene.GradientFog )
		{
			world.Lighting.GradientFog = new Rendering.GradientFogSetup
			{
				Enabled = true,
				StartDistance = size * 0.05f,
				EndDistance = size * 0.8f,
				StartHeight = 0,
				EndHeight = first.Bounds.Size.z * 4,
				MaximumOpacity = 0.9f,
				DistanceFalloffExponent = 1,
				VerticalFalloffExponent = 1,
				Color = new Color( 0.55f, 0.6f, 0.7f ),
			};
		}

		if ( scene.CubemapFog )
		{
			var fog = world.Lighting.CubemapFog;
			fog.Enabled = true;
			fog.Texture = sky ??= SkyCube.Create();
			fog.StartDistance = size * 0.05f;
			fog.EndDistance = size * 0.8f;
			fog.FalloffExponent = 1;
			fog.LodBias = 0.5f;
			fog.Tint = Color.White;
			fog.Transform = Transform.Zero;
		}

		if ( scene.SunDirection is { } sunDirection )
			world.Lighting.SunDirection = sunDirection;

		focus = first.Bounds.Center;
		defaultDistance = MathF.Max( first.Bounds.Size.Length * 1.6f, scene.Grid * spacing * 1.2f );
		ResetCamera();

		if ( scene.HasLocalLights || scene.Floor )
			AddLights( scene, first, MathF.Max( scene.Grid * spacing, spacing * 2 ) );

		timer.Clear();
		Title = $"Scene Lab - {scene.Name}";
		Log.Info( $"Scene Lab: {scene.Name}, {world.Count} objects" );
	}

	RenderMesh GetMesh( string path )
	{
		if ( meshes.TryGetValue( path, out var entry ) ) return entry.Mesh;

		var model = path == LodTestModel.Path ? LodTestModel.Create() : Model.Load( path );
		var mesh = RenderMesh.FromModel( model );
		meshes[path] = (model, mesh);

		var info = model.MeshInfo;
		Log.Info( $"Scene Lab: {path}: {info.LodCount} LODs, {info.TotalTriangles} triangles across them, LOD 0 is {mesh.TriangleCount} triangles" );
		return mesh;
	}

	Dictionary<RenderMesh, Model> ModelsByMesh() => meshes.Values.ToDictionary( x => x.Mesh, x => x.Model );

	/// <summary>
	/// Pose a skinned object from a sequence, frozen at <paramref name="time"/> (0 to 1): a native
	/// <see cref="SceneModel"/> in a world that's never rendered evaluates it, and the object takes its bones and
	/// bounds. The native reference then takes the object's bones, so both draw the same pose.
	/// </summary>
	void Pose( MeshObject obj, string sequence, float time )
	{
		animationWorld ??= new SceneWorld();

		var animator = new SceneModel( animationWorld, obj.Mesh.Model, obj.Transform ) { UseAnimGraph = false, PlaybackRate = 0 };
		animator.CurrentSequence.Name = sequence;
		animator.CurrentSequence.TimeNormalized = time;
		animator.Update( 0 );

		var bones = new Transform[obj.Mesh.Model.BoneCount];
		animator.GetBoneWorldTransforms( bones );
		obj.SetBones( bones );
		// Grid objects are only placed, not rotated or scaled, so local bounds are the world bounds moved back
		var bounds = animator.Bounds;
		var position = obj.Transform.Position;
		obj.LocalBounds = new BBox( bounds.Mins - position, bounds.Maxs - position );

		animator.Delete();
	}

	public void ResetCamera()
	{
		if ( game is not null )
		{
			(Yaw, Pitch, Distance) = gameCamera;
			return;
		}

		Yaw = 35;
		Pitch = 25;
		Distance = defaultDistance * Args.Float( "-zoom", 1 );
	}

	/// <summary>
	/// A floor to catch the light and the shadows, a ring of point lights over it, and a spot
	/// straight down.
	/// </summary>
	void AddLights( SceneLabScene scene, RenderMesh mesh, float extent )
	{
		// Let the local lights carry the scene
		if ( scene.HasLocalLights )
		{
			world.Lighting.SunColor = world.Lighting.SunColor * scene.Sun;
			world.Lighting.AmbientColor = new Color( 0.02f, 0.02f, 0.03f );
		}

		var floor = GetMesh( "models/dev/plane.vmdl" );
		var floorSize = MathF.Max( floor.Bounds.Size.x, 1 );
		var floorZ = mesh.Bounds.Mins.z - floor.Bounds.Maxs.z;
		world.Add( new MeshObject( floor, new Transform( new Vector3( 0, 0, floorZ ), Rotation.Identity, extent * 1.6f / floorSize ) ) { IsStatic = scene.Static } );

		lightRingRadius = extent * 0.35f;
		lightHeight = mesh.Bounds.Maxs.z + mesh.Bounds.Size.z;

		for ( int i = 0; i < scene.Lights; i++ )
		{
			var light = new LightObject( LightObject.LightKind.Point, Transform.Zero )
			{
				Color = new ColorHsv( i * 360.0f / scene.Lights, 0.75f, 1 ).ToColor() * 4,
				Radius = extent * scene.LightRadius,
				CastShadows = scene.LightShadows,
			};

			orbitingLights.Add( light );
			world.Add( light );
		}

		if ( scene.Spot )
		{
			world.Add( new LightObject( LightObject.LightKind.Spot, new Transform( new Vector3( 0, 0, lightHeight * 4 ), Rotation.LookAt( Vector3.Down ) ) )
			{
				Color = new Color( 1.0f, 0.95f, 0.8f ) * 6,
				Radius = lightHeight * 8,
				ConeInner = 15,
				ConeOuter = 25,
				CastShadows = scene.LightShadows,
				IsStatic = scene.Static,
			} );
		}

		MoveLights( 0 );
	}

	void MoveLights( float time )
	{
		for ( int i = 0; i < orbitingLights.Count; i++ )
		{
			var angle = time * 0.5f + i * MathF.Tau / orbitingLights.Count;
			var position = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * lightRingRadius + Vector3.Up * lightHeight;
			orbitingLights[i].Transform = new Transform( position );
		}
	}

	readonly string gameScenePath = Args.String( "-gamescene" );
	Scene gameScene;

	/// <summary>
	/// <c>-gamescene path.scene</c>: load a GameObject scene, tick it, and at the capture frame compare its main
	/// camera through native and the managed renderer's GameObject bridge - what <c>r_managed_scene_compare</c>
	/// does in a game. Frames go to <c>screenshots/scenelab/gamescene</c>; exit code 2 if they differ.
	/// </summary>
	void RunGameScene()
	{
		if ( gameScene is null )
		{
			if ( gameScenePath == "builtin" ) gameScene = BridgeScene.Create();
		}

		if ( gameScene is null )
		{
			gameScene = new Scene();
			using ( gameScene.Push() )
			{
				// A file on disk loads from its JSON, so it doesn't need its addon mounted; otherwise it's a resource
				var loaded = false;
				if ( System.IO.File.Exists( gameScenePath ) )
				{
					var file = new SceneFile();
					file.LoadFromJson( System.IO.File.ReadAllText( gameScenePath ) );
					var options = new SceneLoadOptions();
					loaded = options.SetScene( file ) && gameScene.Load( options );
				}
				else
				{
					loaded = gameScene.LoadFromFile( gameScenePath );
				}

				if ( !loaded ) Log.Warning( $"Scene Lab: couldn't load {gameScenePath}" );
			}
		}

		using ( gameScene.Push() )
			gameScene.GameTick( 1.0 / 60.0 );

		frame++;

		// The builtin scene is compared twice: as built, then after changing it, to prove the mirror follows changes
		var changedFrame = captureFrame + 10;
		if ( frame != captureFrame && frame != changedFrame ) return;

		var camera = gameScene.Camera;
		if ( camera is null )
		{
			Log.Warning( $"Scene Lab: {gameScenePath} has no main camera" );
			Finished = true;
			return;
		}

		var folder = System.IO.Path.Combine( OutputFolder(), "gamescene" );
		System.IO.Directory.CreateDirectory( folder );

		// One bridge for both, as a game has: its mirror keeps listening between them
		gameBridge ??= new Sandbox.SceneRenderer.Bridge.SceneBridge();
		var changed = frame == changedFrame;
		Log.Info( $"Scene Lab: {gameScenePath}{(changed ? ", changed" : "")}: {gameBridge.Compare( camera, folder, changed ? "changed" : "camera" )}" );
		if ( gameBridge.LastParity is { Matches: false } ) Environment.ExitCode = 2;

		if ( !changed && gameScenePath == "builtin" )
		{
			BridgeScene.Change( gameScene );
			return;
		}

		Finished = true;
	}

	Sandbox.SceneRenderer.Bridge.SceneBridge gameBridge;

	// The frame plan over the viewport
	PlanOverlay planOverlay;

	/// <summary>
	/// Render the world into the swap chain, under the panels.
	/// </summary>
	private protected override void OnRenderBackground( SwapChainHandle_t swapChain, Vector2 size )
	{
		// A multisampled window is the main one, as a game's is: native's bitmap renders (the references, and the GameObject
		// presets' compare) take the main swap chain's sample count
		if ( SwapChainMultisample != RenderMultisampleType.RENDER_MULTISAMPLE_NONE ) CSceneSystem.SetMainSwapChain( swapChain );

		parity?.Tick( this );
		if ( Finished || parity?.Idle == true ) return;

		// The plan overlay asks for the frames it shows; a capture with it saves one too, so ask from the start
		if ( planOverlay?.Level > 0 ) Sandbox.Rendering.ManagedFramePlan.Want();

		if ( gameScenePath is not null )
		{
			if ( !Finished ) RunGameScene();
			return;
		}

		if ( game is not null )
		{
			RenderGame( swapChain, size );
			return;
		}

		var capturing = CapturePath is not null || compareRequested is not null;

		// A capture takes the whole window, so it lines up with the native reference - the title
		// bar only gets drawn over it afterwards
		var rect = capturing || viewport is null ? new Rect( 0, 0, size.x, size.y ) : viewport.Box.Rect;
		if ( rect.Width < 1 || rect.Height < 1 ) return;

		view.Viewport = rect;
		view.Rotation = Rotation.From( Pitch, Yaw, 0 );
		view.Position = focus - view.Rotation.Forward * Distance;
		view.ZNear = MathF.Max( 0.1f, Distance * 0.01f );
		view.ZFar = Distance * 10.0f;

		// Captures freeze the lights so they're repeatable
		if ( Scene.Orbit ) MoveLights( capturing ? 0 : RealTime.Now );

		timer.BeginRender();

		if ( Renderer == RendererKind.Native && !capturing )
		{
			nativeScene ??= new NativeScene( world, ModelsByMesh(), renderer.Shadows );
			nativeScene.Render( swapChain, view, size );
		}
		else if ( RenderToTexture )
		{
			// The whole target is the view: sized to the viewport, or the window when capturing
			var targetSize = new Vector2Int( (int)rect.Width, (int)rect.Height );
			if ( target is null || target.Size != targetSize )
			{
				target?.Dispose();
				target = ViewTarget.Create( targetSize, targetMsaa, targetFormat, name: "Scene Lab" );
			}

			view.Viewport = new Rect( 0, 0, targetSize.x, targetSize.y );
			renderer.Render( world, view, target );
		}
		else
		{
			renderer.Render( world, view, swapChain, size );
		}

		// The panel shows the texture over the swap chain, which nothing drew into
		if ( viewport is not null ) viewport.Style.BackgroundImage = RenderToTexture && Renderer == RendererKind.Managed ? target?.Resolved : null;

		timer.EndRender( swapChain );
		frame++;

		if ( benchmark is not null )
		{
			if ( benchmark.Frame( this, timer ) )
			{
				Finished = true;
				benchmark = null;
			}

			return;
		}

		UpdateStats();

		if ( compareRequested is not null )
		{
			var folder = compareRequested;
			compareRequested = null;
			var name = new string( Scene.Name.Where( char.IsLetterOrDigit ).ToArray() ).ToLowerInvariant();
			if ( Capture( swapChain, size, System.IO.Path.Combine( folder, $"{name}.png" ), native: true ) is { } p )
				ShowMessage( p.ToString() );
		}

		if ( CapturePath is not null && frame == captureFrame )
		{
			var result = Capture( swapChain, size, CapturePath, Args.Has( "-native" ) || parity is not null );
			Log.Info( $"Scene Lab: {renderer.Stats}" );
			CapturePlan( CapturePath );
			if ( pendingReference is null ) FinishCapture( result );
		}

		// A few frames on, so the queued native view has rendered and the GPU is done with it
		if ( pendingReference is { } pending && frame >= pending.ReadFrame )
		{
			pendingReference = null;
			var native = FrameComparison.ReadLinear( pending.Texture );
			Save( native, pending.Texture.Width, pending.Texture.Height, pending.Path );
			pending.Texture.Dispose();
			pending.Scene.Dispose();

			var result = CompareReference( native, pending.Managed, pending.Texture.Width, pending.Texture.Height, pending.Path );

			if ( CapturePath is not null ) FinishCapture( result );
			else ShowMessage( result.ToString() );
		}
	}

	/// <summary>
	/// With the plan overlay on, save the frame's plan beside a capture as <c>name.plan.png</c> - the overlay itself is a
	/// panel, drawn after the capture reads the frame.
	/// </summary>
	void CapturePlan( string path )
	{
		if ( planOverlay is not { Level: > 0 } || path is null ) return;

		const int width = 1180, height = 900;
		var planPath = System.IO.Path.ChangeExtension( path, ".plan.png" );
		Save( PlanOverlay.Render( width, height, planOverlay.Level ), width, height, planPath );
		Log.Info( $"Scene Lab: frame plan to {planPath}" );
	}

	void FinishCapture( FrameComparison.Parity? result )
	{
		if ( result is { Matches: false } ) Environment.ExitCode = 2;
		parity?.Report( result?.ToString() ?? "no result", result );
		CaptureDone();
	}

	void ShowMessage( string text )
	{
		message = text;
		messageTime = 5;
	}

	/// <summary>
	/// Twice a second: the frame costs for the renderer on screen, and the managed renderer's own
	/// numbers when it's the one drawing.
	/// </summary>
	void UpdateStats()
	{
		if ( stats is null || statsUpdated < 0.5f || timer.Count < 2 ) return;
		statsUpdated = 0;

		var summary = timer.Summarise();
		timer.Clear();

		var managed = Renderer == RendererKind.Managed;
		var name = managed && !renderer.DepthPrepass ? "Managed, no prepass" : Renderer.ToString();

		var text = new System.Text.StringBuilder();
		if ( message is not null && !messageTime ) text.AppendLine( message ).AppendLine();

		text.AppendLine( $"{name}{(VSync ? ", vsync" : "")}" );
		text.AppendLine( $"fps     {summary.Fps,8:0}" );
		text.AppendLine( $"frame   {summary.FrameMs,8:0.00} ms   p99 {summary.FrameP99Ms,6:0.00} ms" );
		text.AppendLine( $"render  {summary.RenderMs,8:0.000} ms   alloc {summary.AllocBytes,6:0} B" );
		text.Append( $"gpu     {(summary.GpuMs > 0 ? $"{summary.GpuMs,8:0.00} ms" : "     n/a")}" );

		if ( managed )
		{
			var r = GameStats() ?? renderer.Stats;
			text.AppendLine().AppendLine();
			text.AppendLine( $"visible {r.ObjectsVisible,8}   of {r.Objects}, {r.ObjectsSizeCulled} too small" );
			text.AppendLine( $"draws   {r.Draws,8}   {r.Instances} instances, {r.Triangles / 1000000.0:0.0}M tris" );
			text.AppendLine( $"blend   {r.TranslucentDraws,8}   draws" );
			text.AppendLine( $"depth   {r.DepthDraws,8}   draws" );
			text.AppendLine( $"lights  {r.Lights,8}" );
			text.AppendLine( $"shadows {r.ShadowViews,8}   views, {r.ShadowDraws} draws" );
			text.AppendLine();
			text.AppendLine( $"collect {r.CollectMs,8:0.000} ms" );
			text.AppendLine( $"prepare {r.PrepareMs,8:0.000} ms" );
			text.Append( $"record  {r.DrawMs,8:0.000} ms" );
		}

		stats.Text = text.ToString();
	}

	/// <summary>
	/// Where captures and benchmark results go.
	/// </summary>
	public static string OutputFolder()
	{
		var folder = System.IO.Path.Combine( Environment.CurrentDirectory, "screenshots", "scenelab" );
		System.IO.Directory.CreateDirectory( folder );
		return folder;
	}

	/// <summary>
	/// Read the frame back before anything else draws on it - the swap chain, or the target when rendering to
	/// a texture - save it, and optionally render and compare the native reference. Native renders a bitmap,
	/// or for a multisampled target a texture at the same sample count, which is compared a few frames later
	/// (<see cref="pendingReference"/>) and gives null here.
	/// </summary>
	FrameComparison.Parity? Capture( SwapChainHandle_t swapChain, Vector2 size, string path, bool native )
	{
		var managedTarget = RenderToTexture ? target : null;
		var pixels = managedTarget is null ? ReadSwapChain( swapChain, size ) : ReadTarget( managedTarget );
		if ( pixels is null ) return null;

		Save( pixels, (int)size.x, (int)size.y, path );
		Log.Info( $"Scene Lab: captured frame {frame} to {path}{(managedTarget is null ? "" : $" from a {managedTarget.Color.ImageFormat} target, {managedTarget.Samples}x MSAA")}" );
		if ( !native ) return null;

		var nativePath = System.IO.Path.ChangeExtension( path, ".native.png" );
		var reference = new NativeScene( world, ModelsByMesh(), renderer.Shadows );

		if ( managedTarget is { Samples: > 1 } )
		{
			var texture = reference.RenderToTexture( view, size, managedTarget.Color.MultisampleType );
			pendingReference = (reference, texture, pixels, nativePath, frame + 3);
			return null;
		}

		using ( reference )
		{
			return CompareReference( reference.Capture( view, size, nativePath ), pixels, (int)size.x, (int)size.y, nativePath );
		}
	}

	/// <summary>
	/// Compare the frames, and save where they differ next to the native one as <c>.diff.png</c>.
	/// </summary>
	static FrameComparison.Parity CompareReference( Color32[] native, Color32[] managed, int width, int height, string nativePath )
	{
		var parity = FrameComparison.Compare( managed, native );
		if ( managed.Length == native.Length ) Save( FrameComparison.Difference( managed, native ), width, height, nativePath.Replace( ".native.png", ".diff.png" ) );
		Log.Info( $"Scene Lab: native reference to {nativePath}" );
		Log.Info( $"Scene Lab: {parity}" );
		return parity;
	}

	static void Save( Color32[] pixels, int width, int height, string path )
	{
		using var bitmap = new Bitmap( width, height );
		bitmap.SetPixels( pixels.Select( x => x.ToColor() ).ToArray() );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	}

	static unsafe Color32[] ReadSwapChain( SwapChainHandle_t swapChain, Vector2 size )
	{
		var width = (int)size.x;
		var height = (int)size.y;
		var pixels = new Color32[width * height];
		var texture = g_pRenderDevice.GetSwapChainTexture( swapChain, SwapChainBuffer.BufferColor );

		var src = new NativeRect { x = 0, y = 0, w = width, h = height };
		var dst = src;
		fixed ( Color32* ptr = pixels )
		{
			if ( !g_pRenderDevice.ReadTexturePixels( texture, ref src, 0, 0, ref dst, (IntPtr)ptr, ImageFormat.RGBA8888, width * 4 ) )
			{
				Log.Warning( "Scene Lab: couldn't read the swap chain back" );
				return null;
			}
		}

		return pixels;
	}

	/// <summary>
	/// A target's resolved colour as 8-bit sRGB: a float target is encoded the way native's final blit
	/// would, and an 8-bit one was written through an sRGB view already.
	/// </summary>
	static Color32[] ReadTarget( ViewTarget target )
	{
		return ViewTarget.IsFloatFormat( target.Resolved.ImageFormat ) ? FrameComparison.ReadLinear( target.Resolved ) : target.Resolved.GetPixels();
	}

	private protected override void OnClosing()
	{
		// Let the GPU finish the frames in flight - they still read the buffers about to be freed
		Window?.Flush();

		UnloadGame();
		gameBridge?.Shutdown();
		gameBridge = null;

		nativeScene?.Dispose();
		nativeScene = null;

		if ( pendingReference is { } pending )
		{
			pending.Texture.Dispose();
			pending.Scene.Dispose();
			pendingReference = null;
		}

		if ( viewport is not null ) viewport.Style.BackgroundImage = null;
		target?.Dispose();
		target = null;

		animationWorld?.Delete();
		animationWorld = null;

		meshes.Clear();
		renderer.Dispose();
	}
}
