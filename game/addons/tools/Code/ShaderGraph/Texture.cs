using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public enum TextureExtension
{
	Color,
	Normal,
	Rough,
	AO,
	Metal,
	Trans,
	SelfIllum,
	Mask,
}

public enum TextureProcessor
{
	None,
	Mod2XCenter,
	NormalizeNormals,
	FillToPowerOfTwo,
	FillToMultipleOfFour,
	ScaleToPowerOfTwo,
	HeightToNormal,
	Inverse,
	ConvertToYCoCg,
	DilateColorInTransparentPixels,
	EncodeRGBM,
}

public enum TextureColorSpace
{
	Srgb,
	Linear,
}

public enum TextureFormat
{
	DXT5,
	DXT1,
	RGBA8888,
	BC7,
}

public enum TextureType
{
	Tex2D,
	TexCube,
}

public struct UIGroup
{
	/// <summary>
	/// Name of this group
	/// </summary>
	[Editor( "shadergraphgroup" )]
	public string Name { get; set; }

	/// <summary>
	/// Priority of this group
	/// </summary>
	public int Priority { get; set; }
}

public struct TextureInput
{
	/// <summary>
	/// Name that shows up in material editor
	/// </summary>
	[Hide, JsonIgnore]
	public string Name { get; set; }

	/// <summary>
	/// If true, this parameter can be modified with <see cref="RenderAttributes"/>.
	/// </summary>
	public bool IsAttribute { get; set; }

	/// <summary>
	/// Default color that shows up in material editor when using color control
	/// </summary>
	[Title( "Default Color" )]
	public Color Default { get; set; }

	/// <summary>
	/// Default texture that shows up in material editor
	/// </summary>
	[ImageAssetPath]
	public string DefaultTexture { get; set; }

	/// <summary>
	/// Default texture that shows up in material editor (_color, _normal, _rough, etc..)
	/// </summary>
	[ShowIf( nameof( ShowExtension ), true )]
	public TextureExtension Extension { get; set; }

	/// <summary>
	/// Default texture that shows up in material editor (_color, _normal, _rough, etc..)
	/// </summary>
	public string CustomExtension { get; set; }

	[JsonIgnore, Hide]
	public bool ShowGroups { get; set; } = true;

	[Hide]
	public readonly bool ShowExtension => string.IsNullOrWhiteSpace( CustomExtension );

	[JsonIgnore, Hide]
	public string ExtensionString
	{
		get
		{
			if ( !string.IsNullOrWhiteSpace( CustomExtension ) )
			{
				var ext = CustomExtension.Trim();
				if ( ext.StartsWith( "_" ) )
					ext = ext[1..];

				if ( !string.IsNullOrWhiteSpace( ext ) )
					return ext;
			}

			return Extension.ToString();
		}
	}

	/// <summary>
	/// Processor used when compiling this texture
	/// </summary>
	public TextureProcessor Processor { get; set; }

	/// <summary>
	/// Color space used when compiling this texture
	/// </summary>
	public TextureColorSpace ColorSpace { get; set; }

	/// <summary>
	/// Format used when compiling this texture
	/// </summary>
	public TextureFormat ImageFormat { get; set; }

	/// <summary>
	/// Sample this texture as srgb
	/// </summary>
	public bool SrgbRead { get; set; }

	/// <summary>
	/// Priority of this value in the group
	/// </summary>
	public int Priority { get; set; }

	/// <summary>
	/// Primary group
	/// </summary>
	[InlineEditor( Label = false ), Group( "Group" )]
	[ShowIf( nameof( ShowGroups ), true )]
	public UIGroup PrimaryGroup { get; set; }

	/// <summary>
	/// Group within the primary group
	/// </summary>
	[InlineEditor( Label = false ), Group( "Sub Group" )]
	[ShowIf( nameof( ShowGroups ), true )]
	public UIGroup SecondaryGroup { get; set; }

	[JsonIgnore, Hide]
	public readonly string UIGroup => $"{PrimaryGroup.Name},{PrimaryGroup.Priority}/{SecondaryGroup.Name},{SecondaryGroup.Priority}/{Priority}";

	[JsonIgnore, Hide]
	public TextureType Type { get; set; }

	public readonly string CreateTexture( string name )
	{
		if ( Type == TextureType.Tex2D ) return $"Texture2D g_t{name}";
		if ( Type == TextureType.TexCube ) return $"TextureCube g_t{name}";
		return default;
	}

	[JsonIgnore, Hide]
	public readonly string CreateInput
	{
		get
		{
			if ( Type == TextureType.Tex2D ) return "CreateInputTexture2D";
			if ( Type == TextureType.TexCube ) return "CreateInputTextureCube";
			return default;
		}
	}

	public TextureInput()
	{
	}
}
