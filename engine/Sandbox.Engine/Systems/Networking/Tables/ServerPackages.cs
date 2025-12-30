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
	public static ServerPackages Current { get; private set; } = new();

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

		internal async ValueTask<BaseFileSystem> DownloadAndMount( CancellationToken token )
		{
			// Already downloaded
			if ( activePackage != null )
			{
				return activePackage.FileSystem;
			}

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
					CancellationToken = token
				};

				activePackage = await PackageManager.InstallAsync( o );

				// Success
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

	static CaseInsensitiveDictionary<PackageDownload> Downloads;

	internal ServerPackages()
	{
		// WTF???
		Current = this;

		StringTable = new StringTable( "ServerPackages", true );
		StringTable.OnChangeOrAdd += ( e ) => _ = ClientInstallPackage( e );

		Clear();
	}

	internal void Clear()
	{
		StringTable.Reset();
		Downloads = new();
	}

	internal async Task InstallAll()
	{
		// Conna: let's make a copy here incase the entries table is modified during
		// the installation. This could happen if a new package is added from
		// the host while we're installing.
		var entries = StringTable.Entries.ToDictionary();

		Log.Info( $"Installing {entries.Count} server packages.." );

		var sw = System.Diagnostics.Stopwatch.StartNew();

		await entries.ForEachTaskAsync( async p =>
		{
			await ClientInstallPackage( p.Value );
		} );

		Log.Info( $"Installation Complete ({sw.Elapsed.TotalSeconds:0.00}s)" );
	}

	internal async Task ClientInstallPackage( StringTable.Entry entry )
	{
		string ident = entry.Name;
		if ( ident.StartsWith( "local." ) )
			return;

		Log.Info( $"Installing server package: {ident}" );
		ServerPackageInfo packageInfo = entry.Read<ServerPackageInfo>();
		await DownloadAndMount( ident );
	}

	internal void AddRequirement( Package package, ServerPackageInfo info = default )
	{
		AddRequirement( package.GetIdent( false, true ), info );
	}

	internal void AddRequirement( string packageIdent, ServerPackageInfo info = default )
	{
		StringTable.Set( packageIdent, info );
	}

	internal async ValueTask<BaseFileSystem> DownloadAndMount( string packageIdent, CancellationToken token = default )
	{
		ThreadSafe.AssertIsMainThread();

		if ( Networking.IsHost )
		{
			AddRequirement( packageIdent, new ServerPackageInfo() );
		}

		if ( Downloads.TryGetValue( packageIdent, out var dl ) )
			return await dl.DownloadAndMount( token );

		dl = new();
		dl.ident = packageIdent;

		Downloads[packageIdent] = dl;

		return await dl.DownloadAndMount( token );
	}

	internal PackageDownload Get( string packageIdent )
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
		LoadingScreen.Title = "";
		LoadingScreen.Subtitle = "";
	}

	public void LoadingProgress( LoadingProgress progress )
	{
		LoadingScreen.Title = $"{progress.Title}";
		LoadingScreen.Subtitle = $"{progress.Percent:n0}% • {progress.Mbps:n0}mbps • {progress.CalculateETA().ToRemainingTimeString()}";
	}
}
