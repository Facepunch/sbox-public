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

	internal record struct ServerPackageInfo();
	internal StringTable StringTable;

	internal class PackageDownload
	{
		public string ident;
		public Package package;
		public bool IsDownloading;
		public bool IsMounted;
		public bool IsErrored;
		public PackageManager.ActivePackage activePackage;

		internal async ValueTask<BaseFileSystem> DownloadAndMount( CancellationToken token, bool reloadResources = true )
		{
			// Already downloaded
			if ( activePackage != null )
				return activePackage.FileSystem;

			// Downloading right now in another task
			if ( IsDownloading )
			{
				while ( IsDownloading ) await Task.Delay( 20 );
				return activePackage?.FileSystem;
			}

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

				if ( activePackage?.FileSystem is not null )
				{
					LoadingScreen.Title = $"Loading {package?.Title ?? ident}";

					await ServerPackages._resourceLoadSem.WaitAsync( token );
					try
					{
						await ResourceLoader.LoadAllGameResourceAsync( activePackage.FileSystem, token );
						FontManager.Instance.LoadAll( activePackage.FileSystem );
					}
					finally
					{
						ServerPackages._resourceLoadSem.Release();
					}
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
	}

	static readonly SemaphoreSlim _resourceLoadSem = new( 1, 1 );

	static CaseInsensitiveDictionary<PackageDownload> Downloads;

	CaseInsensitiveDictionary<int> _refcounts;

	internal ServerPackages()
	{
		Current = this;

		StringTable = new StringTable( "ServerPackages", true );
		StringTable.OnChangeOrAdd += ( e ) => _ = ClientInstallPackage( e );

		Clear();
	}

	internal void Clear()
	{
		StringTable.Reset();
		Downloads = new();
		_refcounts = new();
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

		if ( entries.Count > 0 )
			LoadingScreen.Title = "Installing Packages";

		var sw = System.Diagnostics.Stopwatch.StartNew();

		// Install all packages without triggering a resource reload per-package,
		// then do a single reload at the end.
		await entries.ForEachTaskAsync( async p =>
		{
			await ClientInstallPackage( p.Value, reloadResources: false );
		} );

		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();

		Log.Info( $"Installation Complete ({sw.Elapsed.TotalSeconds:0.00}s)" );
	}

	internal async Task ClientInstallPackage( StringTable.Entry entry, bool reloadResources = true )
	{
		string ident = entry.Name;
		if ( ident.StartsWith( "local." ) )
			return;

		Log.Info( $"Installing server package: {ident}" );
		ServerPackageInfo packageInfo = entry.Read<ServerPackageInfo>();
		await DownloadAndMount( ident, reloadResources: reloadResources );
	}

	/// <inheritdoc cref="AddRequirement(string, ServerPackageInfo)"/>
	internal void AddRequirement( Package package, ServerPackageInfo info = default )
		=> AddRequirement( package.GetIdent( false, true ), info );

	/// <summary>
	/// Add a reference to a package requirement.
	/// </summary>
	internal void AddRequirement( string packageIdent, ServerPackageInfo info = default )
	{
		if ( _refcounts.TryGetValue( packageIdent, out var count ) )
		{
			_refcounts[packageIdent] = count + 1;
		}
		else
		{
			_refcounts[packageIdent] = 1;
			StringTable.Set( packageIdent, info );
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

		if ( Networking.IsHost )
		{
			Current?.AddRequirement( packageIdent, new ServerPackageInfo() );
		}

		if ( Downloads.TryGetValue( packageIdent, out var dl ) )
			return await dl.DownloadAndMount( token, reloadResources );

		dl = new();
		dl.ident = packageIdent;

		Downloads[packageIdent] = dl;

		return await dl.DownloadAndMount( token, reloadResources );
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
	}

	public void LoadingProgress( LoadingProgress progress )
	{
		LoadingScreen.Title = $"{progress.Title}";
		LoadingScreen.Subtitle = progress.Mbps > 0
			? $"{progress.Percent:n0}% • {progress.Mbps:n0}mbps • {progress.CalculateETA().ToRemainingTimeString()}"
			: "";
	}
}
