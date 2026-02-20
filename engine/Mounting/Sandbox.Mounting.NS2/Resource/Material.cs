using Sandbox;
using System;

class MaterialLoader( string fullPath ) : ResourceLoader<GameMount>
{
	private string FullPath { get; init; } = fullPath;
	private static readonly Material ModelMaterial = Material.Create( "model", "model" );
	private static readonly Material EmissiveMaterial = Material.Create( "emissive", "emissive" );

	static MaterialLoader()
	{
		var normalMap = Texture.Create( 1, 1 ).WithData( new byte[4] { 128, 128, 255, 255 } ).Finish();

		ModelMaterial.Set( "g_tAlbedoMap", Texture.White );
		ModelMaterial.Set( "g_tOpacityMap", Texture.White );
		ModelMaterial.Set( "g_tSpecularMap", Texture.Black );
		ModelMaterial.Set( "g_tEmissiveMap", Texture.Black );
		ModelMaterial.Set( "g_tNormalMap", normalMap );

		EmissiveMaterial.Set( "g_tEmissiveMap", Texture.Black );
	}

	protected override object Load()
	{
		var strings = new Dictionary<string, string>();
		var numbers = new Dictionary<string, float>();
		var shader = string.Empty;

		var lines = File.ReadAllLines( FullPath );
		foreach ( var line in lines )
		{
			var trimmed = line.Trim();
			if ( string.IsNullOrWhiteSpace( trimmed ) )
				continue;

			var eqIndex = trimmed.IndexOf( '=' );
			if ( eqIndex < 0 )
				continue;

			var key = trimmed[..eqIndex].Trim();
			var value = trimmed[(eqIndex + 1)..].Trim();

			if ( string.IsNullOrWhiteSpace( key ) || string.IsNullOrWhiteSpace( value ) )
				continue;

			if ( value.StartsWith( '\"' ) && value.EndsWith( '\"' ) )
			{
				value = value[1..^1].Trim();

				if ( key == "shader" )
				{
					shader = value;
					continue;
				}

				strings[key] = value;
			}
			else if ( float.TryParse( value, out float num ) )
			{
				numbers[key] = num;
			}
		}

		var material = ModelMaterial;

		if ( shader == "shaders/Emissive.surface_shader" )
		{
			material = EmissiveMaterial;
		}

		material = material.CreateCopy( Path );

		foreach ( var (key, value) in strings )
		{
			if ( !value.EndsWith( ".dds", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var texture = Texture.Load( $"mount://ns2/ns2/{value}.vtex", false );
			if ( texture is null || texture.IsError )
				continue;

			material.Set( "g_t" + key, texture );
		}

		foreach ( var (key, value) in numbers )
		{
			material.Set( key, value );
		}

		return material;
	}
}
