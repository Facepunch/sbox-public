

[AssetType( Name = "Sandbox Entity", Extension = "sent", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding | AssetTypeFlags.IncludeThumbnails )]
public class ScriptedEntity : GameResource, IDefinitionResource
{
	/// <summary>Optional local game content required to spawn this entity.</summary>
	[Property] public string RequiredMount { get; set; }

	public bool IsMountAvailable => string.IsNullOrEmpty( RequiredMount ) || Sandbox.Mounting.Directory.Get( RequiredMount )?.IsInstalled == true;

	/// <summary>
	/// Texture in <see cref="RequiredMount"/> to use as the spawn menu icon, such as the game's own spawn icon.
	/// Mounted art loads at runtime, so it can't be rendered from the prefab or baked into a thumbnail.
	/// </summary>
	[Property] public string PreviewIcon { get; set; }

	/// <summary>
	/// Model in <see cref="RequiredMount"/> the prefab shows once mounted, such as models/police.mdl. Rendered for the
	/// spawn menu icon when there's no <see cref="PreviewIcon"/>, and gives the spawn bounds.
	/// </summary>
	[Property] public string PreviewModel { get; set; }

	/// <summary>
	/// Mount <see cref="RequiredMount"/> and load <see cref="PreviewModel"/> from it. Null when either isn't there.
	/// </summary>
	public async Task<Model> LoadPreviewModelAsync()
	{
		if ( string.IsNullOrEmpty( RequiredMount ) || string.IsNullOrEmpty( PreviewModel ) ) return null;

		var mount = await Sandbox.Mounting.Directory.Mount( RequiredMount );
		if ( mount?.IsMounted != true ) return null;

		var model = await Model.LoadAsync( PreviewModelPath );
		return model.IsValid() && !model.IsError ? model : null;
	}

	string PreviewModelPath => $"mount://{RequiredMount}/{PreviewModel}.vmdl";

	/// <summary>
	/// The spawn menu icon of an entity whose art comes from <see cref="RequiredMount"/>, mounting it first if it's
	/// installed but not mounted yet. Null when the mount or its art is missing.
	/// </summary>
	public async Task<Texture> LoadMountedIconAsync()
	{
		if ( string.IsNullOrEmpty( RequiredMount ) ) return null;

		if ( !string.IsNullOrEmpty( PreviewIcon ) )
		{
			var mount = await Sandbox.Mounting.Directory.Mount( RequiredMount );
			if ( mount?.IsMounted != true ) return null;

			var icon = await Texture.LoadAsync( $"mount://{RequiredMount}/{PreviewIcon}.vtex" );
			return icon.IsValid() && !icon.IsError ? icon : null;
		}

		if ( Prefab is null || !IsMountAvailable ) return null;

		if ( !string.IsNullOrEmpty( PreviewModel ) )
			return await Prefab.LoadThumbnailAsync( PreviewModelPath );

		var requiredMount = await Sandbox.Mounting.Directory.Mount( RequiredMount );
		return requiredMount?.IsMounted == true ? await Prefab.LoadThumbnailAsync() : null;
	}

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
	/// If true, this entity only appears in the spawn menu when running in the editor.
	/// Use for test/debug entities that shouldn't ship to players.
	/// </summary>
	[Property]
	public bool Developer { get; set; }

	public override Bitmap RenderThumbnail( ThumbnailOptions options )
	{
		// No prefab - can't make a thumbnail. Mounted art isn't loaded here, and mustn't be baked into one.
		if ( Prefab is null || !string.IsNullOrEmpty( RequiredMount ) ) return default;

		return Prefab.RenderThumbnail( options );
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "📦", width, height, "#f54248" );
	}

	public override void ConfigurePublishing( ResourcePublishContext context )
	{
		if ( Prefab is null )
			context.SetPublishingDisabled( "Invalid: missing a prefab" );
	}
}
