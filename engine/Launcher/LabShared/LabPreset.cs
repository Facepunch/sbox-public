namespace Sandbox.Lab;

/// <summary>
/// A lab preset: a grid of one model, and optionally a floor with lights over it, a probe, a sky or fog. SceneLab and
/// BorealisLab both build it into a <see cref="Borealis.RenderWorld"/> through <see cref="LabWorld"/>.
/// </summary>
internal record LabPreset
{
	public string Name { get; init; }

	/// <summary>
	/// The Scene menu's submenu it's in.
	/// </summary>
	public string Group { get; init; } = "Basics";

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
	/// How far the camera starts from the middle, if not far enough to see the whole grid.
	/// </summary>
	public float? CameraDistance { get; init; }

	public bool HasLocalLights => Lights > 0 || Spot;
}

/// <summary>
/// The lab presets: worlds built straight from a <see cref="LabPreset"/>, with no GameObject scene. SceneLab lists them
/// among its own presets and compares them with native; BorealisLab renders them alone and checks them against its
/// approved images.
/// </summary>
internal static class LabPresets
{
	/// <summary>
	/// Low and from the side, so shadows are long and in view.
	/// </summary>
	static readonly Vector3 SideSun = new Vector3( -0.4f, 0.7f, -0.55f ).Normal;

	/// <summary>
	/// Full-body citizen animations (a jump, a wave, a turn, fidgets), and two still poses - so a pose at another
	/// <c>-sequence-time</c> is a different pose.
	/// </summary>
	static readonly string[] CitizenSequences = ["AvatarMenu_Entry_Jump", "AvatarMenu_Entry_Wave_01", "AvatarMenu_Entry_TurnAround", "AvatarMenu_FidgetLarge_03", "AvatarMenu_ExamineLegs_02", "SitPose_Default", "CrouchIdlePose_Default"];

	public static readonly LabPreset[] All =
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
		new() { Name = "Legacy Combined Fog", Group = "Environment", Grid = 30, Floor = true, GradientFog = true, CubemapFog = true, SunDirection = SideSun },
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
		new() { Name = "Citizens", Group = "Skinning", Models = ["models/citizen_human/human.vmdl"], Grid = 3, Floor = true, Spot = true, LightShadows = true, Sun = 0.6f, SunDirection = SideSun,
			Sequences = CitizenSequences },

		// A model whose LODs are different shapes, so LOD choices can be seen and compared with native
		new() { Name = "LOD Test", Group = "Basics", Models = [LodTestModel.Path], Grid = 40 },

		// Big enough to measure - see SceneLab's -benchmark switch
		new() { Name = "10k Boxes", Group = "Stress", Grid = 100 },
		new() { Name = "50k Mixed", Group = "Stress", Models = ["models/dev/box.vmdl", "models/dev/sphere.vmdl"], Grid = 224 },
		new() { Name = "10k Boxes, 256 Lights", Group = "Stress", Grid = 100, Lights = 256, LightRadius = 0.05f },
		new() { Name = "10k Boxes, 64 Shadowed Lights", Group = "Stress", Grid = 100, Lights = 64, LightRadius = 0.05f, LightShadows = true },
		new() { Name = "10k Boxes, 64 Still Shadowed Lights", Group = "Stress", Grid = 100, Lights = 64, LightRadius = 0.05f, LightShadows = true, Orbit = false },
		new() { Name = "50k LOD Test", Group = "Stress", Models = [LodTestModel.Path], Grid = 224 },
		new() { Name = "100 Citizens", Group = "Stress", Models = ["models/citizen_human/human.vmdl"], Grid = 10, Floor = true, Sun = 0.6f, SunDirection = SideSun,
			Sequences = CitizenSequences },

		// A field wider than the sun's shadow distance with the camera inside it: every cascade full, the last one most
		new() { Name = "Sun Far Field", Group = "Stress", Models = ["models/dev/box.vmdl", "models/dev/sphere.vmdl"], Grid = 160, Floor = true, Sun = 1, SunDirection = SideSun,
			CameraDistance = 1500 },
	];

	/// <summary>
	/// The render tests' core suite: lab presets that cover the renderer's basics, checked against approved images. Append
	/// new presets; the order is the run order.
	/// </summary>
	public static readonly string[] Core =
	[
		"Box", "Spheres", "Point Lights", "Spot Light", "Sun Shadows", "Point Shadows", "All Shadows",
		"Reflections", "Gradient Fog", "Sky", "Translucency", "Faded Shadows",
		"Cubemap Fog", "Legacy Combined Fog", "Image Based Ambient", "Spot Shadow", "Static Spot Shadow",
		"Point Lights and Spot", "256 Lights", "256 Overlapping Lights", "LOD Test", "Box Grid",
	];

	/// <summary>
	/// The preset called <paramref name="name"/>, ignoring case, or null.
	/// </summary>
	public static LabPreset Find( string name ) => All.FirstOrDefault( x => x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
}
