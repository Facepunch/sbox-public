using Sandbox.Rendering;

[AssetType( Name = "Sandbox Line", Extension = "ldef", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class LineDefinition : GameResource, IDefinitionResource
{
	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	[Property, Group( "Material" )]
	public Material Material { get; set; }

	[Property, Group( "Material" )]
	public bool WorldSpace { get; set; } = true;

	[Property, Range( 1, 128 ), Group( "Material" )]
	public float UnitsPerTexture { get; set; } = 32.0f;

	[Property, Group( "Material" )]
	public bool Opaque { get; set; } = true;

	[Property, Group( "Material" )]
	public BlendMode BlendMode { get; set; }

	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		if ( !Material.IsValid() )
		{
			// No material, but return a blank white texture instead of nothing.
			var blank = new Bitmap( options.Width, options.Height );
			blank.Clear( Color.White );
			return blank;
		}

		var texture = Material.GetTexture( "g_tColor" );
		if ( texture is null )return default;

		var bitmap = new Bitmap( options.Width, options.Height );
		bitmap.Clear( Color.Transparent );

		bitmap = bitmap.Resize( texture.Width, texture.Height );
		bitmap.DrawBitmap( texture.GetBitmap( 0 ), new Rect( 0, 0, texture.Width, texture.Height ) );

		return bitmap;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "✨", width, height, "#48c0f5" );
	}
}
