namespace Sandbox.SceneLab;

/// <summary>
/// A Scene Lab scene: a GameObject scene built from engine components and assets, rendered by native's scene system.
/// </summary>
internal sealed record SceneLabScene
{
	public string Name { get; init; }

	/// <summary>
	/// The Scene menu's submenu it's in.
	/// </summary>
	public string Group { get; init; } = "Basics";

	/// <summary>
	/// Builds the GameObject scene (<see cref="GameScenes"/>).
	/// </summary>
	public Func<Scene> Game { get; init; }

	/// <summary>
	/// Changes the scene through its components, for a second capture - proving the change arrives.
	/// </summary>
	public Action<Scene> ChangeGame { get; init; }

	/// <summary>
	/// Check rendered behavior after a capture. Return an error to fail the capture.
	/// </summary>
	public Func<Scene, string> ValidateGame { get; init; }

	/// <summary>
	/// A world the capture adds to the camera's extra worlds (<c>SceneCamera.Worlds</c>), which a camera component clears every
	/// frame: the capture renders a <c>SceneCamera</c> set up from the scene's camera, with it added.
	/// </summary>
	public Func<Scene, SceneWorld> ExtraWorld { get; init; }

	/// <summary>
	/// Runs every frame on a <see cref="Game"/> scene, after it ticks and before it renders - for drawing that's redone
	/// each frame, like a HUD.
	/// </summary>
	public Action<Scene> Frame { get; init; }

	/// <summary>
	/// Runs every frame on a <see cref="Game"/> scene before its cameras render, with the renderer to use: for cube captures a
	/// preset renders itself (an envmap probe's).
	/// </summary>
	public Action<Scene, Rendering.ISceneRenderer> Capture { get; init; }

	/// <summary>
	/// A cube an isolated capture reads back too (<c>name.face0.native.png</c> to <c>face5</c>).
	/// </summary>
	public Func<Scene, Texture> CaptureFaces { get; init; }

	/// <summary>
	/// Float textures an isolated capture reads back too: a volume's, or a single-channel cube's faces (a DDGI probe's depth).
	/// </summary>
	public Func<Scene, Texture[]> CaptureVolumes { get; init; }

	/// <summary>
	/// The view setup a <see cref="Game"/> scene's render target camera renders with, rather than none: for presets that
	/// show what a <see cref="Rendering.ViewSetup"/> does.
	/// </summary>
	public Func<CameraComponent, Rendering.ViewSetup> TargetSetup { get; init; }

	/// <summary>
	/// Whether a <see cref="Game"/> scene has finished loading - a map downloading, say. Captures count their frames from
	/// when it's true.
	/// </summary>
	public Func<Scene, bool> Ready { get; init; }

	/// <summary>
	/// Capture from a fresh scene load, the camera rendered once on a capture frame: effects such as AO and SSR keep history
	/// on the camera, which a second render in the frame would advance.
	/// </summary>
	public bool Isolated { get; init; }

	/// <summary>
	/// The benchmark measures it with occlusion culling off and then on.
	/// </summary>
	public bool CompareOcclusion { get; init; }

	/// <summary>
	/// Puts a scripted <see cref="Game"/> scene back to its first frame. The benchmark calls it as it starts measuring, so
	/// every run measures the same frames whatever the warm-up took.
	/// </summary>
	public Action<Scene> Rewind { get; init; }

	/// <summary>
	/// Frames the benchmark measures, when not the default (<c>-benchmark-frames</c> overrides it): a script's whole length.
	/// </summary>
	public int? BenchmarkFrames { get; init; }

	/// <summary>
	/// The frame a capture saves, when not the default (<c>-capture-frame</c> overrides it): one where the script has got
	/// going.
	/// </summary>
	public int? CaptureFrame { get; init; }

	/// <summary>
	/// The Scene menu's presets, in order.
	/// </summary>
	public static readonly SceneLabScene[] Presets =
	[
		new() { Name = "Better Fog Distance", Group = "Environment", Game = GameScenes.BetterFogDistance, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Height", Group = "Environment", Game = GameScenes.BetterFogHeight, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Height Only", Group = "Environment", Game = GameScenes.BetterFogHeightOnly, ChangeGame = GameScenes.ReverseBetterFogHeight },
		new() { Name = "Better Fog Sky", Group = "Environment", Game = GameScenes.BetterFogSky, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Shared Sky", Group = "Environment", Game = GameScenes.BetterFogSharedSky, ChangeGame = GameScenes.ChangeBetterFogHeightSky },
		new() { Name = "Better Fog Height Skybox", Group = "Environment", Game = GameScenes.BetterFogHeightSkybox, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Height Sky", Group = "Environment", Game = GameScenes.BetterFogHeightSky, ChangeGame = GameScenes.RotateBetterFogSky },
		new() { Name = "Better Fog Rotated Sky", Group = "Environment", Game = GameScenes.BetterFogRotatedSky, ChangeGame = GameScenes.RotateBetterFogSky },
		new() { Name = "Better Fog Skybox World", Group = "Environment", Game = GameScenes.BetterFogSkyboxWorld, ChangeGame = GameScenes.RotateBetterFogSkyboxWorld },
		new() { Name = "Better Fog Sky Selection", Group = "Environment", Game = GameScenes.BetterFogSkySelection, ChangeGame = GameScenes.ExcludeBetterFogLastSky },
		new() { Name = "Better Fog Blend", Group = "Environment", Game = GameScenes.BetterFogBlend, ChangeGame = GameScenes.ClearBetterFogBlend },
		new() { Name = "Better Fog Orthographic", Group = "Environment", Game = GameScenes.BetterFogOrthographic, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog 3D Skybox", Group = "Environment", Game = GameScenes.BetterFog3DSkybox, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Overlay Off", Group = "Environment", Game = GameScenes.BetterFogOverlayOff, ChangeGame = GameScenes.RemoveBetterFogOverlay },
		new() { Name = "Better Fog Overlay On", Group = "Environment", Game = GameScenes.BetterFogOverlayOn, ChangeGame = GameScenes.RemoveBetterFogOverlay },
		new() { Name = "Better Fog Additive Sprites", Group = "Environment", Game = GameScenes.BetterFogAdditiveSprites, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Lines", Group = "Environment", Game = GameScenes.BetterFogLines, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Water", Group = "Environment", Game = GameScenes.BetterFogWater, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Better Fog Text", Group = "Environment", Game = GameScenes.BetterFogText, ChangeGame = GameScenes.DisableBetterFog },
		new() { Name = "Post Processing", Group = "Post Processing", Game = GameScenes.PostProcessing, ChangeGame = GameScenes.ChangePostProcessing },
		new() { Name = "Depth of Field", Group = "Post Processing", Game = GameScenes.DepthOfField },
		new() { Name = "Auto Exposure", Group = "Post Processing", Game = GameScenes.AutoExposure },
		new() { Name = "Blur 0.25", Group = "Post Processing", Game = GameScenes.Blur( 0.25f ) },
		new() { Name = "Blur 0.5", Group = "Post Processing", Game = GameScenes.Blur( 0.5f ) },
		new() { Name = "Blur 0.75", Group = "Post Processing", Game = GameScenes.Blur( 0.75f ) },
		new() { Name = "Highlight Outlines", Group = "Post Processing", Game = GameScenes.HighlightOutlines, ChangeGame = GameScenes.ChangeHighlightOutlines },
		new() { Name = "Screen UI", Group = "UI", Game = GameScenes.ScreenUI, ChangeGame = GameScenes.ChangeScreenUI, Frame = GameScenes.DrawHud },
		new() { Name = "Early UI", Group = "UI", Game = GameScenes.EarlyUI },
		new() { Name = "Stage Attributes", Group = "UI", Game = StageAttributesScene.Build, ChangeGame = StageAttributesScene.Change },
		new() { Name = "World Panel", Group = "UI", Game = GameScenes.WorldUI },
		new() { Name = "Output Overlays", Group = "UI", Game = GameScenes.OutputOverlays, ChangeGame = GameScenes.ChangeOutputOverlays },
		new() { Name = "Sprites and Particles", Group = "Effects", Game = GameScenes.SpritesAndParticles, ChangeGame = GameScenes.ChangeSpritesAndParticles },
		new() { Name = "Decals", Group = "Effects", Game = GameScenes.Decals, ChangeGame = GameScenes.ChangeDecals },
		new() { Name = "EnvMaps and Decals", Group = "Stress", Game = GameScenes.EnvMapsAndDecals },
		new() { Name = "Tools View", Group = "Tools", Game = GameScenes.ToolsView, ChangeGame = GameScenes.ChangeToolsView },
		new() { Name = "Debug Views", Group = "Tools", Game = GameScenes.DebugViews, ChangeGame = GameScenes.ChangeDebugViews },
		new() { Name = "Volumetric Fog", Group = "Effects", Game = GameScenes.VolumetricFog, ChangeGame = GameScenes.ChangeVolumetricFog },
		new() { Name = "Dense Smoke", Group = "Effects", Game = GameScenes.DenseSmoke },
		new() { Name = "Contact Shadows", Group = "Shadows", Game = GameScenes.ContactShadows, ChangeGame = GameScenes.ChangeContactShadows },
		new() { Name = "Decal Geometry", Group = "Effects", Game = GameScenes.DecalGeometry, ChangeGame = GameScenes.ChangeDecalGeometry },
		new() { Name = "Soft Particles", Group = "Effects", Game = GameScenes.SoftParticles },
		new() { Name = "Particle Refraction", Group = "Effects", Game = GameScenes.ParticleRefraction, ChangeGame = GameScenes.ChangeParticleRefraction },
		new() { Name = "Scene Attributes", Group = "GameObject", Game = GameScenes.SceneAttributes, ChangeGame = GameScenes.ChangeSceneAttributes },
		new() { Name = "Scene Object Children", Group = "GameObject", Game = GameScenes.SceneObjectChildren, ChangeGame = GameScenes.ChangeSceneObjectChildren },
		new() { Name = "Morphs", Group = "GameObject", Game = GameScenes.Morphs, ChangeGame = GameScenes.ChangeMorphs },
		new() { Name = "Model Deformers", Group = "GameObject", Game = GameScenes.ModelDeformers, ChangeGame = GameScenes.ChangeModelDeformers },
		new() { Name = "Ambient Occlusion", Group = "Post Processing", Game = GameScenes.AmbientOcclusion, ChangeGame = GameScenes.ChangeOcclusionStage, Isolated = true },
		new() { Name = "Screen Space Reflections", Group = "Post Processing", Game = GameScenes.ScreenSpaceReflections, ChangeGame = GameScenes.ChangeOcclusionStage, Isolated = true },
		new() { Name = "AO and Reflections", Group = "Post Processing", Game = GameScenes.OcclusionAndReflections, ChangeGame = GameScenes.ChangeOcclusionStage, Isolated = true },
		new() { Name = "Bone Merged Deformers", Group = "GameObject", Game = GameScenes.BoneMergedDeformers, ChangeGame = GameScenes.ChangeBoneMergedDeformers, Frame = GameScenes.AnimateBoneMergedDeformers },
		new() { Name = "All Outfits", Group = "Avatar", Game = OutfitScenes.AllOutfits, Frame = OutfitScenes.DressAll, Ready = OutfitScenes.AllDressed },
		new() { Name = "Hotdog Costume", Group = "Avatar", Game = OutfitScenes.HotdogCostume.Build, Frame = OutfitScenes.HotdogCostume.DressWhenLoaded, Ready = OutfitScenes.HotdogCostume.Dressed },
		new() { Name = "Banana Suit", Group = "Avatar", Game = OutfitScenes.BananaSuit.Build, Frame = OutfitScenes.BananaSuit.DressWhenLoaded, Ready = OutfitScenes.BananaSuit.Dressed },
		new() { Name = "Katana", Group = "Avatar", Game = OutfitScenes.Katana.Build, Frame = OutfitScenes.Katana.DressWhenLoaded, Ready = OutfitScenes.Katana.Dressed },
		new() { Name = "Golf Backpack", Group = "Avatar", Game = OutfitScenes.GolfBackpack.Build, Frame = OutfitScenes.GolfBackpack.DressWhenLoaded, Ready = OutfitScenes.GolfBackpack.Dressed },
		new() { Name = "Pearl Necklace", Group = "Avatar", Game = OutfitScenes.PearlNecklace.Build, Frame = OutfitScenes.PearlNecklace.DressWhenLoaded, Ready = OutfitScenes.PearlNecklace.Dressed },
		new() { Name = "Fried Onion Earrings", Group = "Avatar", Game = OutfitScenes.FriedOnionEarrings.Build, Frame = OutfitScenes.FriedOnionEarrings.DressWhenLoaded, Ready = OutfitScenes.FriedOnionEarrings.Dressed },
		new() { Name = "Light Cookie", Group = "GameObject", Game = GameScenes.LightCookie, ChangeGame = GameScenes.ChangeLightCookie },
		new() { Name = "Light Modulation", Group = "GameObject", Game = GameScenes.LightModulation, ChangeGame = GameScenes.ChangeLightModulation },
		new() { Name = "Light Switching", Group = "GameObject", Game = GameScenes.LightSwitching, Frame = GameScenes.SwitchLights },
		new() { Name = "Light Modulation Baked Sun", Group = "Effects", Game = () => LightModulatorScenes.Sun( 0 ), ValidateGame = LightModulatorScenes.ValidateSun },
		new() { Name = "Light Modulation Dynamic Sun", Group = "Effects", Game = () => LightModulatorScenes.Sun( -1 ), ValidateGame = LightModulatorScenes.ValidateSun },
		new() { Name = "Light Modulation Gallery", Group = "Effects", Game = LightModulatorScenes.Gallery, ChangeGame = LightModulatorScenes.ChangeGallery, Isolated = true },
		new() { Name = "Debug Overlay", Group = "GameObject", Game = GameScenes.DebugOverlay, ChangeGame = GameScenes.ChangeDebugOverlay, Frame = GameScenes.DrawDebugOverlay },
		new() { Name = "Extra Worlds", Group = "GameObject", Game = ExtraWorldsScene.Build, ChangeGame = ExtraWorldsScene.Change, ExtraWorld = ExtraWorldsScene.ExtraWorld, ValidateGame = ExtraWorldsScene.Validate },
		new() { Name = "Terrain and Clutter", Group = "GameObject", Game = TerrainScenes.TerrainAndClutter, ChangeGame = TerrainScenes.ChangeTerrainAndClutter, Ready = TerrainScenes.ClutterReady },
		new() { Name = "Instanced Models", Group = "GameObject", Game = InstancedScene.Build, ChangeGame = InstancedScene.Change },
		new() { Name = "Object Command Lists", Group = "GameObject", Game = ObjectCommandListScene.Build, ChangeGame = ObjectCommandListScene.Change, Isolated = true },
		new() { Name = "Water Refraction", Group = "Effects", Game = WaterScene.Build, Isolated = true },
		new() { Name = "Water Planar", Group = "Effects", Game = WaterScene.Planar, Isolated = true },
		new() { Name = "Water Planar Shadows", Group = "Effects", Game = WaterScene.PlanarShadows, Isolated = true },
		new() { Name = "Envmap Capture", Group = "Effects", Game = EnvmapCaptureScene.Build, Capture = EnvmapCaptureScene.Capture, CaptureFaces = EnvmapCaptureScene.Cube, Isolated = true },
		new() { Name = "DDGI Bake", Group = "Effects", Game = DDGIBakeScene.Build, Capture = DDGIBakeScene.Capture, CaptureFaces = DDGIBakeScene.ColorCapture, CaptureVolumes = DDGIBakeScene.Readbacks, Isolated = true },
		new() { Name = "Mirror Reflection", Group = "Effects", Game = MirrorScene.Build, Isolated = true },
		new() { Name = "Mirror Reflection Shadows", Group = "Effects", Game = MirrorScene.BuildWithShadows, Isolated = true },
		new() { Name = "View Projections", Group = "GameObject", Game = ViewSetupScenes.Projections, TargetSetup = ViewSetupScenes.ProjectionSetup, Isolated = true },
		new() { Name = "View Oblique", Group = "GameObject", Game = ViewSetupScenes.Oblique, TargetSetup = ViewSetupScenes.ObliqueSetup, Isolated = true },
		new() { Name = "View Overrides", Group = "GameObject", Game = ViewSetupScenes.Overrides, TargetSetup = ViewSetupScenes.OverrideSetup, Isolated = true },
		new() { Name = "Terrain Ambient Occlusion", Group = "GameObject", Game = TerrainScenes.TerrainAmbientOcclusion, ChangeGame = TerrainScenes.ChangeTerrainAndClutter, Ready = TerrainScenes.ClutterReady, Isolated = true },
		new() { Name = "Refraction", Group = "Effects", Game = GameScenes.Refraction, ChangeGame = GameScenes.ChangeRefraction },
		new() { Name = "Viewmodel", Group = "Effects", Game = GameScenes.Viewmodel, ChangeGame = GameScenes.ChangeViewmodel },
		new() { Name = "Viewmodel Depth", Group = "Effects", Game = GameScenes.ViewmodelDepth, ChangeGame = GameScenes.ChangeViewmodel },
		new() { Name = "Bloom Objects", Group = "Post Processing", Game = GameScenes.BloomObjects, ChangeGame = GameScenes.ChangeBloomObjects },
		new() { Name = "Dynamic", Group = "Stress", Game = DynamicScene.Build, Frame = DynamicScene.Animate },
		new() { Name = "Heavy Scene", Group = "Stress", Game = HeavyScene.Build, Frame = HeavyScene.Animate },
		new() { Name = "Heavy Scene AO", Group = "Stress", Game = HeavyScene.BuildWithOcclusion, Frame = HeavyScene.Animate, Isolated = true },
		new() { Name = "Heavy Scene AO SSR", Group = "Stress", Game = HeavyScene.BuildWithReflectionsAndOcclusion, Frame = HeavyScene.Animate, Isolated = true },
		new() { Name = "Occlusion City", Group = "Stress", Game = OcclusionScene.BuildCity, Frame = OcclusionScene.Drive, CompareOcclusion = true },
		new() { Name = "Flatgrass", Group = "Maps", Game = MapScenes.Flatgrass, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Flatgrass Skyline", Group = "Maps", Game = MapScenes.FlatgrassSkyline, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Flatgrass Interior", Group = "Maps", Game = MapScenes.FlatgrassInterior, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Flatgrass Props", Group = "Maps", Game = MapScenes.FlatgrassProps, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Construct Skyline", Group = "Maps", Game = MapScenes.ConstructSkyline, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Construct Overview", Group = "Maps", Game = MapScenes.ConstructOverview, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },
		new() { Name = "Construct Skyline Fog", Group = "Maps", Game = MapScenes.ConstructSkylineFog, Frame = MapScenes.StartWhenOnline, Ready = MapScenes.Loaded },

		// Scenarios: game-like scenes measured as a whole, scripted by frame count so each run sees the same frames (Scenarios)
		new() { Name = "Firefight", Group = Scenarios.Group, Game = FirefightScene.Build, Frame = FirefightScene.Fight, Ready = FirefightScene.Ready, Rewind = FirefightScene.Rewind,
			CaptureFrame = 240 },
		new() { Name = "Flythrough", Group = Scenarios.Group, Game = FlythroughScene.Build, Frame = FlythroughScene.Fly, Ready = FlythroughScene.Ready, Rewind = FlythroughScene.Rewind,
			BenchmarkFrames = FlythroughScene.Frames, CaptureFrame = 200 },
		new() { Name = "Crowd Near", Group = Scenarios.Group, Game = CrowdScene.Near, Frame = CrowdScene.Animate, Rewind = CrowdScene.Rewind },
		new() { Name = "Crowd Mid", Group = Scenarios.Group, Game = CrowdScene.Mid, Frame = CrowdScene.Animate, Rewind = CrowdScene.Rewind },
		new() { Name = "Crowd Far", Group = Scenarios.Group, Game = CrowdScene.Far, Frame = CrowdScene.Animate, Rewind = CrowdScene.Rewind },
		new() { Name = "Forest", Group = Scenarios.Group, Game = ForestScene.Build, Frame = ForestScene.Walk, Ready = ForestScene.Ready, Rewind = ForestScene.Rewind,
			BenchmarkFrames = ForestScene.LoopFrames, CaptureFrame = 200 },
		new() { Name = "Night Interior", Group = Scenarios.Group, Game = NightInteriorScene.Build, Frame = NightInteriorScene.Walk, Rewind = NightInteriorScene.Rewind,
			BenchmarkFrames = NightInteriorScene.Frames, CaptureFrame = 150 },
		new() { Name = "Smoke and Glass", Group = Scenarios.Group, Game = SmokeAndGlassScene.Build, Frame = SmokeAndGlassScene.Animate, Rewind = SmokeAndGlassScene.Rewind,
			CaptureFrame = 300 },
	];
}
