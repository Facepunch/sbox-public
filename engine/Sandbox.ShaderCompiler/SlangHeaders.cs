using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// The engine's old <c>.fxc</c> headers that our own Slang headers under <c>core/shaders/sbox</c> replace. A Slang compile
/// includes ours wherever a file includes the old one, so nothing a Slang compile reads is a header a player's install
/// doesn't ship. Each old name is still a file that includes ours (a one-line forward), which is what vfx reads for a
/// <c>.shader</c>, a package's included. Our headers keep the old names and results. SboxBuild's check-shader-includes
/// follows the same table.
/// Changing it changes what compiles emit: raise <see cref="SlangCompiler.Version"/>.
/// </summary>
public static partial class SlangHeaders
{
	/// <summary>Old header, as includes name it, to ours.</summary>
	public static IReadOnlyDictionary<string, string> Replaced { get; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
	{
		["math_general.fxc"] = "sbox/math.slangh",
		["encoded_normals.fxc"] = "sbox/encoding.slangh",
		["octohedral_encoding.fxc"] = "sbox/encoding.slangh",
		["transform_buffer.fxc"] = "sbox/transform.slangh",
		["instancing.fxc"] = "sbox/instancing.slangh",
		["vs_decompress.fxc"] = "sbox/vertex_decompress.slangh",
		["morph.fxc"] = "sbox/morph.slangh",
		["ffd.fxc"] = "sbox/ffd.slangh",
		["baked_lighting_constants.fxc"] = "sbox/baked_lighting.slangh",
		["ambient_cube.fxc"] = "sbox/ambient_cube.slangh",
		["sky.fxc"] = "sbox/sky.slangh",
		["sheet_sampling.fxc"] = "sbox/sheet.slangh",
		["volumetric_fog.fxc"] = "sbox/volumetric_fog.slangh",
		["light_probe_volume.fxc"] = "sbox/light_probe_volume.slangh",
		["irradiance_probe_lighting.fxc"] = "sbox/irradiance_probe.slangh",
		["quad_overdraw_ps.fxc"] = "sbox/quad_overdraw.slangh",
		["vr_environment_map.fxc"] = "sbox/environment_map.slangh",
		["vr_detail_texture.fxc"] = "sbox/detail_texture.slangh",
		["vr_lighting.fxc"] = "sbox/lighting.slangh",
		["vr_common_ps_code.fxc"] = "sbox/final_combiner.slangh",
		["vr_common.fxc"] = "sbox/shading.slangh",
		["vr_common_features.fxc"] = "sbox/features.slangh",
		["vr_common_vs_input.fxc"] = "sbox/vertex_input.slangh",
		["vr_common_ps_input.fxc"] = "sbox/pixel_input.slangh",
		["vr_common_vs_code.fxc"] = "sbox/vertex_processing.slangh",
		["vr_shared_standard_vs_input.fxc"] = "sbox/standard_vertex_input.slangh",
		["vr_shared_standard_ps_input.fxc"] = "sbox/standard_pixel_input.slangh",
		["vr_shared_standard_vs_code.fxc"] = "sbox/standard_vertex_processing.slangh",
		["vr_shared_standard_features.fxc"] = "sbox/standard_features.slangh",
		["pcss.fxc"] = "sbox/pcss.slangh",
		["vr_shared_standard_ps_code.fxc"] = "sbox/standard_pixel_processing.slangh",
		["system.fxc"] = "sbox/system.slangh",
		["descriptor_set_support.fxc"] = "sbox/descriptor_sets.slangh",
		["common.fxc"] = "sbox/view.slangh",
		["common_samplers.fxc"] = "sbox/samplers.slangh",
		["vr_triplanar_blend.fxc"] = "sbox/triplanar_blend.slangh",
	};

	[GeneratedRegex( @"^([ \t]*#[ \t]*include[ \t]*"")([^""]+)("")", RegexOptions.Multiline )]
	private static partial Regex Include();

	/// <summary><paramref name="text"/> with each include of a replaced header naming ours; lines are kept.</summary>
	public static string Replace( string text )
	{
		if ( !text.Contains( "#", StringComparison.Ordinal ) ) return text;
		return Include().Replace( text, m => Replaced.TryGetValue( m.Groups[2].Value, out var ours ) ? m.Groups[1].Value + ours + m.Groups[3].Value : m.Value );
	}
}
