[AssetType( Name = "Sandbox Button", Extension = "btndef", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class ButtonDefinition : GameResource, IDefinitionResource
{
	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		if ( Prefab is null ) return default;

		var bitmap = new Bitmap( options.Width, options.Height );
		bitmap.Clear( Color.Transparent );
		SceneUtility.RenderGameObjectToBitmap( Prefab.GetScene(), bitmap );
		return bitmap;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "🔘", width, height, "#e54b4b" );
	}
}
