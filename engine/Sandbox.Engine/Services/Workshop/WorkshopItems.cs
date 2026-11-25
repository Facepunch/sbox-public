using Sandbox.Internal;
using Sandbox.Network;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Owns a game's Workshop items and tells joining clients which Steam items to install.
/// </summary>
internal sealed class WorkshopItems
{
	internal static WorkshopItems Current { get; set; }
	internal readonly StringTable StringTable = new( "WorkshopItems", true );

	internal struct ItemInfo
	{
		internal ulong Id;
		internal Workshop.ItemType Type;
		internal long Version;
		internal bool ResourcesReady;
	}

	sealed class Item
	{
		internal Task<WorkshopFileSystem> Install;
		internal CancellationTokenSource Cancellation;
		internal Workshop.ItemType Type;
		internal bool ResourcesLoaded;
		internal bool Failed;
		internal int Requirements;
		internal BaseFileSystem MountedInto;
		internal WorkshopFileSystem Mount => Install.IsCompletedSuccessfully ? Install.Result : null;
	}

	readonly Dictionary<ulong, Item> items = new();
	readonly SemaphoreSlim resourceLoading = new( 1, 1 );
	readonly Func<ulong, CancellationToken, Task<WorkshopFileSystem>> createMount;
	CancellationTokenSource cancellation = new();

	internal WorkshopItems( Func<ulong, CancellationToken, Task<WorkshopFileSystem>> createMount = null )
	{
		this.createMount = createMount ?? (( id, token ) => WorkshopFileSystem.OpenAsync( id, $"Workshop item {id}", token ));
	}

	/// <summary>
	/// Whether either the host or this client is still preparing a required item.
	/// </summary>
	internal bool HasPending => StringTable.Entries.Values.Any( entry =>
	{
		var info = entry.Read<ItemInfo>();
		return !info.ResourcesReady || !items.TryGetValue( info.Id, out var item ) || !item.Install.IsCompleted;
	} );

	/// <summary>
	/// Whether any required item failed to mount or load.
	/// </summary>
	internal bool HasFailures => StringTable.Entries.Values.Any( entry =>
		!items.TryGetValue( entry.Read<ItemInfo>().Id, out var item ) || item.Failed || item.Mount is null );

	/// <summary>
	/// Loads an item on the host, publishing its scripts before marking its resources ready for clients.
	/// </summary>
	internal async Task<WorkshopFileSystem> LoadAsync( ulong id, Workshop.ItemType type, CancellationToken token = default )
	{
		return await InstallAsync( id, type, publish: Networking.IsHost, loadResources: !Networking.IsClient, token ) ? Get( id ) : null;
	}

	/// <summary>
	/// Hosts acquire a requirement for the Workshop map. Clients open the item from their host's table.
	/// </summary>
	internal async Task<(string Asset, Action Release)> OpenMapAsync( ulong id, CancellationToken token )
	{
		var client = Networking.IsClient;

		WorkshopFileSystem mount;
		if ( client )
		{
			if ( !items.TryGetValue( id, out var item ) || item.Type != Workshop.ItemType.Map ) return default;
			mount = await item.Install.WaitAsync( token );
		}
		else
		{
			mount = await LoadAsync( id, Workshop.ItemType.Map, token );
		}
		if ( mount is null || Get( id ) != mount ) return default;

		// Only a host holds a requirement. A client uses the item its host required.
		Action release = client ? null : () =>
		{
			if ( Get( id ) == mount ) RemoveRequirement( id );
		};

		try
		{
			var asset = FindPrimaryAsset( mount, mount.Metadata?.Meta )
				?? throw new InvalidDataException( $"Workshop map {id} has no loadable primary asset." );

			return (asset, release);
		}
		catch ( Exception e )
		{
			release?.Invoke();
			Log.Warning( e, $"Couldn't resolve Workshop map {id}" );
			return default;
		}
	}

	/// <summary>
	/// The map in an item's files: the primary asset its metadata names, or failing that the one <see cref="FindMapFile"/> picks.
	/// Null if that isn't a map the item has.
	/// </summary>
	internal static string FindPrimaryAsset( BaseFileSystem files, string meta )
	{
		var asset = Json.Deserialize<JsonObject>( meta ?? "{}" )?["PrimaryAsset"]?.GetValue<string>() ?? FindMapFile( files );

		return MapReference.IsAssetPath( asset ) && MapReference.Contains( files, asset ) ? asset : null;
	}

	/// <summary>
	/// The map in an item that doesn't name its primary asset. Prefers a scene, then the shortest-named VPK so a skybox VPK is skipped.
	/// </summary>
	static string FindMapFile( BaseFileSystem files )
	{
		var scene = files.FindFile( "/", "*.scene_c", true ).Select( x => x[..^2] )
			.Concat( files.FindFile( "/", "*.scene", true ) )
			.OrderBy( x => x.Length ).FirstOrDefault();

		return scene ?? files.FindFile( "/", "*.vpk", true ).OrderBy( x => x.Length ).FirstOrDefault();
	}

	/// <summary>
	/// Downloads a host-selected item. Resource loading waits until the client has received its scripts.
	/// </summary>
	internal Task<bool> InstallAsync( StringTable.Entry entry, bool loadResources = false )
	{
		var info = entry.Read<ItemInfo>();
		return InstallAsync( info.Id, info.Type, publish: false, loadResources, default );
	}

	async Task<bool> InstallAsync( ulong id, Workshop.ItemType type, bool publish, bool loadResources, CancellationToken token )
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentOutOfRangeException.ThrowIfZero( id );
		token.ThrowIfCancellationRequested();
		var key = id.ToString( CultureInfo.InvariantCulture );

		if ( !items.TryGetValue( id, out var item ) )
		{
			item = new Item
			{
				Cancellation = CancellationTokenSource.CreateLinkedTokenSource( cancellation.Token ),
				Type = type,
				MountedInto = FileSystem.Mounted
			};
			item.Install = InstallItemAsync( item, id, item.Cancellation.Token );
			items.Add( id, item );
		}

		if ( item.Type != type )
		{
			Log.Warning( $"Workshop item {id} is already required as {item.Type}, not {type}." );
			return false;
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource( token, item.Cancellation.Token );
		token = linked.Token;
		if ( publish && !StringTable.Entries.ContainsKey( key ) )
			StringTable.Set( key, new ItemInfo { Id = id, Type = type } );

		if ( publish ) item.Requirements++;

		bool completed = false;
		try
		{
			await item.Install.WaitAsync( token );

			if ( loadResources && !item.ResourcesLoaded )
			{
				NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();
				await LoadResourcesAsync( item, token );
			}

			token.ThrowIfCancellationRequested();
			if ( publish )
				StringTable.Set( key, new ItemInfo { Id = id, Type = type, Version = item.Mount.Version, ResourcesReady = true } );

			completed = true;
			return true;
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
		{
			if ( publish ) throw;
			return false;
		}
		catch ( Exception e )
		{
			item.Failed = true;
			Log.Warning( e, $"Couldn't load Workshop item {id}" );
			return false;
		}
		finally
		{
			// A cancelled old session must not remove an entry from the next game.
			if ( publish && !completed && items.TryGetValue( id, out var current ) && ReferenceEquals( item, current ) )
			{
				RemoveRequirement( id );
				if ( item.Requirements == 0 && !item.Install.IsCompletedSuccessfully )
				{
					items.Remove( id );
					Release( item );
				}
			}
		}
	}

	async Task<WorkshopFileSystem> InstallItemAsync( Item item, ulong id, CancellationToken token )
	{
		WorkshopFileSystem mount = null;
		try
		{
			mount = await createMount( id, token );
			token.ThrowIfCancellationRequested();
			var validContent = item.Type == Workshop.ItemType.Addon
				? Workshop.IsAddonMetadataForGame( mount.Metadata, Application.GameIdent )
				: mount.Metadata is { } metadata && string.Equals( metadata.TypeName, item.Type.ToString(), StringComparison.OrdinalIgnoreCase );
			if ( !validContent )
				throw new InvalidDataException( $"Workshop item {id} is not valid {item.Type.ToString().ToLowerInvariant()} content for this game." );

			// Installation is only complete once the files are visible to scripts and resources.
			item.MountedInto.Mount( mount );
			return mount;
		}
		catch
		{
			if ( mount is not null )
			{
				item.MountedInto.UnMount( mount );
				mount.Dispose();
			}
			throw;
		}
	}

	async Task LoadResourcesAsync( Item item, CancellationToken token )
	{
		await resourceLoading.WaitAsync( token );
		try
		{
			if ( item.ResourcesLoaded ) return;

			LoadingScreen.Title = $"Loading {item.Mount.Title}";
			await ResourceLoader.LoadAllGameResourceAsync( item.Mount, token );
			FontManager.Instance.LoadAll( item.Mount );
			item.ResourcesLoaded = true;
			item.Failed = false;
		}
		finally
		{
			resourceLoading.Release();
		}
	}

	/// <summary>
	/// Mounts the initial item list before the client runs the host's scripts.
	/// </summary>
	internal async Task<bool> InstallAllAsync()
	{
		foreach ( var entry in StringTable.Entries.Values.ToArray() )
		{
			if ( !await InstallAsync( entry ) ) return false;
		}

		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();
		return !HasFailures;
	}

	/// <summary>
	/// Waits for downloads started by table updates before scripts use their files.
	/// </summary>
	internal async Task WaitForDownloadsAsync()
	{
		var token = cancellation.Token;
		while ( items.Values.Any( item => !item.Install.IsCompleted ) )
			await Task.Delay( 20, token );
	}

	/// <summary>
	/// Loads deferred item resources after the client's script snapshot is available.
	/// </summary>
	internal async Task LoadResourcesAsync()
	{
		var token = cancellation.Token;
		foreach ( var entry in StringTable.Entries.Values.ToArray() )
		{
			var info = entry.Read<ItemInfo>();
			if ( info.ResourcesReady && items.TryGetValue( info.Id, out var item ) && item.Mount is not null )
				await LoadResourcesAsync( item, token );
		}
	}

	/// <summary>
	/// Finishes items the previous host was still preparing when ownership changed.
	/// </summary>
	internal async Task OnBecameHost()
	{
		var token = cancellation.Token;
		try
		{
			foreach ( var entry in StringTable.Entries.Values.ToArray() )
			{
				var info = entry.Read<ItemInfo>();
				if ( info.ResourcesReady )
				{
					if ( items.TryGetValue( info.Id, out var item ) ) item.Requirements = Math.Max( item.Requirements, 1 );
				}
				else
				{
					await LoadAsync( info.Id, info.Type, token );
				}
			}
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
		{
			// The game closed while its new host was finishing the downloads.
		}
	}

	/// <summary>
	/// Explains which installed items differ from the host's versions, or returns null when they match.
	/// </summary>
	internal string GetVersionMismatch()
	{
		var names = new List<string>();
		foreach ( var entry in StringTable.Entries.Values )
		{
			var info = entry.Read<ItemInfo>();
			var mount = Get( info.Id );
			if ( mount is not null && info.Version != 0 && mount.Version != 0 && info.Version != mount.Version )
				names.Add( mount.Title );
		}

		return names.Count == 0 ? null : Workshop.GetVersionMismatchMessage( names );
	}

	/// <summary>
	/// Cancels this game's downloads and releases its item files so Steam can update them.
	/// </summary>
	internal void Clear()
	{
		ThreadSafe.AssertIsMainThread();
		var released = items.Values.ToArray();
		items.Clear();
		StringTable.Reset();

		var previous = cancellation;
		cancellation = new();
		previous.Cancel();
		foreach ( var item in released ) Release( item );
		previous.Dispose();
	}

	static void Release( Item item )
	{
		ThreadSafe.AssertIsMainThread();
		item.Cancellation.Cancel();
		if ( item.Mount is { } mount )
		{
			item.MountedInto.UnMount( mount );
			mount.Dispose();
		}
		item.Cancellation.Dispose();
	}

	/// <summary>
	/// Workshop IDs of addons mounted in the current game.
	/// </summary>
	internal ulong[] GetMountedAddonIds()
	{
		return items
			.Where( x => x.Value.Type == Workshop.ItemType.Addon && !x.Value.Failed && x.Value.Mount is not null )
			.Select( x => x.Key )
			.ToArray();
	}

	/// <summary>
	/// Finds the mounted content without fetching a package or starting another download.
	/// </summary>
	internal WorkshopFileSystem Get( ulong id ) => items.GetValueOrDefault( id )?.Mount;

	/// <summary>
	/// Removes one requirement. Files remain available to existing resources until the game closes.
	/// </summary>
	internal void RemoveRequirement( ulong id )
	{
		if ( !Networking.IsHost || !items.TryGetValue( id, out var item ) || item.Requirements == 0 ) return;
		if ( --item.Requirements == 0 ) StringTable.Remove( id.ToString( CultureInfo.InvariantCulture ) );
	}
}
