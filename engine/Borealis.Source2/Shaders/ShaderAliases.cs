namespace Borealis.Source2.Shaders;

/// <summary>
/// Shader names that stand for another shader: complex, simple and generic are standard. Their materials compile from
/// standard's module, open in the material editor with standard's inputs and draw with its surface; the engine's own
/// materials name standard itself, and these keep older and package materials working. Every lookup of a shader's source
/// by name goes through here: the material compiler, the material editor, the surface drawing a material, the compiled
/// shaders' leftover rule and materialsystem2 (handed <see cref="Names"/> as the engine starts). Names, not files: an
/// alias holds whatever the shader's file is called.
/// </summary>
public static class ShaderAliases
{
	/// <summary>A shader's name (no folder or extension) to the name of the shader it stands for.</summary>
	public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
	{
		["complex"] = "standard",
		["simple"] = "standard",
		["generic"] = "standard",
	};

	/// <summary>
	/// The shader <paramref name="shader"/> stands for, written the same way (<c>shaders/complex.shader</c> to
	/// <c>shaders/standard.shader</c>, <c>complex</c> to <c>standard</c>, <c>complex.vfx</c> to <c>standard.vfx</c>), or
	/// <paramref name="shader"/> itself when it's no alias. Only a bare name or one in a <c>shaders</c> folder is one.
	/// </summary>
	public static string Resolve( string shader )
	{
		if ( string.IsNullOrEmpty( shader ) ) return shader;

		var slash = shader.LastIndexOfAny( ['/', '\\'] );
		var folder = shader[..(slash + 1)];
		if ( folder.Length > 0 && !folder.TrimEnd( '/', '\\' ).Replace( '\\', '/' ).Split( '/' )[^1].Equals( "shaders", StringComparison.OrdinalIgnoreCase ) ) return shader;

		var file = shader[(slash + 1)..];
		var dot = file.IndexOf( '.' );
		var name = dot < 0 ? file : file[..dot];
		return Names.TryGetValue( name, out var standsFor ) ? folder + standsFor + file[name.Length..] : shader;
	}
}
