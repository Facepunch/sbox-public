using NativeEngine;
using System.IO;

namespace Sandbox;

/// <summary>
/// A material. Uses several <see cref="Texture"/>s and a <see cref="Shader"/> with specific settings for more interesting visual effects.
/// </summary>
public sealed partial class Material : Resource
{
	/// <summary>
	/// Create a new empty material at runtime.
	/// </summary>
	/// <param name="materialName">Name of the new material.</param>
	/// <param name="shader">Shader that the new material will use.</param>
	/// <param name="anonymous">If false, material can be found by name.</param>
	/// <returns>The new material.</returns>
	public static Material Create( string materialName, string shader, bool anonymous = true )
	{
		// MaterialSystem2.CreateRawMaterial will also assert in native, but let's catch this in managed too.
		ThreadSafe.AssertIsMainThread();
		return FromNative( MaterialSystem2.CreateRawMaterial( materialName, shader, anonymous ) );
	}

	static Dictionary<string, Material> shaderMaterials = new Dictionary<string, Material>( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Get an empty material based on the specified shader. This will cache the material so that subsequent calls
	/// will return the same material.
	/// </summary>
	public static Material FromShader( Shader shader )
	{
		if ( shader == null )
			return null;

		var shaderPath = shader.ResourcePath.NormalizeFilename( false ).Replace( "/", "_" );
		if ( shaderMaterials.TryGetValue( shaderPath, out var material ) )
			return material;

		var materialName = $"__shader_{shaderPath}.vmat";

		material = Create( materialName, shader.ResourcePath );
		shaderMaterials[shaderPath] = material;
		return material;
	}

	/// <summary>
	/// Get an empty material based on the specified shader. This will cache the material so that subsequent calls
	/// will return the same material.
	/// </summary>
	public static Material FromShader( string path )
	{
		var shaderDir = Path.GetDirectoryName( path );
		var shaderName = Path.GetFileNameWithoutExtension( path ).ToLower();
		var shaderPath = Path.Combine( shaderDir, shaderName ).NormalizeFilename( false ).Replace( "/", "_" );
		if ( shaderMaterials.TryGetValue( shaderPath, out var material ) )
			return material;

		var materialName = $"__shader_{shaderPath}.vmat";

		material = Create( materialName, path );
		shaderMaterials[shaderPath] = material;
		return material;
	}

}
