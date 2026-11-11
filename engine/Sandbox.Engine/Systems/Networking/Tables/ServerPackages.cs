using Sandbox.Internal;
using Sandbox.Menu;
using Sandbox.Network;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Manages the network string table "ServerPackages", which contains a list of packages that the client should
/// have installed. During join the client will install these packages before loading in.
/// </summary>
internal class ServerPackages
{
	public static ServerPackages Current { get; private set; }

	internal StringTable StringTable;

	/// <summary>
	/// Packages can download early, but their resources need the host's script declarations first.
	/// The host marks resources ready after scanning the package and publishing its scripts.
	/// </summary>
	internal struct PackageInfo
	{
		internal long WorkshopVersion;
		internal bool ResourcesReady;
	}

	internal class PackageDownload
	{
		public string ident;
		public Package package;
		public bool IsDownloading;
		public bool IsMounted;
		public bool IsErrored;
		public PackageManager.ActivePackage activePackage;
		private bool resourcesLoaded;

		internal async ValueTask<BaseFileSystem> DownloadAndMount( CancellationToken token, bool reloadResources = true )
		{
			// Downloading right now in another task
			if ( IsDownloading )
			{
				while ( IsDownloading ) await Task.Delay( 20, token );
				return activePackage?.FileSystem;
			}

			// Already downloaded
			if ( activePackage != null )
				return activePackage.FileSystem;

			IsDownloading = true;
			try
			{
				package = await Package.Fetch( ident, false );

				if ( package == null )
				{
					Log.Warning( $"Package not found: {ident}" );
					return null;
				}

				var o = new PackageLoadOptions
				{
					PackageIdent = ident,
					ContextTag = "game",
					Loading = new UpdateLoadingScreen(),
					AllowLocalPackages = true,
					CancellationToken = token,
					ReloadResources = reloadResources
				};

				activePackage = await PackageManager.InstallAsync( o );

				if ( reloadResources )
				{
					await LoadResources( token );
				}

				IsMounted = true;
				o.Loading.Dispose();

				return activePackage.FileSystem;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, e.Message );
				Log.Warning( e, e.StackTrace );
				IsErrored = true;
				return null;
			}
			finally
			{
				IsDownloading = false;
			}
		}

		/// <summary>
		/// Loads this package's deferred resources once their script types are available, preserving ownership.
		/// </summary>
		internal async Task LoadResources( CancellationToken token = default )
		{
			if ( resourcesLoaded || activePackage?.FileSystem is null )
				return;

			await _resourceLoadSem.WaitAsync( token );
			try
			{
				if ( resourcesLoaded )
					return;

				LoadingScreen.Title = $"Loading {package?.Title ?? ident}";
				await ResourceLoader.LoadAllGameResourceAsync( activePackage.FileSystem, token, sourcePackage: activePackage.Package );
				FontManager.Instance.LoadAll( activePackage.FileSystem );
				resourcesLoaded = true;
			}
			finally
			{
				_resourceLoadSem.Release();
			}
		}
	}

	static readonly SemaphoreSlim _resourceLoadSem = new( 1, 1 );

	static CaseInsensitiveDictionary<PackageDownload> Downloads;

	CaseInsensitiveDictionary<int> _refcounts;

	internal ServerPackages()
	{
		Current = this;

		StringTable = new StringTable( "ServerPackages", true );

		Clear();
	}

	internal void Clear()
	{
		StringTable.Reset();
		Downloads = new();
		_refcounts = new();
	}

	/// <summary>
	/// Adopts the previous host's requirements and completes packages whose resources were still pending.
	/// </summary>
	internal async Task OnBecameHost()
	{
		var entries = StringTable.Entries.Values.ToArray();

		// Refcounts are host-local. Keep one reference to each inherited, completed requirement.
		foreach ( var entry in entries )
		{
			if ( entry.Read<PackageInfo>().ResourcesReady )
				_refcounts.TryAdd( entry.Name, 1 );
		}

		foreach ( var entry in entries )
		{
			if ( entry.Read<PackageInfo>().ResourcesReady )
				continue;

			try
			{
				// The normal host install adopts the requirement and publishes scripts before readiness.
				// It also reuses downloads already started as a client, and removes failed requirements.
				await DownloadAndMount( entry.Name );
			}
			catch ( Exception exception )
			{
				Log.Warning( exception, $"Failed to finish inherited server package: {entry.Name}" );
			}
		}
	}

	[ConCmd( "list_serverpackages", Help = "Prints a summary of currently required server packages." )]
	internal static void ListAll()
	{
		if ( Current == null )
		{
			Log.Info( "Not in a game." );
			return;
		}

		Log.Info( $"{Current.StringTable.Entries.Count} required Server Package(s):" );
		foreach ( var kvp in Current._refcounts )
		{
			Log.Info( $"\t{kvp.Key} (x{kvp.Value})" );
		}
	}

	internal async Task InstallAll()
	{
		// Conna: let's make a copy here incase the entries table is modified during
		// the installation. This could happen if a new package is added from
		// the host while we're installing.
		var entries = StringTable.Entries.ToDictionary();

		Log.Info( $"Installing {entries.Count} server packages.." );

		var sw = System.Diagnostics.Stopwatch.StartNew();

		// What's coming down, all of it sized up before the first byte - so the loading screen can show
		// it as one bar, room made for each, rather than one download after another
		if ( entries.Count > 0 )
		{
			LoadingScreen.Title = "Checking Packages";
			await LoadingScreen.ReserveDownloads( entries.Keys );

			LoadingScreen.Title = "Installing Packages";
		}

		// Install all packages without triggering a resource reload per-package,
		// then do a single reload at the end.
		await entries.ForEachTaskAsync( async p =>
		{
			await ClientInstallPackage( p.Value, reloadResources: false );
		} );

		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();

		Log.Info( $"Installation Complete ({sw.Elapsed.TotalSeconds:0.00}s)" );
	}

	/// <summary>
	/// Waits for required packages already being installed by table updates, without reinstalling or reloading them.
	/// </summary>
	internal async Task WaitForDownloads()
	{
		while ( StringTable.Entries.Values.Any( entry => Get( entry.Name )?.IsDownloading == true ) )
		{
			await Task.Delay( 20 );
		}
	}

	internal async Task ClientInstallPackage( StringTable.Entry entry, bool reloadResources = true )
	{
		string ident = entry.Name;
		if ( ident.StartsWith( "local." ) )
			return;

		Log.Info( $"Installing server package: {ident}" );
		await DownloadAndMount( ident, reloadResources: reloadResources );
	}

	/// <summary>
	/// Deserializes packages deferred during the initial handshake, after the script snapshot is published.
	/// </summary>
	internal async Task LoadResources()
	{
		foreach ( var entry in StringTable.Entries.Values.ToArray() )
		{
			if ( entry.Read<PackageInfo>().ResourcesReady && Get( entry.Name ) is { } download )
			{
				await download.LoadResources();
			}
		}
	}

	/// <summary>
	/// The workshop items the server has a different version of than we've installed, like Garry's Mod's map check. We
	/// can't join with those - one of us has an older version, and the workshop only has the latest.
	/// </summary>
	internal List<Workshop.Item> GetMismatchedWorkshopItems()
	{
		var mismatched = new List<Workshop.Item>();

		foreach ( var entry in StringTable.Entries.Values )
		{
			if ( Get( entry.Name )?.package is not { InstallsFromWorkshop: true } package )
				continue;

			// Each package's entry is the version of its workshop item the server has
			var server = entry.Read<PackageInfo>().WorkshopVersion;
			var ours = Workshop.GetInstalledVersion( package.WorkshopId );

			if ( server != 0 && ours != 0 && server != ours )
				mismatched.Add( new Workshop.Item { Id = package.WorkshopId, Title = package.Title } );
		}

		return mismatched;
	}

	/// <summary>
	/// Why we can't join a server whose versions of these workshop items are different to ours
	/// </summary>
	internal static string MismatchMessage( IEnumerable<Workshop.Item> items )
	{
		var names = items.Select( x => x.Title ?? $"Workshop item {x.Id}" );
		return $"You can't join this server because its version of {string.Join( ", ", names )} is different from yours. One of you has an older version - the server needs to update, or yours does.";
	}

	/// <inheritdoc cref="AddRequirement(string, bool)"/>
	internal void AddRequirement( Package package )
		=> AddRequirement( package.GetIdent( false, true ) );

	/// <summary>
	/// Add a reference to a package requirement.
	/// </summary>
	internal void AddRequirement( string packageIdent, bool resourcesReady = true )
	{
		if ( _refcounts.TryGetValue( packageIdent, out var count ) )
		{
			_refcounts[packageIdent] = count + 1;
		}
		else
		{
			_refcounts[packageIdent] = 1;
			StringTable.Set( packageIdent, new PackageInfo { ResourcesReady = resourcesReady } );
		}
	}

	/// <inheritdoc cref="RemoveRequirement(string)"/>
	internal void RemoveRequirement( Package package )
		=> RemoveRequirement( package.GetIdent( false, true ) );

	/// <summary>
	/// Remove a reference to a package requirement.
	/// When all references are removed, the package requirement is removed so new clients don't need to download it.
	/// </summary>
	/// <param name="packageIdent">Ident of the package, needs to match the ident format the requirement was added with :(</param>
	internal void RemoveRequirement( string packageIdent )
	{
		if ( !_refcounts.TryGetValue( packageIdent, out var count ) )
			return;

		if ( count <= 1 )
		{
			_refcounts.Remove( packageIdent );
			StringTable.Remove( packageIdent );
		}
		else
		{
			_refcounts[packageIdent] = count - 1;
		}
	}

	/// <summary>
	/// Install a package if it's not already installed.
	/// If we're the host of a game, we'll register it as a requirement so clients will also install it.
	/// </summary>
	internal static async ValueTask<BaseFileSystem> DownloadAndMount( string packageIdent, CancellationToken token = default, bool reloadResources = true )
	{
		ThreadSafe.AssertIsMainThread();

		// A client download may finish after promotion. Only the host operation that adopted
		// this requirement can release its resources, after loading and publishing its scripts.
		var owner = Networking.IsHost ? Current : null;
		owner?.AddRequirement( packageIdent, resourcesReady: false );

		bool completed = false;
		try
		{
			if ( !Downloads.TryGetValue( packageIdent, out var dl ) )
			{
				dl = new();
				dl.ident = packageIdent;

				Downloads[packageIdent] = dl;
			}

			var fs = await dl.DownloadAndMount( token, reloadResources );
			if ( fs is null )
			{
				return null;
			}

			if ( reloadResources )
			{
				await dl.LoadResources( token );
			}

			// Which version of its workshop item we've got, so clients can tell if theirs is the same
			if ( owner is not null )
			{
				var version = dl.package is { InstallsFromWorkshop: true } package ? Workshop.GetInstalledVersion( package.WorkshopId ) : 0;
				owner.SetPackageReady( packageIdent, version );
			}

			completed = true;
			return fs;
		}
		finally
		{
			// Release only this caller's reference, including cancellation during a deferred resource scan.
			if ( !completed )
			{
				owner?.RemoveRequirement( packageIdent );
			}
		}
	}

	/// <summary>
	/// Releases deferred resources after the package's scripts have been sent, and records its workshop version.
	/// </summary>
	void SetPackageReady( string packageIdent, long version )
	{
		if ( _refcounts.ContainsKey( packageIdent ) )
			StringTable.Set( packageIdent, new PackageInfo { WorkshopVersion = version, ResourcesReady = true } );
	}

	internal static PackageDownload Get( string packageIdent )
	{
		if ( !Downloads.TryGetValue( packageIdent, out var dl ) )
			return null;

		return dl;
	}
}

internal class UpdateLoadingScreen : ILoadingInterface
{
	public void Dispose()
	{
		LoadingScreen.Subtitle = "";
		LoadingScreen.Progress = null;
	}

	public void LoadingProgress( LoadingProgress progress )
	{
		LoadingScreen.Progress = progress;
		LoadingScreen.Title = $"{progress.Title}";
		LoadingScreen.Subtitle = progress.Mbps > 0
			? $"{progress.Percent:n0}% • {progress.Mbps:n0}mbps • {progress.CalculateETA().ToRemainingTimeString()}"
			: "";
	}
}
