/// <summary>
/// Categories for browsing post-processing effects.
/// </summary>
public enum PostProcessGroup
{
	/// <summary>
	/// Effects entries.
	/// </summary>
	[Title( "#effects.group.effects" )]
	Effects,

	/// <summary>
	/// Overlay entries.
	/// </summary>
	[Title( "#effects.group.overlay" )]
	Overlay,

	/// <summary>
	/// Shaders entries.
	/// </summary>
	[Title( "#effects.group.shaders" )]
	Shaders,

	/// <summary>
	/// Textures entries.
	/// </summary>
	[Title( "#effects.group.textures" )]
	Textures,

	/// <summary>
	/// Misc entries.
	/// </summary>
	[Title( "#effects.group.misc" )]
	Misc
}

[AssetType( Name = "Post Process Effect", Extension = "spp", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class PostProcessResource : GameResource, IDefinitionResource
{
	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public PostProcessGroup Group { get; set; } = PostProcessGroup.Misc;

	[Property]
	public Texture Icon { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	[Property]
	public bool IncludeCode { get; set; } = true;

	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		if ( Icon is null ) return default;

		return Icon.GetBitmap( 0 );
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "🎨", width, height, "#35B851" );
	}

	public override void ConfigurePublishing( ResourcePublishContext context )
	{
		if ( Prefab is null )
		{
			context.SetPublishingDisabled( "Invalid: missing a prefab" );
			return;
		}

		if ( Icon is null )
		{
			context.SetPublishingDisabled( "Invalid: missing an icon" );
			return;
		}

		context.IncludeCode = IncludeCode;
	}
}

