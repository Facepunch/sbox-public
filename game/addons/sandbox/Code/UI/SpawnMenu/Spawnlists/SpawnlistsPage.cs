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
		BindCollection();
		SpawnlistData.SpawnlistCreated += Collection.Refresh;
		Collection.Refresh();
	}

	void BindCollection()
	{
		Collection.OnChanged = OnCollectionChanged;
		Collection.OnInstalled = OnCollectionInstalled;
	}

	public override void OnHotloaded()
	{
		// Constructors do not run again for existing panels during hotload.
		BindCollection();
		base.OnHotloaded();
		OnParametersSet();
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

	void OnCollectionInstalled( string name )
	{
		OnParametersSet();
		SelectOption( name );
	}

	public override void OnDeleted()
	{
		SpawnlistData.SpawnlistCreated -= Collection.Refresh;
		Collection.OnChanged = null;
		Collection.OnInstalled = null;
		base.OnDeleted();
	}

	protected void AddSpawnlistOptions()
	{
		SpawnMenuOption header = AddHeader( "#spawnmenu.section.workshop_spawnlists" );

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
		if ( Collection.Entries.Count > 0 || Collection.PendingCount > 0 )
		{
			foreach ( var entry in Collection.Entries )
			{
				var captured = entry;
				_spawnlistOptions[entry.StorageEntry.Id] = AddOption( entry.Icon, entry.Name,
					() => new SpawnlistView { Entry = captured.StorageEntry },
					entry.IsEditable
						? () => OnEditableRightClick( captured )
						: () => OnInstalledRightClick( captured ) );
				_spawnlistOptions[entry.StorageEntry.Id].Tokenize = false;
			}
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

	void OnEditableRightClick( SpawnlistCollection.Entry entry )
	{
		var menu = new Sandbox.UI.Menu();

		if ( entry.StorageEntry.GetMeta( "_workshopId", 0ul ) == 0 )
		{
			menu.AddOption( "#spawnmenu.spawnlist.rename", "edit", () =>
			{
				var data = SpawnlistData.Load( entry.StorageEntry );
				UIModal.Prompt( this, "#spawnmenu.spawnlist.rename_title", data.Name, newName =>
				{
					if ( entry.StorageEntry.GetMeta( "_workshopId", 0ul ) != 0 ) return;
					SpawnlistData.Rename( entry.StorageEntry, newName );
					Collection.Refresh();
				}, placeholder: "#spawnmenu.spawnlist.name_placeholder", confirmText: "#spawnmenu.spawnlist.rename_button" );
			} );
		}

		menu.AddOption( "#spawnmenu.spawnlist.delete", "delete", () => Collection.Delete( entry.StorageEntry ) );

		menu.Open( this, Popup.PositionMode.UnderMouse );
	}

	void OnInstalledRightClick( SpawnlistCollection.Entry entry )
	{
		var menu = new Sandbox.UI.Menu();
		menu.AddOption( "#spawnmenu.spawnlist.remove", "delete", () => Collection.Uninstall( entry.WorkshopId ) );

		menu.Open( this, Popup.PositionMode.UnderMouse );
	}
}

