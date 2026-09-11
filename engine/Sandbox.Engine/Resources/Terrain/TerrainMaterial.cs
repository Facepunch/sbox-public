using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

[Flags]
public enum TerrainFlags : uint
{
	None = 0,
	NoTile = 1 << 0
}

/// <summary>
/// Description of a Terrain Material.
/// </summary>
[AssetType( Name = "Terrain Material", Extension = "tmat", Category = "World", Flags = AssetTypeFlags.NoEmbedding )]
public class TerrainMaterial : GameResource
{
	//
	// Source images used when a slot is left empty
	//
	internal const string DefaultAlbedoImage = "materials/default/default_color.tga";
	internal const string DefaultRoughnessImage = "materials/default/default_rough.tga";
	internal const string DefaultNormalImage = "materials/default/default_normal.tga";
	internal const string DefaultHeightImage = "materials/default/default_ao.tga";
	internal const string DefaultAOImage = "materials/default/default_ao.tga";

	//
	// Editor only. These get packed into the two generated textures below when the
	// material compiles, which means they have to resolve to an image file - the
	// packing runs through the texture compiler, not through the generator.
	//
	[Category( "Source Images" )] public Texture AlbedoImage { get; set; }
	[Category( "Source Images" )] public Texture RoughnessImage { get; set; }
	[Category( "Source Images" )] public Texture NormalImage { get; set; }
	[Category( "Source Images" )] public Texture HeightImage { get; set; }
	[Category( "Source Images" ), Title( "AO Image" )] public Texture AOImage { get; set; }

	//
	// Compiled generated textures
	//
	[JsonIgnore, Hide] public Texture BCRTexture { get; private set; }
	[JsonIgnore, Hide] public Texture NHOTexture { get; private set; }

	[Category( "Material" ), Title( "UV Scale" )] public float UVScale { get; set; } = 1.0f;
	[Category( "Material" ), Range( 0.0f, 1.0f )] public float Metalness { get; set; } = 0.0f;
	[Category( "Material" ), Range( 0.1f, 10 )] public float NormalStrength { get; set; } = 1.0f;
	[Category( "Material" ), Range( 0.1f, 10 )] public float HeightBlendStrength { get; set; } = 1.0f;

	[JsonIgnore, Hide]
	public bool HasHeightTexture => HeightImage is not null;

	[Category( "Material" ), Range( 0.0f, 10.0f ), Title( "Displacement Scale" ), ShowIf( nameof( HasHeightTexture ), true )]
	public float DisplacementScale { get; set; } = 0.0f;

	[Category( "Material" ), Title( "No Tiling" )]
	public bool NoTiling { get; set; } = false;

	[JsonIgnore, Hide]
	public TerrainFlags Flags
	{
		get
		{
			var flags = TerrainFlags.None;

			if ( NoTiling )
				flags |= TerrainFlags.NoTile;

			return flags;
		}
	}

	[Category( "Misc" )] public Surface Surface { get; set; }

	[Hide, JsonIgnore] public override int ResourceVersion => 1;

	/// <summary>
	/// v1
	/// - Source images were bare image paths, they're textures now. An empty slot falls
	///   back to its default image, so drop the paths that were only ever the default.
	/// </summary>
	[Expose, JsonUpgrader( typeof( TerrainMaterial ), 1 )]
	static void Upgrader_v1_ImagePathsToTextures( JsonObject json )
	{
		Upgrade( json, nameof( AlbedoImage ), DefaultAlbedoImage );
		Upgrade( json, nameof( RoughnessImage ), DefaultRoughnessImage );
		Upgrade( json, nameof( NormalImage ), DefaultNormalImage );
		Upgrade( json, nameof( HeightImage ), DefaultHeightImage );
		Upgrade( json, nameof( AOImage ), DefaultAOImage );

		static void Upgrade( JsonObject json, string name, string defaultPath )
		{
			if ( !json.TryGetPropertyValue( name, out var node ) )
				return;

			// Only a bare path is the old format
			if ( node is not JsonValue value || !value.TryGetValue<string>( out var filePath ) )
				return;

			if ( string.IsNullOrWhiteSpace( filePath ) || filePath == defaultPath )
			{
				json[name] = null;
				return;
			}

			json[name] = new JsonObject
			{
				["$compiler"] = "texture",
				["$source"] = "imagefile",
				["data"] = new JsonObject
				{
					["FilePath"] = filePath,
					["MaxSize"] = 4096
				},
				["compiled"] = null
			};
		}
	}

	void LoadGeneratedTextures()
	{
		BCRTexture = Texture.Load( Path.Combine( Path.GetDirectoryName( ResourcePath ), $"{Path.GetFileNameWithoutExtension( ResourcePath )}_tmat_bcr.generated.vtex" ) );
		NHOTexture = Texture.Load( Path.Combine( Path.GetDirectoryName( ResourcePath ), $"{Path.GetFileNameWithoutExtension( ResourcePath )}_tmat_nho.generated.vtex" ) );
	}

	protected override void PostLoad()
	{
		base.PostLoad();
		LoadGeneratedTextures();
	}

	protected override void PostReload()
	{
		base.PostReload();
		LoadGeneratedTextures();
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "landscape", width, height );
	}
}
