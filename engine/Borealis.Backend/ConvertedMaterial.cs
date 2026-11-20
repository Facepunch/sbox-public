using Borealis.Source2.Materials;
using Sandbox.ShaderCompiler;
using System.IO;

namespace Borealis.Backend;

/// <summary>
/// A <c>.vmat</c> material of a <c>.shader</c> that a surface stands in for (<see cref="SurfaceShaders.Convert"/>): the
/// surface's record filled from the material's values, field by field. A field takes the value of its <c>[Source]</c>
/// (a feature, a variable or <c>ShaderVersion</c>), else of its name with vfx's prefixes; textures load from the
/// material's texture of that name. Its render state is the surface's, changed by the material's values of the surface's
/// <c>[RenderState]</c> fields (<c>F_TRANSLUCENT</c> blending, <c>F_ALPHA_TEST</c> alpha-testing), as any material's.
/// </summary>
internal sealed class ConvertedMaterial : SurfaceMaterial
{
	static readonly string[] Prefixes = ["g_t", "g_fl", "g_f", "g_v", "g_n", "g_b", ""];

	ConvertedMaterial( string shader, Sandbox.ShaderCompiler.MaterialLayout layout ) : base( shader, layout.Size, MaterialClassWriter.Hash( layout ), layout.States() )
	{
		this.layout = layout;
	}

	readonly Sandbox.ShaderCompiler.MaterialLayout layout;

	/// <summary>
	/// Set the fields a shader variable fills (<c>g_vColorTint</c>: a field whose <c>[Source]</c> it is, or named for it with
	/// vfx's prefixes), as code setting it on the material does. False when the surface has no such field.
	/// </summary>
	public bool Set( string variable, ReadOnlySpan<float> value )
	{
		var found = false;
		foreach ( var field in layout.Fields )
		{
			if ( field.Kind == MaterialFieldKind.Texture2D || !Fills( field, variable ) ) continue;
			found = true;
			for ( int i = 0; i < field.Components && i < value.Length; i++ )
			{
				if ( field.Kind == MaterialFieldKind.Float ) SetFloat( field.Offset + i * 4, value[i] );
				else SetInt( field.Offset + i * 4, field.Kind == MaterialFieldKind.Bool ? (value[i] != 0 ? 1 : 0) : (int)MathF.Round( value[i] ) );
			}
		}

		return found;
	}

	/// <summary>Set the texture fields a shader variable (<c>g_tColor</c>) fills, as <see cref="Set(string, ReadOnlySpan{float})"/> does values.</summary>
	public bool Set( string variable, GpuTexture texture )
	{
		var found = false;
		foreach ( var field in layout.Fields )
		{
			if ( field.Kind != MaterialFieldKind.Texture2D || !Fills( field, variable ) ) continue;
			found = true;
			SetTexture( field.Offset, texture, field.Srgb );
		}

		return found;
	}

	static bool Fills( MaterialField field, string variable )
	{
		if ( field.Source is { } source ) return string.Equals( source, variable, StringComparison.OrdinalIgnoreCase );
		foreach ( var prefix in Prefixes )
		{
			if ( variable.Length == prefix.Length + field.Name.Length && variable.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) && variable.EndsWith( field.Name, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	/// <summary>The compiled material it was made from: the textures it names by shader variable, read or not.</summary>
	public MaterialFile File { get; private init; }

	/// <summary>
	/// Whether the surface takes this material: every feature it reads (a field whose <c>[Source]</c> is an <c>F_</c>
	/// feature) is in the field's range. A surface declares the features it doesn't do as fields with <c>Range( 0, 0 )</c>,
	/// so materials that set them stay on their <c>.shader</c>.
	/// </summary>
	public static bool Accepts( CompiledShader compiled, MaterialFile file )
	{
		var blends = false;
		foreach ( var field in compiled.Material.Fields )
		{
			if ( field.Source is not { } source || !source.StartsWith( "F_", StringComparison.Ordinal ) ) continue;
			var value = file.Value( source ) is { Length: > 0 } v ? v[0] : 0;
			if ( (field.Min is { } min && value < min) || (field.Max is { } max && value > max) ) return false;
			blends |= source is "F_TRANSLUCENT" or "F_ADDITIVE_BLEND";
		}

		// A translucent material converts only to a surface that reads its translucency features
		return blends || !Blended( file );
	}

	/// <summary>Whether a material blends (<c>F_TRANSLUCENT</c> or <c>F_ADDITIVE_BLEND</c>), as BackendMaterial.IsTranslucent reads it.</summary>
	static bool Blended( MaterialFile file ) => file.Value( "F_TRANSLUCENT" ) is [> 0, ..] || file.Value( "F_ADDITIVE_BLEND" ) is [> 0, ..];

	public static ConvertedMaterial Create( CompiledShader compiled, MaterialFile file, string name, Func<string, GpuTexture> loadTexture )
	{
		var material = new ConvertedMaterial( compiled.Path, compiled.Material ) { DisplayName = name, File = file };

		foreach ( var field in compiled.Material.Fields )
		{
			if ( field.Kind == MaterialFieldKind.Texture2D )
			{
				if ( Lookup( field, file.Textures ) is { } texture && loadTexture( texture ) is { } loaded ) material.SetTexture( field.Offset, loaded, field.Srgb );
				continue;
			}

			var value = field.Source == "ShaderVersion" ? new float[] { file.ShaderVersion } : Value( field, file ) ?? field.Default;
			if ( value is null ) continue;
			for ( int i = 0; i < field.Components && i < value.Length; i++ )
			{
				if ( field.Kind == MaterialFieldKind.Float ) material.SetFloat( field.Offset + i * 4, value[i] );
				else material.SetInt( field.Offset + i * 4, field.Kind == MaterialFieldKind.Bool ? (value[i] != 0 ? 1 : 0) : (int)MathF.Round( value[i] ) );
			}
		}

		return material;
	}

	static float[] Value( MaterialField field, MaterialFile file )
	{
		if ( field.ShaderNames is { } names ) return ShaderIndex( names, file.Shader ) is var index and >= 0 ? [index] : null;
		if ( field.Source is { } source ) return file.Value( source );
		foreach ( var prefix in Prefixes )
		{
			if ( file.Value( prefix + field.Name ) is { } value ) return value;
		}

		return null;
	}

	/// <summary>Where a material's <c>.shader</c> (<c>shaders/generic.shader</c>) is in a <c>[Source( "Shader: ..." )]</c> list; -1 when it isn't.</summary>
	static int ShaderIndex( IReadOnlyList<string> names, string shader )
	{
		if ( shader is null ) return -1;
		var name = Path.GetFileNameWithoutExtension( shader.Replace( '\\', '/' ) );
		for ( int i = 0; i < names.Count; i++ )
		{
			if ( string.Equals( names[i], name, StringComparison.OrdinalIgnoreCase ) ) return i;
		}

		return -1;
	}

	static string Lookup( MaterialField field, IReadOnlyDictionary<string, string> textures )
	{
		if ( field.Source is { } source ) return textures.TryGetValue( source, out var named ) ? named : null;
		foreach ( var prefix in Prefixes )
		{
			if ( textures.TryGetValue( prefix + field.Name, out var texture ) ) return texture;
		}

		return null;
	}
}
