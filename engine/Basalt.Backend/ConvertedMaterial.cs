using Basalt.Source2.Materials;
using Sandbox.ShaderCompiler;
using System.IO;

namespace Basalt.Backend;

/// <summary>
/// A <c>.vmat</c> material of a <c>.shader</c> that a surface stands in for (<see cref="SurfaceShaders.Convert"/>): the
/// surface's record filled from the material's values, field by field. A field takes the value of its <c>[Source]</c>
/// (a feature, a variable or <c>ShaderVersion</c>), else of its name with vfx's prefixes; textures load from the
/// material's texture of that name.
/// </summary>
internal sealed class ConvertedMaterial : SurfaceMaterial
{
	static readonly string[] Prefixes = ["g_t", "g_fl", "g_v", "g_n", "g_b", ""];

	ConvertedMaterial( string shader, Sandbox.ShaderCompiler.MaterialLayout layout ) : base( shader, layout.Size, MaterialClassWriter.Hash( layout ) )
	{
	}

	public static ConvertedMaterial Create( CompiledShader compiled, MaterialFile file, string name, Func<string, GpuTexture> loadTexture )
	{
		var material = new ConvertedMaterial( Path.GetFileName( compiled.Path ), compiled.Material ) { DisplayName = name };
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
		if ( field.Source is { } source ) return file.Value( source );
		foreach ( var prefix in Prefixes )
		{
			if ( file.Value( prefix + field.Name ) is { } value ) return value;
		}

		return null;
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
