

[AssetType( Name = "Sandbox Thruster", Extension = "tdef", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class ThrusterDefinition : GameResource, IDefinitionResource
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
		return CreateSimpleAssetTypeIcon( "🚀", width, height, "#f54248" );
	}
}
