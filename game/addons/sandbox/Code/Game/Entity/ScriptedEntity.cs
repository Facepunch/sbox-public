

[AssetType( Name = "Sandbox Entity", Extension = "sent", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class ScriptedEntity : GameResource, IDefinitionResource
{
	[Property]
	public PrefabFile Prefab { get; set; }

	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	/// <summary>
	/// Used to group this entity under a named category in the spawn menu (e.g. "Chair", "Weapon", "Npc", "World").
	/// Categories can be nested with <c>/</c>, such as <c>Weapon/Melee</c>.
	/// Leave blank to place it under "Other".
	/// </summary>
	[Property]
	public string Category { get; set; }

	/// <summary>
	/// If this entity uses code then you should enable this so the code is included when publishing.
	/// </summary>
	[Property]
	public bool IncludeCode { get; set; }

	/// <summary>
	/// If true, this entity only appears in the spawn menu when running in the editor.
	/// Use for test/debug entities that shouldn't ship to players.
	/// </summary>
	[Property]
	public bool Developer { get; set; }

	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		// No prefab - can't make a thumbnail
		if ( Prefab is null ) return default;

		var bitmap = new Bitmap( options.Width, options.Height );
		bitmap.Clear( Color.Transparent );

		// A ThumbnailCamera in the prefab frames the shot, otherwise fit the whole thing
		if ( !ThumbnailCamera.TryRender( Prefab, bitmap ) )
			SceneUtility.RenderGameObjectToBitmap( Prefab.GetScene(), bitmap );

		return bitmap;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "📦", width, height, "#f54248" );
	}

	public override void ConfigurePublishing( ResourcePublishContext context )
	{
		if ( Prefab is null )
		{
			context.SetPublishingDisabled( "Invalid: missing a prefab" );
			return;
		}

		context.IncludeCode = IncludeCode;
	}
}
