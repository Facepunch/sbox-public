
[AssetType( Name = "Scripted Model Emitter", Extension = "smemit", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class ScriptedEmitterModel : GameResource, IDefinitionResource
{
	/// <summary>
	/// The prefab containing the physical emitter body
	/// </summary>
	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		return ThumbnailCamera.RenderToolModel( Prefab, options.Width, options.Height );
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "💨", width, height, "#42b4f5" );
	}
}
