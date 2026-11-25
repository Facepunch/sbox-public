[AssetType( Name = "Sandbox Button", Extension = "btndef", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class ButtonDefinition : GameResource, IDefinitionResource
{
	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	/// <inheritdoc/>
	public override Bitmap RenderThumbnail( ThumbnailOptions options ) => Prefab?.RenderThumbnail( options );

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "🔘", width, height, "#e54b4b" );
	}
}
