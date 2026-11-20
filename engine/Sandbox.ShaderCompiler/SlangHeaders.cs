using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// The engine's old <c>.fxc</c> headers that our own Slang headers under <c>core/shaders/sbox</c> replace. A Slang compile
/// includes ours wherever a file includes the old one, so the shared headers that .shader files also include
/// (<c>common/*.hlsl</c> and the rest) stay as they are for vfx, and nothing a Slang compile reads is a header a player's
/// install doesn't ship. Our headers keep the old names and results. SboxBuild's check-shader-includes follows the same table.
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
