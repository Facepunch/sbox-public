/// <summary>
/// How rope-like constraints are drawn - shared by the rope and elastic tools so they offer the same materials and
/// look the same.
/// </summary>
public static class RopeLook
{
	public const string RopeMaterial = "materials/default/rope01.vmat";
	public const string CableMaterial = "hydraulics/metal_tile_line.vmat";
	public const string ThickBeamMaterial = "entities/trails/line_thickbeam.vmat";
	public const string LavaBeamMaterial = "entities/trails/line_electricbeam.vmat";
	public const string CrackleBeamMaterial = "weapons/Physgun/physgun_beam.vmat";
	public const string PlasmaBeamMaterial = "weapons/Toolgun/Effects/beam-success.vmat";

	/// <summary>
	/// The recommended materials, in the order the material picker shows them.
	/// </summary>
	public static readonly string[] Presets = [RopeMaterial, CableMaterial, ThickBeamMaterial, LavaBeamMaterial, CrackleBeamMaterial, PlasmaBeamMaterial];

	/// <summary>
	/// Names for <see cref="Presets"/>, by index.
	/// </summary>
	public static readonly string[] Titles = ["#tool.name.rope", "#tool.setting.rope_material.cable", "#asset.thickbeam.title", "#asset.electricbeam.title", "#tool.setting.rope_material.crackle", "#tool.setting.rope_material.plasma"];

	/// <summary>
	/// Beam textures are drawn as light: added on top, no shading, no shadow.
	/// </summary>
	public static readonly string[] Glow = [ThickBeamMaterial, LavaBeamMaterial, CrackleBeamMaterial, PlasmaBeamMaterial];

	public static bool IsGlow( string path ) => Glow.Contains( path, StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Add the line that draws a rope from <paramref name="start"/> to <paramref name="end"/>, on the start object.
	/// <paramref name="materialPath"/> is a local path or a workshop package ident; a workshop material starts as plain
	/// rope and swaps in once each client has downloaded it.
	/// </summary>
	public static LineRenderer AddLine( GameObject start, GameObject end, string materialPath, float radius, Color tint, int splineInterpolation )
	{
		materialPath = string.IsNullOrWhiteSpace( materialPath ) ? RopeMaterial : materialPath;
		var isPackage = LineMaterial.IsPackageIdent( materialPath );
		var glow = IsGlow( materialPath );
		var material = isPackage ? null : Material.Load( materialPath );

		var line = start.AddComponent<LineRenderer>();
		line.Points = [start, end];
		line.Width = radius;
		line.Color = tint;
		line.Lighting = !glow;
		line.CastShadows = !glow;
		line.Opaque = !glow;
		line.Additive = glow;
		line.SplineInterpolation = splineInterpolation;
		line.Face = SceneLineObject.FaceMode.Cylinder;
		line.Texturing = line.Texturing with { Material = material ?? Material.Load( RopeMaterial ), WorldSpace = true, UnitsPerTexture = 32 };

		// each client fetches it and makes a line material of its own
		if ( isPackage )
			start.AddComponent<LineMaterial>().Ident = materialPath;

		return line;
	}
}
