namespace Sandbox;

/// <summary>
/// Holds metadata and raw data relating to a Saved Game.
/// </summary>
public static class LoadingScreen
{
	private static bool _loading;

	public static bool IsVisible
	{
		get => _loading;
		set
		{
			if ( _loading == value )
				return;

			//Log.Info( $"Loading: {value}\n{new StackTrace( true ).ToString()}" );

			_loading = value;

			// Each load's downloads are its own
			_downloads.Clear();

			if ( !value )
			{
				Progress = null;
				Package = null;
			}
		}
	}

	/// <summary>
	/// The game being loaded, once it's been looked up - so the loading screen can show its name, its
	/// art and what's new in it. Null before then, and when what's loading isn't a game.
	/// Cleared when the loading screen is hidden.
	/// </summary>
	public static Package Package { get; internal set; }

	/// <summary>
	/// A title to show
	/// </summary>
	public static string Title { get; set; } = "Loading..";

	/// <summary>
	/// A subtitle to show
	/// </summary>
	public static string Subtitle { get; set; } = "";

	/// <summary>
	/// A snapshot of the current download's progress, or null when progress is unavailable.
	/// Cleared when the loading screen is hidden.
	/// </summary>
	public static Menu.LoadingProgress? Progress { get; internal set; }

	/// <summary>
	/// Every package downloaded for the load under way, in the order they became known - for one bar
	/// across all of them, rather than <see cref="Progress"/>'s one download at a time. Packages known in
	/// advance (a server's, the game and its map) are added with their sizes before anything starts
	/// downloading; anything found later is added as it starts. Cleared when the loading screen's shown
	/// or hidden.
	/// </summary>
	public static IReadOnlyList<Menu.LoadingDownload> Downloads => _downloads;

	static readonly List<Menu.LoadingDownload> _downloads = new();

	/// <summary>
	/// The download for a package, added if it isn't there yet. Null while the loading screen's hidden -
	/// a running game downloading things isn't a load. Main thread only, like everything that touches the
	/// list - downloads update it from their main thread loop, the loading screen reads it each frame.
	/// </summary>
	internal static Menu.LoadingDownload TrackDownload( string ident, string title )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsVisible || string.IsNullOrEmpty( ident ) )
			return null;

		var download = _downloads.FirstOrDefault( x => string.Equals( x.Ident, ident, StringComparison.OrdinalIgnoreCase ) );
		if ( download is null )
		{
			download = new Menu.LoadingDownload { Ident = ident, Title = title };
			_downloads.Add( download );
		}
		else if ( !string.IsNullOrEmpty( title ) )
		{
			download.Title = title;
		}

		return download;
	}

	/// <summary>
	/// Make room for packages about to be downloaded - each looked up and checked against the download
	/// cache, all at once, so <see cref="Downloads"/> knows the whole of it before the first byte comes
	/// down. Ones that aren't packages, or are local, are skipped. With <paramref name="withReferences"/>,
	/// everything they reference too, and everything that references - what installing them pulls in
	/// (a game's libraries and the cloud assets it uses), less anything already installed. Package info
	/// and manifests only; nothing's downloaded.
	/// </summary>
	internal static async Task ReserveDownloads( IEnumerable<string> idents, System.Threading.CancellationToken token = default, bool withReferences = false )
	{
		if ( !IsVisible ) return;

		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		bool Unseen( string ident ) { lock ( seen ) return seen.Add( ident ); }

		static bool IsPackage( string ident ) => !string.IsNullOrWhiteSpace( ident ) && !ident.StartsWith( "local.", StringComparison.OrdinalIgnoreCase );

		async Task Reserve( string ident )
		{
			try
			{
				var package = await Package.Fetch( ident, false );
				if ( package is null ) return;

				// What it pulls in, sized alongside it rather than after
				var references = withReferences
					? package.EnumerateInstallDependencies()
						.Where( x => IsPackage( x ) && PackageManager.Find( x, false ) is null && Unseen( x ) )
						.Select( Reserve )
						.ToList()
					: new List<Task>();

				if ( package.IsRemote )
				{
					var size = await package.GetDownloadSizeAsync( token: token );

					var download = size < 0 ? null : TrackDownload( package.FullIdent, package.Title );
					if ( download is not null && !download.IsComplete && !download.IsDownloading )
					{
						download.TotalSize = size;
						download.IsComplete = size == 0;
					}
				}

				await Task.WhenAll( references );
			}
			catch ( OperationCanceledException ) { }
			catch ( Exception e )
			{
				Log.Warning( e, $"Couldn't size up {ident}'s download: {e.Message}" );
			}
		}

		await Task.WhenAll( idents.Where( x => IsPackage( x ) && Unseen( x ) ).Select( Reserve ) );
		token.ThrowIfCancellationRequested();
	}

	/// <summary>
	/// A URL or filepath to show as the background image.
	/// </summary>
	public static string Media { get; set; }

	/// <summary>
	/// A list of tasks that are currently being awaited during loading.
	/// </summary>
	public static List<LoadingContext> Tasks { get; } = [];

	/// <summary>
	/// Called by the scene system to tell us about the loading tasks
	/// </summary>
	internal static void UpdateLoadingTasks( List<LoadingContext> incoming )
	{
		Tasks.Clear();

		if ( incoming.Count > 0 )
		{
			Tasks.AddRange( incoming );
		}
	}

}
