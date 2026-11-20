/// <summary>
/// Makes a decal available in the Sandbox toolgun's decal picker.
/// </summary>
[AssetType( Name = "Sandbox Tool Decal", Extension = "ddef", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public sealed class ToolDecalDefinition : GameResource, IDefinitionResource
{
	/// <summary>
	/// Finds a tool decal in the loaded game and addon definitions.
	/// </summary>
	public static ToolDecalDefinition Find( string path )
	{
		return string.IsNullOrEmpty( path ) ? null : ResourceLibrary.Get<ToolDecalDefinition>( path );
	}

	/// <summary>
	/// Updates decal paths stored by earlier versions of the tool.
	/// </summary>
	public static string ResolveSelectionPath( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return path;

		if ( path.EndsWith( ".decal", StringComparison.OrdinalIgnoreCase ) )
		{
			return ResourceLibrary.GetAll<ToolDecalDefinition>().FirstOrDefault( x => string.Equals( x.Decal?.ResourcePath, path, StringComparison.OrdinalIgnoreCase ) )?.ResourcePath;
		}

		const string oldFolder = "decals/graffiti/";
		if ( path.StartsWith( oldFolder, StringComparison.OrdinalIgnoreCase ) && Find( path ) is null )
		{
			var moved = Find( "tools/decals/defs/" + path[oldFolder.Length..] );
			if ( moved is not null ) return moved.ResourcePath;
		}

		return path;
	}

	/// <summary>
	/// The decal to preview and paint.
	/// </summary>
	[Property]
	public DecalDefinition Decal { get; set; }

	/// <summary>
	/// The name shown in the toolgun picker.
	/// </summary>
	[Property]
	public string Title { get; set; }

	/// <summary>
	/// The description shown when hovering over the decal.
	/// </summary>
	[Property]
	public string Description { get; set; }

	/// <summary>
	/// Draws the decal's color texture into its picker thumbnail.
	/// </summary>
	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		var texture = Decal?.ColorTexture;
		if ( !texture.IsValid() ) return null;

		using var source = texture.GetBitmap();
		var bitmap = new Bitmap( options.Width, options.Height );
		bitmap.Clear( Color.Transparent );
		bitmap.DrawBitmap( source, new Rect( 0, 0, options.Width, options.Height ).Contain( new Vector2( source.Width, source.Height ) ) );
		return bitmap;
	}
}
