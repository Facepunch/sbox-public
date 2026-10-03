namespace Sandbox.SceneLab;

/// <summary>
/// What a Scene Lab scene is made of: a grid of one model, and optionally a floor with lights over it.
/// </summary>
internal sealed record SceneLabScene
{
	public string Name { get; init; }

	/// <summary>
	/// The Scene menu's submenu it's in.
	/// </summary>
	public string Group { get; init; } = "Basics";

	/// <summary>
	/// Builds a GameObject scene instead of a grid, drawn through the managed renderer's GameObject bridge - for what only
	/// exists as components (<see cref="GameScenes"/>). The other settings don't apply to it.
	/// </summary>
	public Func<Scene> Game { get; init; }

	/// <summary>
	/// Changes a <see cref="Game"/> scene through its components, for a second compare - proving the change arrives.
	/// </summary>
	public Action<Scene> ChangeGame { get; init; }

	/// <summary>
	/// Runs every frame on a <see cref="Game"/> scene, after it ticks and before it renders - for drawing that's redone
	/// each frame, like a HUD.
	/// </summary>
	public Action<Scene> Frame { get; init; }

	/// <summary>
	/// Whether a <see cref="Game"/> scene has finished loading - a map downloading, say. Captures count their frames from
	/// when it's true.
	/// </summary>
	public Func<Scene, bool> Ready { get; init; }

	/// <summary>
	/// A <see cref="Game"/> scene's camera also renders natively every frame, into a texture no one sees, while the managed
	/// renderer draws the window - for effects that carry frames forward, like volumetric fog's history, so native's has run as
	/// many frames as the managed one's when they're compared.
	/// </summary>
	public bool NativeAlongside { get; init; }

	/// <summary>
	/// Compare each renderer from a fresh scene load: effects such as AO and SSR keep history on the camera,
	/// so reusing the scene would mix the renderers' frames. The built-in <c>-parity</c> runner automatically
	/// performs isolated native and managed passes for this preset.
	/// </summary>
	public bool Isolated { get; init; }

	public bool IsGame => Game is not null;

	/// <summary>
	/// Laid out in a Grid x Grid square. With more than one, neighbours alternate between them.
	/// </summary>
	public string[] Models { get; init; } = ["models/dev/box.vmdl"];

	public int Grid { get; init; } = 1;

	/// <summary>
	/// Coloured point lights circling over a floor.
	/// </summary>
	public int Lights { get; init; }

	/// <summary>
	/// Point light reach, as a fraction of the grid's size.
	/// </summary>
	public float LightRadius { get; init; } = 0.4f;

	/// <summary>
	/// A spot light pointing straight down at the middle.
	/// </summary>
	public bool Spot { get; init; }

	/// <summary>
	/// Sun brightness while there are local lights, which carry the scene.
	/// </summary>
	public float Sun { get; init; } = 0.1f;

	/// <summary>
	/// The point lights circle. When they're still, their shadow maps are cached and only re-render a few
	/// at a time (<c>r.shadows.updates</c>), in both renderers.
	/// </summary>
	public bool Orbit { get; init; } = true;

	/// <summary>
	/// The point and spot lights cast shadows. The sun always does, unless shadows are switched off.
	/// </summary>
	public bool LightShadows { get; init; }

	/// <summary>
	/// Everything marked static - lights keep their static casters' shadows in a cache, rendered once,
	/// and copy it in each frame. The frames should come out the same as without.
	/// </summary>
	public bool Static { get; init; }

	/// <summary>
	/// A floor under the grid even without local lights, for the sun's shadows to land on.
	/// </summary>
	public bool Floor { get; init; }

	/// <summary>
	/// Drawn with this material rather than the models' own.
	/// </summary>
	public string Material { get; init; }

	/// <summary>
	/// Materials and tint alphas the grid cycles through, in a checker with the models' own: cell (x, y) gets
	/// entry (x + y) % (n + 1) - 1, or nothing when that's -1. A null material keeps the model's own, so an alpha
	/// under 1 fades it.
	/// </summary>
	public (string Material, float Alpha)[] Checker { get; init; } = [];

	/// <summary>
	/// Animation sequences the grid's skinned models cycle through, each posed at <see cref="SequenceTime"/> and
	/// frozen there - so the managed and native frames are of the same pose. Empty leaves them in bind pose.
	/// </summary>
	public string[] Sequences { get; init; } = [];

	/// <summary>
	/// Where in each sequence the models are posed, 0 to 1.
	/// </summary>
	public float SequenceTime { get; init; } = 0.3f;

	/// <summary>
	/// A 2D sky material drawn behind everything, with a whole-world probe of its cubemap for its light, as
	/// <c>SkyBox2D</c> sets one up.
	/// </summary>
	public string Sky { get; init; }

	/// <summary>
	/// An environment map probe around the grid, box projected, for reflections.
	/// </summary>
	public bool EnvMap { get; init; }

	/// <summary>
	/// Ambient light from the probe (image based) rather than a flat color.
	/// </summary>
	public bool ImageBasedAmbient { get; init; }

	/// <summary>
	/// Gradient fog, thickening into the distance and thinning with height.
	/// </summary>
	public bool GradientFog { get; init; }

	/// <summary>
	/// Cubemap fog, fading the distance into a cubemap.
	/// </summary>
	public bool CubemapFog { get; init; }

	/// <summary>
	/// Where the sunlight travels, if not the default - which is close to the way the camera looks,
	/// so shadows fall behind what casts them.
	/// </summary>
	public Vector3? SunDirection { get; init; }

	/// <summary>
	/// Low and from the side, so shadows are long and in view.
	/// </summary>
	static readonly Vector3 SideSun = new Vector3( -0.4f, 0.7f, -0.55f ).Normal;

	public bool HasLocalLights => Lights > 0 || Spot;

	/// <summary>
	/// Full-body citizen animations (a jump, a wave, a turn, fidgets), and two still poses - so a pose at another
	/// <c>-sequence-time</c> is a different pose.
	/// </summary>
	static readonly string[] CitizenSequences = ["AvatarMenu_Entry_Jump", "AvatarMenu_Entry_Wave_01", "AvatarMenu_Entry_TurnAround", "AvatarMenu_FidgetLarge_03", "AvatarMenu_ExamineLegs_02", "SitPose_Default", "CrouchIdlePose_Default"];

	public static readonly SceneLabScene[] Presets =
	[
		new() { Name = "Box" },
		new() { Name = "Box Grid", Grid = 8 },
		new() { Name = "Spheres", Models = ["models/dev/sphere.vmdl"], Grid = 6, Lights = 6 },
		new() { Name = "Point Lights", Group = "Lights", Grid = 4, Lights = 8 },
		new() { Name = "Spot Light", Group = "Lights", Grid = 2, Spot = true, Sun = 0 },
		new() { Name = "Point Lights and Spot", Group = "Lights", Grid = 4, Lights = 8, Spot = true },
		new() { Name = "256 Lights", Group = "Lights", Grid = 10, Lights = 256, LightRadius = 0.08f, Sun = 0 },
		new() { Name = "256 Overlapping Lights", Group = "Lights", Grid = 10, Lights = 256, Sun = 0 },

		// Shadows from each kind of light: sun cascades, a spot's projection, point light cubes
		new() { Name = "Sun Shadows", Group = "Shadows", Grid = 6, Floor = true, SunDirection = SideSun },
		new() { Name = "Spot Shadow", Group = "Shadows", Grid = 3, Spot = true, LightShadows = true, Sun = 0 },
		new() { Name = "Static Spot Shadow", Group = "Shadows", Grid = 3, Spot = true, LightShadows = true, Sun = 0, Static = true },
		new() { Name = "Point Shadows", Group = "Shadows", Grid = 4, Lights = 4, LightShadows = true, Sun = 0 },
		new() { Name = "All Shadows", Group = "Shadows", Grid = 4, Lights = 8, Spot = true, LightShadows = true, Sun = 0.3f, SunDirection = SideSun },

		// Environment maps: glossy metal reflecting a probe, then the probe lighting the ambient too
		new() { Name = "Reflections", Group = "Environment", Models = ["models/dev/sphere.vmdl"], Grid = 4, Floor = true, Material = "materials/dev/dev_metal_rough10.vmat", EnvMap = true, SunDirection = SideSun },
		new() { Name = "Gradient Fog", Group = "Environment", Grid = 30, Floor = true, GradientFog = true, SunDirection = SideSun },
		new() { Name = "Cubemap Fog", Group = "Environment", Grid = 30, Floor = true, CubemapFog = true, SunDirection = SideSun },
		new() { Name = "Image Based Ambient", Group = "Environment", Models = ["models/dev/sphere.vmdl"], Grid = 4, Floor = true, EnvMap = true, ImageBasedAmbient = true, SunDirection = SideSun },
		new() { Name = "Sky", Group = "Environment", Models = ["models/dev/sphere.vmdl"], Grid = 4, Floor = true, Material = "materials/dev/dev_metal_rough10.vmat", Sky = "materials/skybox/skybox_day_01.vmat", ImageBasedAmbient = true, SunDirection = SideSun },

		// Blending: translucent boxes overlapping each other and opaque ones, lit, shadowed and fogged - a lit
		// complex material at half opacity, and an unlit generic one - then opaque boxes faded by their tint,
		// dithered on screen and into the sun's and a spot's shadow maps
		new() { Name = "Translucency", Group = "Blending", Grid = 5, Floor = true, Lights = 6, Sun = 0.5f, GradientFog = true, SunDirection = SideSun,
			Checker = [("materials/dev/primary_white_trans.vmat", 0.5f), ("materials/dev/primary_red_additive.vmat", 1)] },
		new() { Name = "Faded Shadows", Group = "Blending", Grid = 4, Floor = true, Spot = true, LightShadows = true, Sun = 0.5f, SunDirection = SideSun,
			Checker = [(null, 0.5f)] },

		// Skinned: citizens posed from a few sequences, frozen, under the sun's cascades and a spot's shadow
		new() { Name = "Citizens", Group = "Skinning", Models = ["models/citizen/citizen.vmdl"], Grid = 3, Floor = true, Spot = true, LightShadows = true, Sun = 0.6f, SunDirection = SideSun,
			Sequences = CitizenSequences },

		// A model whose LODs are different shapes, so LOD choices can be seen and compared with native
		new() { Name = "LOD Test", Group = "Basics", Models = [LodTestModel.Path], Grid = 40 },

		// Big enough to measure - see the -benchmark switch
		new() { Name = "10k Boxes", Group = "Stress", Grid = 100 },
		new() { Name = "50k Mixed", Group = "Stress", Models = ["models/dev/box.vmdl", "models/dev/sphere.vmdl"], Grid = 224 },
		new() { Name = "10k Boxes, 256 Lights", Group = "Stress", Grid = 100, Lights = 256, LightRadius = 0.05f },
		new() { Name = "10k Boxes, 64 Shadowed Lights", Group = "Stress", Grid = 100, Lights = 64, LightRadius = 0.05f, LightShadows = true },
		new() { Name = "10k Boxes, 64 Still Shadowed Lights", Group = "Stress", Grid = 100, Lights = 64, LightRadius = 0.05f, LightShadows = true, Orbit = false },
		new() { Name = "50k LOD Test", Group = "Stress", Models = [LodTestModel.Path], Grid = 224 },
		new() { Name = "100 Citizens", Group = "Stress", Models = ["models/citizen/citizen.vmdl"], Grid = 10, Floor = true, Sun = 0.6f, SunDirection = SideSun,
			Sequences = CitizenSequences },

		// GameObject scenes, through the managed renderer's GameObject bridge (r_managed_scene) - see GameScenes
		new() { Name = "GameObject Bridge", Group = "GameObject", Game = BridgeScene.Create, ChangeGame = BridgeScene.Change },
		new() { Name = "Post Processing", Group = "Post Processing", Game = GameScenes.PostProcessing, ChangeGame = GameScenes.ChangePostProcessing },
		new() { Name = "Depth of Field", Group = "Post Processing", Game = GameScenes.DepthOfField },
		new() { Name = "Auto Exposure", Group = "Post Processing", Game = GameScenes.AutoExposure },
		new() { Name = "Highlight Outlines", Group = "Post Processing", Game = GameScenes.HighlightOutlines, ChangeGame = GameScenes.ChangeHighlightOutlines },
		new() { Name = "Screen UI", Group = "UI", Game = GameScenes.ScreenUI, ChangeGame = GameScenes.ChangeScreenUI, Frame = GameScenes.DrawHud },
		new() { Name = "Early UI", Group = "UI", Game = GameScenes.EarlyUI },
		new() { Name = "World Panel", Group = "UI", Game = GameScenes.WorldUI },
		new() { Name = "Output Overlays", Group = "UI", Game = GameScenes.OutputOverlays, ChangeGame = GameScenes.ChangeOutputOverlays },
		new() { Name = "Sprites and Particles", Group = "Effects", Game = GameScenes.SpritesAndParticles, ChangeGame = GameScenes.ChangeSpritesAndParticles },
		new() { Name = "Decals", Group = "Effects", Game = GameScenes.Decals, ChangeGame = GameScenes.ChangeDecals },
		new() { Name = "Tools View", Group = "Tools", Game = GameScenes.ToolsView, ChangeGame = GameScenes.ChangeToolsView },
		new() { Name = "Debug Views", Group = "Tools", Game = GameScenes.DebugViews, ChangeGame = GameScenes.ChangeDebugViews },
		new() { Name = "Volumetric Fog", Group = "Effects", Game = GameScenes.VolumetricFog, ChangeGame = GameScenes.ChangeVolumetricFog, NativeAlongside = true },
		new() { Name = "Contact Shadows", Group = "Shadows", Game = GameScenes.ContactShadows, ChangeGame = GameScenes.ChangeContactShadows },
		new() { Name = "Decal Geometry", Group = "Effects", Game = GameScenes.DecalGeometry, ChangeGame = GameScenes.ChangeDecalGeometry },
		new() { Name = "Soft Particles", Group = "Effects", Game = GameScenes.SoftParticles },
		new() { Name = "Scene Attributes", Group = "GameObject", Game = GameScenes.SceneAttributes, ChangeGame = GameScenes.ChangeSceneAttributes },
		new() { Name = "Scene Object Children", Group = "GameObject", Game = GameScenes.SceneObjectChildren, ChangeGame = GameScenes.ChangeSceneObjectChildren },
		new() { Name = "Morphs", Group = "GameObject", Game = GameScenes.Morphs, ChangeGame = GameScenes.ChangeMorphs },
		new() { Name = "Model Deformers", Group = "GameObject", Game = GameScenes.ModelDeformers, ChangeGame = GameScenes.ChangeModelDeformers },
		new() { Name = "Ambient Occlusion", Group = "Post Processing", Game = GameScenes.AmbientOcclusion, ChangeGame = GameScenes.ChangeOcclusionStage, Isolated = true },
		new() { Name = "Screen Space Reflections", Group = "Post Processing", Game = GameScenes.ScreenSpaceReflections, ChangeGame = GameScenes.ChangeOcclusionStage, Isolated = true },
		new() { Name = "Bone Merged Deformers", Group = "GameObject", Game = GameScenes.BoneMergedDeformers, ChangeGame = GameScenes.ChangeBoneMergedDeformers, Frame = GameScenes.AnimateBoneMergedDeformers },
		new() { Name = "All Outfits", Group = "Avatar", Game = OutfitScenes.AllOutfits, Frame = OutfitScenes.DressAll, Ready = OutfitScenes.AllDressed },
		new() { Name = "Hotdog Costume", Group = "Avatar", Game = OutfitScenes.HotdogCostume.Build, Frame = OutfitScenes.HotdogCostume.DressWhenLoaded, Ready = OutfitScenes.HotdogCostume.Dressed },
		new() { Name = "Banana Suit", Group = "Avatar", Game = OutfitScenes.BananaSuit.Build, Frame = OutfitScenes.BananaSuit.DressWhenLoaded, Ready = OutfitScenes.BananaSuit.Dressed },
		new() { Name = "Katana", Group = "Avatar", Game = OutfitScenes.Katana.Build, Frame = OutfitScenes.Katana.DressWhenLoaded, Ready = OutfitScenes.Katana.Dressed },
		new() { Name = "Golf Backpack", Group = "Avatar", Game = OutfitScenes.GolfBackpack.Build, Frame = OutfitScenes.GolfBackpack.DressWhenLoaded, Ready = OutfitScenes.GolfBackpack.Dressed },
		new() { Name = "Pearl Necklace", Group = "Avatar", Game = OutfitScenes.PearlNecklace.Build, Frame = OutfitScenes.PearlNecklace.DressWhenLoaded, Ready = OutfitScenes.PearlNecklace.Dressed },
		new() { Name = "Fried Onion Earrings", Group = "Avatar", Game = OutfitScenes.FriedOnionEarrings.Build, Frame = OutfitScenes.FriedOnionEarrings.DressWhenLoaded, Ready = OutfitScenes.FriedOnionEarrings.Dressed },
		new() { Name = "Light Cookie", Group = "GameObject", Game = GameScenes.LightCookie, ChangeGame = GameScenes.ChangeLightCookie },
		new() { Name = "Debug Overlay", Group = "GameObject", Game = GameScenes.DebugOverlay, ChangeGame = GameScenes.ChangeDebugOverlay, Frame = GameScenes.DrawDebugOverlay },
		new() { Name = "Terrain and Clutter", Group = "GameObject", Game = TerrainScenes.TerrainAndClutter, ChangeGame = TerrainScenes.ChangeTerrainAndClutter, Ready = TerrainScenes.ClutterReady },
		new() { Name = "Terrain Ambient Occlusion", Group = "GameObject", Game = TerrainScenes.TerrainAmbientOcclusion, ChangeGame = TerrainScenes.ChangeTerrainAndClutter, Ready = TerrainScenes.ClutterReady, Isolated = true },
		new() { Name = "Refraction", Group = "Effects", Game = GameScenes.Refraction, ChangeGame = GameScenes.ChangeRefraction },
		new() { Name = "Viewmodel", Group = "Effects", Game = GameScenes.Viewmodel, ChangeGame = GameScenes.ChangeViewmodel },
		new() { Name = "Viewmodel Depth", Group = "Effects", Game = GameScenes.ViewmodelDepth, ChangeGame = GameScenes.ChangeViewmodel },
		new() { Name = "Bloom Objects", Group = "Post Processing", Game = GameScenes.BloomObjects, ChangeGame = GameScenes.ChangeBloomObjects },
		new() { Name = "Dynamic", Group = "Stress", Game = DynamicScene.Build, Frame = DynamicScene.Animate },
		new() { Name = "Heavy Scene", Group = "Stress", Game = HeavyScene.Build, Frame = HeavyScene.Animate },
		new() { Name = "Heavy Scene AO", Group = "Stress", Game = HeavyScene.BuildWithOcclusion, Frame = HeavyScene.Animate, Isolated = true },
		new() { Name = "Flatgrass", Group = "Maps", Game = MapScenes.Flatgrass, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Flatgrass Skyline", Group = "Maps", Game = MapScenes.FlatgrassSkyline, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Flatgrass Interior", Group = "Maps", Game = MapScenes.FlatgrassInterior, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Flatgrass Props", Group = "Maps", Game = MapScenes.FlatgrassProps, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Construct Skyline", Group = "Maps", Game = MapScenes.ConstructSkyline, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Construct Skyline Fog", Group = "Maps", Game = MapScenes.ConstructSkylineFog, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded, NativeAlongside = true },
	];

	/// <summary>
	/// The scene described by the command line, or null if it doesn't describe one.
	/// </summary>
	public static SceneLabScene FromCommandLine()
	{
		string[] switches = ["-model", "-grid", "-lights", "-lightradius", "-spot", "-sun", "-lightshadows", "-floor"];
		if ( !switches.Any( Args.Has ) ) return null;

		var scene = new SceneLabScene
		{
			Name = "Command Line",
			Models = [Args.String( "-model" ) ?? "models/dev/box.vmdl"],
			Grid = Math.Max( 1, Args.Int( "-grid", 1 ) ),
			Lights = Math.Max( 0, Args.Int( "-lights", 0 ) ),
			LightRadius = Args.Float( "-lightradius", 0.4f ),
			Spot = Args.Has( "-spot" ),
			Sun = Args.Float( "-sun", 0.1f ),
			LightShadows = Args.Has( "-lightshadows" ),
			Floor = Args.Has( "-floor" ),
		};

		return scene;
	}
}

/// <summary>
/// The command line.
/// </summary>
internal static class Args
{
	public static string String( string name )
	{
		var args = Environment.GetCommandLineArgs();
		var index = Array.FindIndex( args, x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
	}

	public static bool Has( string name ) => Environment.GetCommandLineArgs().Any( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );

	public static int Int( string name, int fallback ) => int.TryParse( String( name ), out var value ) ? value : fallback;

	public static float Float( string name, float fallback ) => float.TryParse( String( name ), System.Globalization.CultureInfo.InvariantCulture, out var value ) ? value : fallback;
}
