using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Shared spawnlist support for spawn menu pages.
/// </summary>
public abstract class SpawnlistsPage : BaseSpawnMenu
{
	public SpawnlistCollection Collection { get; } = new();
	readonly Dictionary<string, SpawnMenuOption> _spawnlistOptions = new();

	public SpawnlistsPage()
	{
		Collection.OnChanged = OnCollectionChanged;
		SpawnlistData.SpawnlistCreated += Collection.Refresh;
		Collection.Refresh();
	}

	public override void OnHotloaded()
	{
		// Constructors do not run again for existing panels during hotload.
		Collection.OnChanged = OnCollectionChanged;
		base.OnHotloaded();
		Collection.Refresh();
	}

	void OnCollectionChanged()
	{
		OnParametersSet();

		// A removed list must stop refreshing its deleted storage and leave the
		// content area on a valid page, whether removed here or from the sidebar.
		if ( ActivePanel is SpawnlistView { Entry: not null } view &&
			!Collection.Entries.Any( entry => entry.StorageEntry.Id == view.Entry.Id ) )
		{
			DeselectOption();
			view.Delete( true );
			SelectOption( "#spawnmenu.props.all" );
		}
	}

	public override void OnDeleted()
	{
		SpawnlistData.SpawnlistCreated -= Collection.Refresh;
		Collection.OnChanged = null;
		base.OnDeleted();
	}

	protected void AddSpawnlistOptions()
	{
		SpawnMenuOption header = AddHeader( "#spawnmenu.section.your_spawnlists" );

		AddResourceSpawnlists();
		AddProceduralSpawnlists();
		AddUserSpawnlists();

		// hide the header if there are no spawnlists to show
		if ( !header.HasChildren )
		{
			RemoveOption( header );
		}
	}

	/// <summary>Built-in generated lists, alongside the authored default lists.</summary>
	protected virtual void AddProceduralSpawnlists() { }

	void AddResourceSpawnlists()
	{
		var resources = SpawnlistResource.GetVisible().ToList();
		var visible = resources.ToHashSet();
		var added = new HashSet<SpawnlistResource>();

		void AddWithChildren( SpawnlistResource resource, SpawnMenuOption parent )
		{
			if ( !added.Add( resource ) ) return;

			var option = AddOption( parent, new SpawnMenuOption
			{
				Icon = resource.Icon ?? "collections_bookmark",
				IconTexture = resource.IconImage,
				Name = resource.DisplayName,
				Key = resource.ResourcePath,
				Tokenize = false,
				PanelCreator = () => new SpawnlistView { Resource = resource }
			} );

			foreach ( var child in resources.Where( x => x.Parent == resource ) )
				AddWithChildren( child, option );
		}

		foreach ( var resource in resources.Where( x => x.Parent is null || !visible.Contains( x.Parent ) ) )
			AddWithChildren( resource, null );

		foreach ( var resource in resources )
			AddWithChildren( resource, null );
	}

	void AddUserSpawnlists()
	{
		_spawnlistOptions.Clear();

		foreach ( var entry in Collection.Entries )
		{
			var storage = entry.StorageEntry;
			var option = AddOption( "folder", entry.Name,
				() => new SpawnlistView { Entry = storage },
				() => OnLocalRightClick( storage ) );

			option.Tokenize = false;
			option.Key = storage.Id;
			_spawnlistOptions[storage.Id] = option;
		}
	}

	protected override void OnMenuFooter( Panel footer )
	{
		footer.AddChild<SpawnlistFooter>();
	}

	/// <summary>Refresh after external changes (create, etc.).</summary>
	public void RefreshList() => Collection.Refresh();

	public void SelectSpawnlist( Storage.Entry entry )
	{
		if ( !_spawnlistOptions.TryGetValue( entry.Id, out var option ) ) return;
		_firstViewed = true;
		SwitchOption( option );
	}

	/// <summary>
	/// Opens the local list in edit mode with keyboard focus on its name.
	/// </summary>
	internal void EditSpawnlist( Storage.Entry entry )
	{
		SelectSpawnlist( entry );
		if ( ActivePanel is SpawnlistView view && view.Entry?.Id == entry.Id )
			view.EditName();
	}

	void OnLocalRightClick( Storage.Entry entry )
	{
		var menu = new Sandbox.UI.Menu();
		menu.AddOption( "#spawnmenu.spawnlist.rename", "edit", () => EditSpawnlist( entry ) );
		menu.AddOption( "#spawnmenu.spawnlist.delete", "delete", () => Collection.Delete( entry ) );
		menu.Open( this, Popup.PositionMode.UnderMouse );
	}
}
