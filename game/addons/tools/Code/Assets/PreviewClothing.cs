
namespace Editor.Assets;

[AssetPreview( "clothing" )]
class PreviewClothing : PreviewImage
{
	public PreviewClothing( Asset asset ) : base( asset )
	{
		if ( Asset.TryLoadResource<Clothing>( out var clothing ) && !string.IsNullOrEmpty( clothing.Icon.Path ) )
		{
			Texture = Texture.Load( clothing.Icon.Path );
		}
	}
}
