using Sandbox;

namespace MenuProject;

/// <summary>
/// What the loading screen shows - the real <see cref="LoadingScreen"/>, or a made up load of a real
/// game while <c>menu_mock_loading</c> is running, so it can be looked at without actually loading
/// anything. The mock never touches the engine's loading state; only the overlay sees it.
/// </summary>
public static class LoadingView
{
	static Package _mockPackage;
	static RealTimeSince _mockStarted;

	/// <summary>
	/// Real model packages for the mock to pretend it's pulling in - a game's models and materials come
	/// down as packages of their own during a load, one after another.
	/// </summary>
	static List<Package> _mockAssets = new();

	/// <summary>
	/// Pretending to join a server rather than host - the server's own packages come as a second batch
	/// once the game's loaded.
	/// </summary>
	static bool _mockJoin;

	/// <summary>
	/// Showing a made up load.
	/// </summary>
	public static bool IsMocking => _mockPackage is not null;

	/// <summary>
	/// <c>menu_mock_loading facepunch.sandbox</c> - put the loading screen up for that game, running
	/// through a pretend load over and over, the way a real one goes: looking it up, downloading it, then
	/// the small packages it references (real trending models) as a batch - all on one bar, sized up from
	/// the start - loading it, then its resources. Add <c>join</c> for joining a server: the same, then once the game's loaded the server's
	/// own few packages sized up and installed as a second batch on the same bar.
	/// <c>menu_mock_loading off</c>, or Cancel, puts it away.
	/// </summary>
	[MenuConCmd( "menu_mock_loading", Help = "Show the loading screen for a game, running through a made up load: menu_mock_loading <ident> [join], or menu_mock_loading off" )]
	public static async Task Mock( string ident, string mode = "" )
	{
		if ( string.IsNullOrWhiteSpace( ident ) || ident == "off" )
		{
			StopMock();
			return;
		}

		// In full - that's what a real load fetches, and the latest news only comes with it
		var package = await Package.FetchAsync( ident, false );
		if ( package is null )
		{
			Log.Warning( $"menu_mock_loading: couldn't find a package called {ident}" );
			return;
		}

		// Some small packages to fetch on the way - whatever models are trending, real names and all
		var assets = await Package.FindAsync( "type:model sort:trending", MockAssetCount );
		_mockAssets = assets?.Packages?.ToList() ?? new();

		_mockJoin = string.Equals( mode, "join", StringComparison.OrdinalIgnoreCase );
		_mockPackage = package;
		_mockStarted = 0;
	}

	public static void StopMock() => _mockPackage = null;

	public static bool IsVisible => IsMocking || LoadingScreen.IsVisible;

	public static Package Package => IsMocking ? _mockPackage : LoadingScreen.Package;

	public static string Media => IsMocking ? _mockPackage.LoadingScreen.MediaUrl : LoadingScreen.Media;

	public static string Title => IsMocking ? MockStep.Title : LoadingScreen.Title;

	public static string Subtitle => IsMocking ? MockStep.Subtitle : LoadingScreen.Subtitle;

	public static int TaskCount => IsMocking ? 0 : LoadingScreen.Tasks.Count;

	public static string FirstTask => IsMocking ? null : LoadingScreen.Tasks.FirstOrDefault()?.Title;

	/// <summary>
	/// One package coming down for the load - its share of the bar.
	/// </summary>
	/// <param name="Ident">Its full ident - null when all that's known is a download's title.</param>
	/// <param name="Size">What it's fetching, in bytes - 0 when that isn't known.</param>
	/// <param name="Fraction">How far through it is, 0 to 1.</param>
	public readonly record struct Download( string Ident, string Title, double Size, double Fraction, double Mbps, bool Complete );

	/// <summary>
	/// Everything coming down for the load, for one bar across all of it - each package with something
	/// to fetch (one that's all cached has nothing to take up room with). The engine's list of the load's
	/// downloads; failing that, the one download it's reporting on its own (a party's preload).
	/// </summary>
	public static IReadOnlyList<Download> Downloads
	{
		get
		{
			if ( IsMocking ) return MockDownloads;

			var downloads = LoadingScreen.Downloads
				.Where( x => x.TotalSize > 0 )
				.Select( x => new Download( x.Ident, x.Title, x.TotalSize, x.Fraction, x.Mbps, x.IsComplete ) )
				.ToList();

			if ( downloads.Count == 0 && LoadingScreen.Progress is { Fraction: > 0 and < 1 } p )
				downloads.Add( new Download( null, NameIn( p.Title ), p.TotalSize, p.Fraction, p.Mbps, false ) );

			return downloads;
		}
	}

	/// <summary>
	/// What a download's title says it's fetching - "Sandbox" out of "Downloading 'Sandbox'".
	/// </summary>
	static string NameIn( string title ) => System.Text.RegularExpressions.Regex.Match( title ?? "", "'([^']+)'" ) is { Success: true } m ? m.Groups[1].Value : title;

	/// <summary>
	/// Stop loading - or stop pretending to.
	/// </summary>
	public static void Cancel()
	{
		if ( IsMocking ) StopMock();
		else MenuUtility.CancelLoading();
	}

	//
	// The mock - how a first time load of a game really goes, looping
	//

	record struct Step( string Title, string Subtitle );

	// The steps GameInstance really goes through, in order, with what it really puts under the title -
	// the files it's mounting, the assemblies it's compiling, the resources it's loading. Lists tick
	// over every so often like the real thing's do (it only updates the subtitle every few ms of work).
	// {0} is the game's title. JoinOnly ones are what joining a server adds - its network tables read once
	// the game's loaded, then its own packages sized up and installed (ServerPackages.InstallAll).
	record struct Phase( string Title, float Duration, string[] Subtitles = null, float SubtitleRate = 0, bool Download = false, bool References = false, bool Extras = false, bool JoinOnly = false );

	static readonly Phase[] Phases =
	{
		new( "Fetching Package Info", 0.6f ),
		new( "Downloading '{0}'", 7f, Download: true ),
		new( "Installing {0}", 0, References: true ),
		new( "Installing {0}", 1.2f, new[] { "manifest.json", ".sbproj", "citizen.vmdl_c", "terrain_albedo.vtex_c", "props_crate.vmat_c", "ambience.vsnd_c" }, 8 ),
		new( "Loading {0}", 1.5f, new[] { "package.base", "package.{1}", "package.{1}.editor" }, 2 ),
		new( "Checking Packages", 0.6f, JoinOnly: true ),
		new( "Installing Packages", 0, Extras: true, JoinOnly: true ),
		new( "Loading Resources", 3f, new[] { "citizen.vmdl_c", "terrain_albedo.vtex_c", "props_crate.vmat_c", "ambience.vsnd_c", "weapon_pistol.vmdl_c", "skybox_day.vtex_c", "ui_hud.vtex_c", "footsteps_concrete.vsnd_c", "player_controller.prefab", "main.scene" }, 6 ),
		new( "Loading Achievements", 0.4f ),
		new( "Loading Fonts", 0.3f ),
		new( "Loading Scene", 1.6f, new[] { "Generating NavMesh..", "Loading Finished.." }, 1.25f ),
		new( "Starting Game", 1f ),
	};

	const double MockSize = 1_100_000_000;

	//
	// The small packages - real trending models, a few coming down at once like the real thing's parallel
	// downloads, each a few megabytes. Most are the game's references: sized up with it at the start, on
	// the bar from the first frame, and coming down as a batch once the game's own download is done,
	// hosting or joining. Joining, the last few are the server's own - what the host added on top - sized
	// up and installed once the game's loaded, a second batch on the same bar
	//

	const int MockAssetCount = 16;

	/// <summary>
	/// Joining, how many of the small packages are the server's own rather than the game's.
	/// </summary>
	const int MockServerExtras = 5;

	/// <summary>
	/// How many come down at once.
	/// </summary>
	const int MockAssetLanes = 3;

	/// <summary>
	/// A steady made up number from 0 to 1 for the nth small package - the same every time round.
	/// </summary>
	static float Roll( int n, int salt ) => ((n * 9301 + salt * 49297 + 233) % 233280) / 233280f;

	/// <summary>
	/// How long the nth small package takes to come down - a third of a second to a second and a bit.
	/// </summary>
	static float AssetTime( int n ) => 0.3f + Roll( n, 1 ) * 0.9f;

	/// <summary>
	/// How big it is - half a megabyte to thirty.
	/// </summary>
	static double AssetSize( int n ) => 500_000 + Roll( n, 2 ) * 29_500_000;

	/// <summary>
	/// The game's references - all of the small packages hosting, all but the server's own joining.
	/// </summary>
	static int[] References => Enumerable.Range( 0, _mockJoin ? Math.Max( 0, _mockAssets.Count - MockServerExtras ) : _mockAssets.Count ).ToArray();

	/// <summary>
	/// The server's own - none hosting.
	/// </summary>
	static int[] ServerExtras => Enumerable.Range( References.Length, _mockAssets.Count - References.Length ).ToArray();

	/// <summary>
	/// When the nth small package starts, into its batch - after the ones before it in its lane.
	/// </summary>
	static float StartIn( int[] batch, int n ) => batch.TakeWhile( x => x != n ).Where( x => x % MockAssetLanes == n % MockAssetLanes ).Sum( AssetTime );

	static float DurationOf( int[] batch ) => batch.Length == 0 ? 0 : batch.Max( x => StartIn( batch, x ) + AssetTime( x ) ) + 0.4f;

	static float DurationOf( Phase phase )
	{
		if ( phase.JoinOnly && !_mockJoin ) return 0;
		if ( phase.References ) return DurationOf( References );
		return phase.Extras ? DurationOf( ServerExtras ) : phase.Duration;
	}

	/// <summary>
	/// How far through the lap the mock is, in seconds.
	/// </summary>
	static float LapTime => (float)_mockStarted % Phases.Sum( DurationOf );

	/// <summary>
	/// When a phase starts, into the lap.
	/// </summary>
	static float StartOf( Func<Phase, bool> which ) => Phases.TakeWhile( x => !which( x ) ).Sum( DurationOf );

	static Download SmallDownload( int i, float at )
	{
		var time = AssetTime( i );
		var going = at > 0 && at < time;

		return new Download( _mockAssets[i].FullIdent, _mockAssets[i].Title, AssetSize( i ), Math.Clamp( at / time, 0, 1 ), going ? 30 + Roll( i, 3 ) * 40 : 0, at >= time );
	}

	/// <summary>
	/// The mock's downloads at this point in the lap, as the engine keeps them for a real load - the game
	/// and everything it references, sized up at the start; the game coming down on its own, then its
	/// references as a batch; then, joining, the server's own added once they're sized up, the earlier
	/// ones staying on, done.
	/// </summary>
	static IReadOnlyList<Download> MockDownloads
	{
		get
		{
			var t = LapTime;
			var list = new List<Download>();

			var game = Phases.First( x => x.Download );
			var gameAt = t - StartOf( x => x.Download );
			list.Add( new Download( _mockPackage.FullIdent, _mockPackage.Title, MockSize, Math.Clamp( gameAt / game.Duration, 0, 1 ), gameAt > 0 && gameAt < game.Duration ? 120 + Math.Sin( gameAt * 1.3 ) * 18 : 0, gameAt >= game.Duration ) );

			var references = References;
			var referencesAt = t - StartOf( x => x.References );
			foreach ( var i in references )
				list.Add( SmallDownload( i, referencesAt - StartIn( references, i ) ) );

			var extrasAt = t - StartOf( x => x.Extras );
			if ( !_mockJoin || extrasAt < 0 ) return list;

			var extras = ServerExtras;
			foreach ( var i in extras )
				list.Add( SmallDownload( i, extrasAt - StartIn( extras, i ) ) );

			return list;
		}
	}

	static Step MockStep
	{
		get
		{
			var title = _mockPackage.Title;
			var ident = _mockPackage.Ident;
			var t = LapTime;

			foreach ( var phase in Phases )
			{
				var duration = DurationOf( phase );

				if ( t >= duration )
				{
					t -= duration;
					continue;
				}

				var subtitle = phase.Subtitles is { Length: > 0 } subs
					? string.Format( subs[(int)(t * phase.SubtitleRate) % subs.Length], title, ident )
					: null;

				return new( string.Format( phase.Title, title ), subtitle );
			}

			return new( "Starting Game", null );
		}
	}
}
