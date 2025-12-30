using System.IO;
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
	// Editor only
	//
	[Category( "Source Images" ), ImageAssetPath] public string AlbedoImage { get; set; } = "materials/default/default_color.tga";
	[Category( "Source Images" ), ImageAssetPath] public string RoughnessImage { get; set; } = "materials/default/default_rough.tga";
	[Category( "Source Images" ), ImageAssetPath] public string NormalImage { get; set; } = "materials/default/default_normal.tga";
	[Category( "Source Images" ), ImageAssetPath] public string HeightImage { get; set; } = "materials/default/default_ao.tga";
	[Category( "Source Images" ), ImageAssetPath, Title( "AO Image" )] public string AOImage { get; set; } = "materials/default/default_ao.tga";

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
	public bool HasHeightTexture => !string.IsNullOrEmpty( HeightImage ) && HeightImage != "materials/default/default_ao.tga";

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
