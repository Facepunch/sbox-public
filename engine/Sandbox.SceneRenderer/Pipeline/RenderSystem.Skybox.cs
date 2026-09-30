namespace Sandbox.SceneRenderer;

/// <summary>
/// Renders the 3D skybox through a child system after <c>AfterOpaque</c> (<c>CRenderingPipelineStandard::Add3DSkyboxLayers</c>).
/// Shares the parent recorder and output; the skybox supplies the 2D sky.
/// </summary>
public sealed partial class RenderSystem
{
	/// <summary>
	/// Upper bound of skybox depth, starting at zero (<c>DEPTH_RANGE_3D_SKYBOX</c>).
	/// </summary>
	internal const float SkyboxMaxZ = MainWorldMinZ;

	static readonly uint SkyboxTag = new StringToken( "skybox" ).Value;
	static readonly StringToken SkyboxCombo = new( "D_SKYBOX" );

	/// <summary>
	/// Enable the world's 3D skybox. Defaults to true, matching <c>r_3d_skybox</c>.
	/// </summary>
	public bool Draw3DSkybox { get; set; } = true;

	/// <summary>
	/// Lazily created skybox system and view.
	/// </summary>
	RenderSystem skybox;
	RenderView skyboxView;

	/// <summary>
	/// Whether this system draws a 3D skybox for another, into the skybox's depth range.
	/// </summary>
	bool isSkybox;

	// The depth range this system's world draws into, within the viewport's
	float depthMin = MainWorldMinZ;
	float depthMax = 1;

	/// <summary>
	/// Viewport depth range for this world.
	/// </summary>
	internal float DepthMin => depthMin;
	internal float DepthMax => depthMax;

	/// <summary>
	/// Whether this system draws a 3D skybox for another.
	/// </summary>
	internal bool IsSkyboxSystem => isSkybox;

	/// <summary>
	/// Last skybox statistics, or null if no skybox rendered.
	/// </summary>
	internal RenderStats? SkyboxStats => lastDrewSkybox ? skybox.Stats : null;
	bool lastDrewSkybox;

	/// <summary>
	/// Skybox system for tests; null until first use.
	/// </summary>
	internal RenderSystem SkyboxSystem => skybox;

	/// <summary>
	/// Apply native's <c>skybox</c> tag filter (<c>Add2DSkyboxLayers</c>, <c>Add3DSkyboxLayers</c>).
	/// </summary>
	internal static bool ShowsSkies( RenderView view )
	{
		if ( view.RenderTags.Length > 0 && Array.IndexOf( view.RenderTags, SkyboxTag ) < 0 ) return false;
		return Array.IndexOf( view.ExcludeTags, SkyboxTag ) < 0;
	}

	/// <summary>
	/// Collect and prepare the skybox using the main view transformed into skybox space.
	/// </summary>
	void PrepareSkybox( RenderWorld world, RenderView view )
	{
		frame.Skybox = null;
		lastDrewSkybox = false;
		var sky = world.Skybox3D;
		if ( isSkybox || !Draw3DSkybox || sky is null || sky.Scale <= 0 || !ShowsSkies( view ) ) return;

		skybox ??= new RenderSystem
		{
			isSkybox = true,
			depthMin = 0,
			depthMax = SkyboxMaxZ,

			// Baked lighting needs no shadows (C3DSkyboxRenderer::Render); foliage still needs a prepass.
			Shadows = false,
			DepthPrepass = true,
		};
		skybox.ParallelRecording = ParallelRecording;
		skybox.DrawSky = DrawSky;

		skyboxView ??= new RenderView { Name = "3D Skybox" };
		FillSkyboxView( skyboxView, view, sky );
		skyboxView.Update();

		var stats = new RenderStats { Objects = sky.World.Count };
		var skyFrame = skybox.frame;
		skyFrame.Begin( sky.World, skyboxView, frame.Output, null );
		skybox.Collect( sky.World, skyboxView, ref stats );
		skybox.Prepare( sky.World, skyboxView );
		stats.Lights = skybox.lightBinner.Count;
		skybox.Stats = stats;

		// Inherit main-world ambient and scaled fog.
		skyFrame.Space = new ViewEnvironment.ViewSpace { Lighting = world.Lighting, Scale = sky.Scale, OffsetZ = sky.Origin.z, Fog = fog, FogView = view, Origin = sky.Origin };
		frame.Skybox = skyFrame;
		lastDrewSkybox = true;
	}

	/// <summary>
	/// Copy the main view at origin + position / scale (<c>CCameraRenderer::CreateView</c>, <c>UpdateFrustumFromViewSetup</c>).
	/// Disable size culling and leave tags unset, matching native's template.
	/// </summary>
	internal static void FillSkyboxView( RenderView into, RenderView main, Skybox3D sky )
	{
		into.Position = sky.Origin + main.Position / sky.Scale;
		into.Rotation = main.Rotation;
		into.FieldOfView = main.FieldOfView;
		into.Orthographic = main.Orthographic;
		into.ToolsVisMode = main.ToolsVisMode;
		into.OrthoSize = main.OrthoSize;
		into.ZNear = main.ZNear;
		into.ZFar = main.ZFar;
		into.Viewport = main.Viewport;
		into.ClearColor = main.ClearColor;
		into.ToneMapScalar = main.ToneMapScalar;
		into.SizeCullThreshold = 0;
	}
}
