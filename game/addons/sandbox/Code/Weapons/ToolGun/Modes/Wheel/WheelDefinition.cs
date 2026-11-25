

[AssetType( Name = "Sandbox Wheel", Extension = "wdef", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class WheelDefinition : GameResource, IDefinitionResource
{
	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	public override Bitmap RenderThumbnail( ThumbnailOptions options ) => Prefab?.RenderThumbnail( options );

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "🛞", width, height, "#f54248" );
	}
}
