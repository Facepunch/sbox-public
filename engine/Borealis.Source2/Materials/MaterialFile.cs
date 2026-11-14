using Borealis.Source2.Resources;
using System.IO;

namespace Borealis.Source2.Materials;

/// <summary>
/// A compiled material (<c>.vmat_c</c>): its shader and parameters, from the KV3 in its <c>DATA</c> block
/// (<c>CMaterialLayer</c>'s <c>m_intParams</c>, <c>m_floatParams</c>, <c>m_vectorParams</c>, <c>m_textureParams</c>
/// and attributes). Features (<c>F_*</c>) are int parameters; textures are compiled <c>.vtex</c> paths.
/// </summary>
public sealed class MaterialFile
{
	/// <summary>The material's own path.</summary>
	public string Name { get; init; }

	/// <summary>The shader, <c>shaders/complex.shader</c>.</summary>
	public string Shader { get; init; }

	/// <summary>Int parameters: features and int/bool constants.</summary>
	public IReadOnlyDictionary<string, int> Ints { get; init; }

	/// <summary>Float parameters.</summary>
	public IReadOnlyDictionary<string, float> Floats { get; init; }

	/// <summary>Vector parameters (four components).</summary>
	public IReadOnlyDictionary<string, float[]> Vectors { get; init; }

	/// <summary>Texture parameters: compiled texture paths by shader variable (<c>g_tColor</c>).</summary>
	public IReadOnlyDictionary<string, string> Textures { get; init; }

	/// <summary>Int attributes (<c>RepresentativeTextureWidth</c>...).</summary>
	public IReadOnlyDictionary<string, int> IntAttributes { get; init; }

	/// <summary>
	/// The shader version it was compiled for (<c>m_shaderVersion</c>), which shaders read as <c>Source( ShaderVersion )</c>:
	/// blendable's version 1 packs normal and roughness together, 0 is the older layout.
	/// </summary>
	public int ShaderVersion { get; init; }

	/// <summary>Read a material from its <c>.vmat_c</c> bytes.</summary>
	public static MaterialFile Read( byte[] data )
	{
		var resource = ResourceFile.Read( data );
		if ( Kv3.Read( resource.Block( "DATA" ) ) is not Dictionary<string, object> root ) throw new InvalidDataException( "Material DATA isn't an object" );

		return new MaterialFile
		{
			Name = root.Get( "m_materialName" ) as string,
			Shader = root.Get( "m_shaderName" ) as string,
			Ints = Pairs( root, "m_intParams", "m_nValue", v => (int)Number( v ) ),
			Floats = Pairs( root, "m_floatParams", "m_flValue", v => (float)Number( v ) ),
			Vectors = Pairs( root, "m_vectorParams", "m_value", Vector ),
			Textures = Pairs( root, "m_textureParams", "m_pValue", v => v as string ),
			IntAttributes = Pairs( root, "m_intAttributes", "m_nValue", v => (int)Number( v ) ),
			ShaderVersion = (int)Number( root.Get( "m_shaderVersion" ) ),
		};
	}

	/// <summary>Read a <c>.vmat_c</c> from disk.</summary>
	public static MaterialFile Load( string path ) => Read( File.ReadAllBytes( path ) );

	/// <summary>
	/// The material's value for a shader variable, as floats: a float, vector or int parameter by name; null if it
	/// doesn't set one.
	/// </summary>
	public float[] Value( string name )
	{
		if ( Floats.TryGetValue( name, out var f ) ) return [f];
		if ( Vectors.TryGetValue( name, out var v ) ) return v;
		if ( Ints.TryGetValue( name, out var i ) ) return [i];
		return null;
	}

	static Dictionary<string, T> Pairs<T>( Dictionary<string, object> root, string list, string valueKey, Func<object, T> convert )
	{
		var result = new Dictionary<string, T>( StringComparer.OrdinalIgnoreCase );
		if ( root.Get( list ) is not object[] entries ) return result;
		foreach ( var entry in entries )
		{
			if ( entry is not Dictionary<string, object> pair || pair.Get( "m_name" ) is not string name ) continue;
			result[name] = convert( pair.Get( valueKey ) );
		}

		return result;
	}

	static double Number( object value ) => value switch
	{
		int i => i,
		uint u => u,
		long l => l,
		ulong ul => ul,
		float f => f,
		double d => d,
		bool b => b ? 1 : 0,
		_ => 0,
	};

	static float[] Vector( object value )
	{
		var result = new float[4];
		if ( value is object[] items )
		{
			for ( int i = 0; i < Math.Min( items.Length, 4 ); i++ ) result[i] = (float)Number( items[i] );
		}

		return result;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
