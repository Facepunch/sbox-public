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
	// Source images, editor only. Grouped with the values that act on them. Roughness, height and AO are
	// optional: clear them (right-click > Clear) and the compiler fills the channel with white.
	//

	/// <summary>
	/// What an optional image falls back to when it's left empty.
	/// </summary>
	internal const string UnboundImage = "materials/default/default_ao.tga";

	[Category( "Color" ), Order( 1 ), ImageAssetPath, Title( "Image" )] public string AlbedoImage { get; set; } = "materials/default/default_color.tga";
	[Category( "Color" ), Order( 1 ), Range( 0.0f, 1.0f )] public float Metalness { get; set; } = 0.0f;

	[Category( "Normal" ), Order( 2 ), ImageAssetPath, Title( "Image" )] public string NormalImage { get; set; } = "materials/default/default_normal.tga";
	[Category( "Normal" ), Order( 2 ), Range( 0.1f, 10 ), Title( "Strength" )] public float NormalStrength { get; set; } = 1.0f;

	[Category( "Roughness" ), Order( 3 ), ImageAssetPath, Title( "Image" ), Description( "Optional. Without one the value below is the roughness." )]
	public string RoughnessImage { get; set; } = "materials/default/default_rough.tga";

	[Category( "Roughness" ), Order( 3 ), Range( 0.0f, 1.0f ), Title( "Value" ), Description( "Multiplies the roughness image. Without an image this is the roughness." )]
	public float Roughness { get; set; } = 1.0f;

	[Category( "Height" ), Order( 4 ), ImageAssetPath, Title( "Image" ), Description( "Optional. Drives height blending between materials and displacement." )]
	public string HeightImage { get; set; } = "";

	[Category( "Height" ), Order( 4 ), Range( 0.1f, 10 ), Title( "Blend Strength" ), ShowIf( nameof( HasHeightTexture ), true )]
	public float HeightBlendStrength { get; set; } = 1.0f;

	[Category( "Height" ), Order( 4 ), Range( 0.0f, 10.0f ), Title( "Displacement Scale" ), ShowIf( nameof( HasHeightTexture ), true )]
	public float DisplacementScale { get; set; } = 0.0f;

	[Category( "Ambient Occlusion" ), Order( 5 ), ImageAssetPath, Title( "Image" ), Description( "Optional." )]
	public string AOImage { get; set; } = "";

	//
	// Compiled generated textures
	//
	[JsonIgnore, Hide] public Texture BCRTexture { get; private set; }
	[JsonIgnore, Hide] public Texture NHOTexture { get; private set; }

	[Category( "Tiling" ), Order( 6 ), Title( "UV Scale" )] public float UVScale { get; set; } = 1.0f;

	[JsonIgnore, Hide]
	public bool HasHeightTexture => !string.IsNullOrEmpty( HeightImage ) && HeightImage != UnboundImage;

	[Category( "Tiling" ), Order( 6 ), Title( "No Tiling" )]
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

	[Category( "Misc" ), Order( 7 )] public Surface Surface { get; set; }

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
