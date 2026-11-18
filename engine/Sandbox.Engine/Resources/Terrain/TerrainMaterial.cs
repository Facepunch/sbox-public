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

	[Category( "Color" ), Order( 1 ), ImageAssetPath, Title( "Image" ), Description( "The surface color, without lighting or shadows baked in." )]
	public string AlbedoImage { get; set; } = "materials/default/default_color.tga";

	[Category( "Color" ), Order( 1 ), Range( 0.0f, 1.0f ), Description( "0 for ground, rock and grass. 1 is bare metal: shiny reflections tinted by the color. Values in between rarely look real." )]
	public float Metalness { get; set; } = 0.0f;

	[Category( "Normal" ), Order( 2 ), ImageAssetPath, Title( "Image" ), Description( "Small bumps and cracks that catch the light, without changing the shape." )]
	public string NormalImage { get; set; } = "materials/default/default_normal.tga";

	[Category( "Normal" ), Order( 2 ), Range( 0.1f, 10 ), Title( "Strength" ), Description( "How strongly the normal image bends the light. 1 is the image as made." )]
	public float NormalStrength { get; set; } = 1.0f;

	[Category( "Roughness" ), Order( 3 ), ImageAssetPath, Title( "Image" ), Description( "Optional. Where the surface is matte or glossy. Without one, the value below is used everywhere." )]
	public string RoughnessImage { get; set; } = "materials/default/default_rough.tga";

	[Category( "Roughness" ), Order( 3 ), Range( 0.0f, 1.0f ), Title( "Value" ), Description( "0 is glossy, like wet ground or ice, with sharp reflections. 1 is fully matte, like dry dirt. Multiplies the image when there is one." )]
	public float Roughness { get; set; } = 1.0f;

	[Category( "Height" ), Order( 4 ), ImageAssetPath, Title( "Image" ), Description( "Optional. White is high. Lets this material's high parts, like rocks, show through its neighbours where they blend." )]
	public string HeightImage { get; set; } = "";

	[Category( "Height" ), Order( 4 ), Range( 0.1f, 10 ), Title( "Blend Strength" ), ShowIf( nameof( HasHeightTexture ), true ), Description( "How much the height image wins when blending with other materials. Higher pokes through more." )]
	public float HeightBlendStrength { get; set; } = 1.0f;

	[Category( "Height" ), Order( 4 ), Range( 0.0f, 10.0f ), Title( "Displacement Scale" ), ShowIf( nameof( HasHeightTexture ), true ), Description( "How far the height image pushes the surface out. 0 keeps it flat." )]
	public float DisplacementScale { get; set; } = 0.0f;

	[Category( "Ambient Occlusion" ), Order( 5 ), ImageAssetPath, Title( "Image" ), Description( "Optional. Darkens cracks and gaps that light can't reach. White is no darkening." )]
	public string AOImage { get; set; } = "";

	//
	// Compiled generated textures
	//
	[JsonIgnore, Hide] public Texture BCRTexture { get; private set; }
	[JsonIgnore, Hide] public Texture NHOTexture { get; private set; }

	[Category( "Tiling" ), Order( 6 ), Title( "UV Scale" ), Description( "How big one repeat of the images is. Higher makes the texture larger and repeat less." )]
	public float UVScale { get; set; } = 1.0f;

	[JsonIgnore, Hide]
	public bool HasHeightTexture => !string.IsNullOrEmpty( HeightImage ) && HeightImage != UnboundImage;

	[Category( "Tiling" ), Order( 6 ), Title( "No Tiling" ), Description( "Randomly rotates and shifts each repeat so the pattern doesn't show across large areas." )]
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

	[Category( "Misc" ), Order( 7 ), Description( "What the ground is made of for footsteps, impacts and physics." )]
	public Surface Surface { get; set; }

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
