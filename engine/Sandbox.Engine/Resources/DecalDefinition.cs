using Sandbox.Rendering;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A decal which can be applied to objects and surfaces.
/// </summary>
[AssetType( Name = "Decal Definition", Extension = "decal", Category = "Effects" )]
public sealed class DecalDefinition : GameResource
{

	[Obsolete, Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )] public List<DecalEntry> Decals { get; set; } = [];

	[Obsolete]
	public class DecalEntry
	{
		[Obsolete, Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )] public Material Material { get; set; }
		[Obsolete, Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )] public RangedFloat Depth { get; set; }
		[Obsolete, Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )] public RangedFloat Rotation { get; set; }
		[Obsolete, Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )] public RangedFloat Width { get; set; }
		[Obsolete, Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )] public RangedFloat Height { get; set; }
	}

	/// <summary>
	/// The color map to use for the decal including transparency which masks the decal.
	/// This must be set for other textures to use the decal mask.
	/// </summary>
	[Header( "Textures" )]
	public Texture ColorTexture { get; set; }

	/// <summary>
	/// The normal texture map to use for the decal.
	/// </summary>
	public Texture NormalTexture { get; set; }

	/// <summary>
	/// The Roughness/Metal/Ambient Occlusion texture map to use for the decal, stored in the respective RGB channels.
	/// </summary>
	[Title( "Rough/Metal/Occlusion" )]
	public Texture RoughMetalOcclusionTexture { get; set; }

	/// <summary>
	/// The emissive texture map to use for the decal.
	/// </summary>
	public Texture EmissiveTexture { get; set; }

	/// <summary>
	/// Strength of the emission effect.
	/// </summary>
	public float EmissionEnergy { get; set; } = 1.0f;

	/// <summary>
	/// The height texture to use for parallax mapping.
	/// </summary>
	public Texture HeightTexture { get; set; }

	/// <summary>
	/// Strength of the parallax effect.
	/// </summary>
	[HideIf( "HeightTexture", null )]
	public float ParallaxStrength { get; set; } = 1.0f;

	/// <summary>
	/// Adjusts the opacity mask based on the height map.
	/// </summary>
	[HideIf( "HeightTexture", null )]
	public float CoverageAmount { get; set; } = 1.0f;

	/// <summary>
	/// Tints the color of the decal's albedo and can be used to adjust the overall opacity of the decal.
	/// </summary>
	[Header( "Parameters" )]
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// Controls the opacity of the decal's color texture without reducing the impact of the normal or rmo texture.
	/// Set to 0 to create a normal/rmo only decal masked by the color textures alpha.
	/// </summary>
	[Range( 0, 1 )]
	public float ColorMix { get; set; } = 1.0f;

	/// <summary>
	/// Width of the decal.
	/// </summary>
	public float Width { get; set; } = 16;

	/// <summary>
	/// Height of the decal.
	/// </summary>
	public float Height { get; set; } = 16;

	/// <summary>
	/// How the texture gets filtered.
	/// </summary>
	public FilterMode FilterMode { get; set; } = FilterMode.Anisotropic;

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "approval", width, height );
	}

}
