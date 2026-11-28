namespace Sandbox;

[AssetType( Name = "Sandbox Spawnlist", Extension = "spawn", Category = "Sandbox", Flags = AssetTypeFlags.NoEmbedding )]
public class SpawnlistResource : GameResource
{
	[Property]
	public string Title { get; set; }

	[Property]
	public string Description { get; set; }

	/// <summary>
	/// Material icon shown next to the list in the spawn menu sidebar.
	/// </summary>
	[Property, IconName]
	public string Icon { get; set; } = "collections_bookmark";

	/// <summary>
	/// Optional texture shown in place of the sidebar icon.
	/// </summary>
	[Property]
	public Texture IconImage { get; set; }

	/// <summary>
	/// Mount required to see this list in the spawn menu. Leave empty for no requirement.
	/// </summary>
	public string RequiredMount { get; set; }

	/// <summary>
	/// Nests this list under another one in the spawn menu tree. Leave empty for a top level list.
	/// </summary>
	[Property]
	public SpawnlistResource Parent { get; set; }

	/// <summary>
	/// Lists are sorted by this, then by title.
	/// </summary>
	[Property]
	public int Order { get; set; }

	/// <summary>
	/// Keeps the list out of the spawn menu tree, for a list a built-in page shows itself
	/// (e.g. Building Props > Robotics).
	/// </summary>
	[Property]
	public bool HideInSidebar { get; set; }

	[Property]
	public List<Entry> Items { get; set; } = new();

	public class Entry
	{
		[Property]
		public SpawnlistItemKind Kind { get; set; }

		/// <summary>
		/// A prefab to spawn directly. The spawnlist includes it as a resource dependency.
		/// </summary>
		[Property, ShowIf( nameof( Kind ), SpawnlistItemKind.Item )]
		public PrefabFile Prefab { get; set; }

		/// <summary>
		/// A raw spawn ident, for anything that isn't a local model or entity,
		/// e.g. <c>prop:facepunch.wooden_crate</c>. Used when no model or entity is set.
		/// </summary>
		[Property, ShowIf( nameof( Kind ), SpawnlistItemKind.Item )]
		public string Ident { get; set; }

		/// <summary>
		/// Item label, or the section heading for a title row.
		/// </summary>
		[Property, HideIf( nameof( Kind ), SpawnlistItemKind.Divider )]
		public string Title { get; set; }

		/// <summary>
		/// Optional thumbnail override. Defaults to the content's own thumbnail.
		/// </summary>
		[Property, ShowIf( nameof( Kind ), SpawnlistItemKind.Item ), IconName]
		public string Icon { get; set; }

		/// <summary>
		/// Expands model paths for prop and mount spawners while preserving other spawn identifiers.
		/// </summary>
		public string GetIdent()
		{
			if ( Prefab is not null )
				return $"prefab:{Prefab.ResourcePath}";

			var ident = Ident?.Trim();
			if ( string.IsNullOrEmpty( ident ) )
				return null;

			var (type, path, _) = SpawnlistItem.ParseIdent( ident );
			if ( type is ("prop" or "mount") && path.Contains( '/' ) && !path.EndsWith( ".vmdl" ) )
				ident += ".vmdl";

			return ident;
		}

		public override string ToString()
		{
			return $"({Kind}) {GetIdent() ?? Title}";
		}
	}

	/// <summary>
	/// Resolves a spawn request against the prefabs offered by the game's authored spawnlists.
	/// </summary>
	internal static Entry FindPrefab( string path )
	{
		return ResourceLibrary.GetAll<SpawnlistResource>()
			.Where( list => !list.IsRemote && list.ShouldShow() )
			.SelectMany( list => list.Items ?? [] )
			.FirstOrDefault( entry => entry?.Kind == SpawnlistItemKind.Item && entry.Prefab is not null
				&& string.Equals( entry.Prefab.ResourcePath, path, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// The title in the player's language. Titles can be <c>#phrase</c> references.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public string DisplayName => string.IsNullOrWhiteSpace( Title ) ? ResourceName : LocalizedText.Resolve( Title );

	/// <summary>
	/// The resource in the shape the spawnlist view draws. Items get stable ids
	/// from their position so the view can tell when the resource changes.
	/// </summary>
	public SpawnlistData ToData()
	{
		var data = new SpawnlistData { Name = DisplayName, Description = Description ?? "" };

		for ( var i = 0; i < (Items?.Count ?? 0); i++ )
		{
			var entry = Items[i];
			if ( entry is null ) continue;

			var item = new SpawnlistItem
			{
				Id = new Guid( i + 1, 0, 0, new byte[8] ),
				Kind = entry.Kind,
				Title = entry.Title,
			};

			if ( entry.Kind == SpawnlistItemKind.Item )
			{
				item.Ident = entry.GetIdent();
				if ( item.Ident is null ) continue;
				item.Icon = string.IsNullOrWhiteSpace( entry.Icon ) ? null : entry.Icon;
			}

			data.Items.Add( item );
		}

		return data;
	}

	bool ShouldShow()
	{
		if ( !string.IsNullOrWhiteSpace( RequiredMount ) )
		{
			var mount = Sandbox.Mounting.Directory.Get( RequiredMount );
			if ( mount is null || !mount.IsMounted ) return false;
		}

		return true;
	}

	/// <summary>
	/// Every authored spawnlist the player should see in the spawn menu tree, in display order.
	/// </summary>
	public static IEnumerable<SpawnlistResource> GetVisible()
	{
		return ResourceLibrary.GetAll<SpawnlistResource>()
			.Concat( MountedSpawnlists.Build() )
			.Where( x => !x.HideInSidebar && x.ShouldShow() )
			.OrderBy( x => x.Order )
			.ThenBy( x => x.DisplayName, StringComparer.OrdinalIgnoreCase );
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "📋", width, height, "#3fa9f5" );
	}
}
