using System;
using System.Globalization;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VmtFile
{
	private static readonly string[] ShaderSuffixes =
		["_dx9_hdr", "_dx90", "_dx95", "_dx9", "_dx80", "_dx81", "_dx8", "_dx70", "_dx7", "_dx60", "_dx6"];

	internal static string NormalizeShaderName( string shader )
	{
		shader = shader.ToLowerInvariant();
		foreach ( var suffix in ShaderSuffixes )
			if ( shader.EndsWith( suffix, StringComparison.Ordinal ) ) return shader[..^suffix.Length];
		return shader;
	}

	internal static IReadOnlyDictionary<string, string> SelectParameters( VmtFile material )
	{
		Dictionary<string, string> values = null;
		foreach ( var block in material.Root.Children )
		{
			if ( !block.IsBlock ) continue;
			var name = block.Name.AsSpan();
			var dx = name.IndexOf( "DX", StringComparison.OrdinalIgnoreCase );
			if ( dx < 0 || !int.TryParse( name[(dx + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out var level ) )
				continue;
			var selected = name[..dx] switch
			{
				">=" => 90 >= level,
				">" => 90 > level,
				"<=" => 90 <= level,
				"<" => 90 < level,
				"" or "==" => 90 == level,
				"!=" => 90 != level,
				_ => false
			};
			if ( selected )
			{
				values ??= new Dictionary<string, string>( material.Parameters, StringComparer.OrdinalIgnoreCase );
				foreach ( var pair in block.Values ) values[pair.Key] = pair.Value;
			}
		}
		return values ?? material.Parameters;
	}
}
