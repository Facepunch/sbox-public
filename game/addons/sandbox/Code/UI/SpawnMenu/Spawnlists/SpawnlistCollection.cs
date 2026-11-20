namespace Sandbox;

/// <summary>
/// Class that handles all the spawnlist data fetching and workshop fetching so it's not a huge mess everywhere.
/// </summary>
public class SpawnlistCollection
{
	public record Entry(
		string Icon,
		string Name,
		Storage.Entry StorageEntry,
		ulong WorkshopId,
		bool IsEditable
	);

	/// <summary>
	/// Raised whenever the visible entry list changes.
	/// </summary>
	internal Action OnChanged { get; set; }

	/// <summary>
	/// Raised after a workshop install, with the installed entry name.
	/// </summary>
	internal Action<string> OnInstalled { get; set; }

	/// <summary>
	/// Raised after an uninstall.
	/// </summary>
	public event Action Uninstalled;

	public IReadOnlyList<Entry> Entries => _entries;

	/// <summary>
	/// Number of cookie-tracked workshop entries not yet resolved from storage or the cloud.
	/// Non-zero while the initial cloud fetch is in progress.
	/// </summary>
	public int PendingCount
	{
		get
		{
			if ( !_loading ) return 0;
			var loaded = _entries.Select( e => e.WorkshopId ).Where( id => id > 0 ).ToHashSet();
			return new SavedSpawnlists().Installed.Count( id => !loaded.Contains( id ) );
		}
	}

	List<Entry> _entries = new();
	Dictionary<ulong, Storage.Entry> _cloudEntries = new();
	bool _queried;
	bool _loading;

	/// <summary>
	/// Rebuilds the visible list and triggers a cloud re-fetch.
	/// </summary>
	public void Refresh()
	{
		_queried = false;
		Rebuild();
	}

	/// <summary>
	/// Install a workshop item, persist its ID to cookie, and refresh.
	/// </summary>
	public async Task InstallAsync( Storage.QueryItem item )
	{
		var entry = await item.Install();
		if ( entry is null ) return;

		var saved = new SavedSpawnlists();
		saved.Add( item.Id );
		saved.Save();

		OnInstalled?.Invoke( item.Title );
		Refresh();
	}

	/// <summary>
	/// Uninstall a workshop-installed entry: remove from cookie, cloud list, and rebuild.
	/// </summary>
	public void Uninstall( ulong workshopId )
	{
		if ( workshopId == 0 ) return;

		var saved = new SavedSpawnlists();
		if ( saved.Remove( workshopId ) )
			saved.Save();

		_cloudEntries.Remove( workshopId );
		Uninstalled?.Invoke();
		Rebuild();
	}

	/// <summary>
	/// Delete a locally editable spawnlist and rebuild.
	/// </summary>
	public void Delete( Storage.Entry entry )
	{
		try
		{
			SpawnlistData.Delete( entry );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Something went wrong while deleting an entry: {e.Message}" );
		}
		finally
		{
			Refresh();
		}
	}

	struct SavedSpawnlists
	{
		public List<ulong> Installed { get; set; }

		public SavedSpawnlists()
		{
			Installed = Game.Cookies.Get<List<ulong>>( "spawnlists.installed", new() );
		}

		public void Save() => Game.Cookies.Set( "spawnlists.installed", Installed );

		public void Add( ulong id ) { if ( !Installed.Contains( id ) ) Installed.Add( id ); }
		public bool Remove( ulong id ) => Installed.Remove( id );
		public HashSet<ulong> ToHashSet() => Installed.ToHashSet();
	}

	void Rebuild()
	{
		var installedIds = new SavedSpawnlists().ToHashSet();
		var result = new List<Entry>();

		foreach ( var storageEntry in SpawnlistData.GetAll() )
		{
			SpawnlistData data;

			try
			{
				if ( storageEntry.Files.IsReadOnly ) continue;
				data = SpawnlistData.Load( storageEntry );
			}
			catch
			{
				continue;
			}

			result.Add( new Entry( "folder", data.Name, storageEntry, 0, true ) );
		}

		foreach ( var (workshopId, storageEntry) in _cloudEntries )
		{
			if ( !installedIds.Contains( workshopId ) ) continue;

			var data = SpawnlistData.Load( storageEntry );
			result.Add( new Entry( "cloud", data.Name, storageEntry, workshopId, false ) );
		}

		_entries = result;

		if ( !_queried ) _loading = true;

		OnChanged?.Invoke();

		if ( !_queried )
		{
			_queried = true;
			_ = FetchCloudSpawnlists();
		}
	}

	async Task FetchCloudSpawnlists()
	{
		var query = new Storage.Query();
		query.KeyValues["package"] = "facepunch.sandbox";
		query.KeyValues["type"] = "spawnlist";
		query.Author = Game.SteamId;

		var result = await query.Run();

		if ( result?.Items is not null )
		{
			foreach ( var item in result.Items )
			{
				if ( _cloudEntries.ContainsKey( item.Id ) ) continue;

				var installed = await item.Install();
				if ( installed == null ) continue;

				_cloudEntries[item.Id] = installed;
			}
		}

		var missingIds = new SavedSpawnlists().Installed
			.Where( id => !_cloudEntries.ContainsKey( id ) )
			.ToList();

		if ( missingIds.Count > 0 )
		{
			var missingResult = await new Storage.Query { FileIds = missingIds }.Run();
			if ( missingResult?.Items is not null )
			{
				foreach ( var item in missingResult.Items )
				{
					if ( _cloudEntries.ContainsKey( item.Id ) ) continue;

					var installed = await item.Install();
					if ( installed == null ) continue;

					_cloudEntries[item.Id] = installed;
				}
			}
		}

		_loading = false;
		Rebuild();
	}
}
