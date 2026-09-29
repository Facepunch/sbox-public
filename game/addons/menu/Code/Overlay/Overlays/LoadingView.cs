using Sandbox;

namespace MenuProject;

/// <summary>
/// What the loading screen shows - the real <see cref="LoadingScreen"/>, or a made up load of a real
/// game while <c>loading_mock</c> is running, so it can be looked at without actually loading
/// anything. The mock never touches the engine's loading state; only the overlay sees it.
/// </summary>
public static class LoadingView
{
	static Package _mockPackage;
	static RealTimeSince _mockStarted;

	/// <summary>
	/// Showing a made up load.
	/// </summary>
	public static bool IsMocking => _mockPackage is not null;

	/// <summary>
	/// <c>loading_mock facepunch.sandbox</c> - put the loading screen up for that game, running
	/// through a pretend load (looking it up, downloading, loading, resources) over and over.
	/// <c>loading_mock off</c>, or Cancel, puts it away.
	/// </summary>
	[MenuConCmd( "loading_mock", Help = "Show the loading screen for a game, running through a made up load: loading_mock <ident>, or loading_mock off" )]
	public static async Task Mock( string ident )
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
			Log.Warning( $"loading_mock: couldn't find a package called {ident}" );
			return;
		}

		_mockPackage = package;
		_mockStarted = 0;
	}

	public static void StopMock() => _mockPackage = null;

	public static bool IsVisible => IsMocking || LoadingScreen.IsVisible;

	public static Package Package => IsMocking ? _mockPackage : LoadingScreen.Package;

	public static string Media => IsMocking ? _mockPackage.LoadingScreen.MediaUrl : LoadingScreen.Media;

	public static string Title => IsMocking ? MockStep.Title : LoadingScreen.Title;

	public static string Subtitle => IsMocking ? MockStep.Subtitle : LoadingScreen.Subtitle;

	public static LoadingProgress? Progress => IsMocking ? MockStep.Progress : LoadingScreen.Progress;

	public static int TaskCount => IsMocking ? 0 : LoadingScreen.Tasks.Count;

	public static string FirstTask => IsMocking ? null : LoadingScreen.Tasks.FirstOrDefault()?.Title;

	/// <summary>
	/// Stop loading - or stop pretending to.
	/// </summary>
	public static void Cancel()
	{
		if ( IsMocking ) StopMock();
		else MenuUtility.CancelLoading();
	}

	//
	// The mock - roughly how a first time load of a game goes, looping
	//

	record struct Step( string Title, string Subtitle, LoadingProgress? Progress );

	// The steps GameInstance really goes through, in order, with what it really puts under the title -
	// the files it's mounting, the assemblies it's compiling, the resources it's loading. Lists tick
	// over every so often like the real thing's do (it only updates the subtitle every few ms of work).
	// {0} is the game's title.
	record struct Phase( string Title, float Duration, string[] Subtitles = null, float SubtitleRate = 0, bool Download = false );

	static readonly Phase[] Phases =
	{
		new( "Fetching Package Info", 0.6f ),
		new( "Downloading '{0}'", 7f, Download: true ),
		new( "Installing {0}", 1.2f, new[] { "manifest.json", ".sbproj", "citizen.vmdl_c", "terrain_albedo.vtex_c", "props_crate.vmat_c", "ambience.vsnd_c" }, 8 ),
		new( "Loading {0}", 1.5f, new[] { "package.base", "package.{1}", "package.{1}.editor" }, 2 ),
		new( "Loading Resources", 3f, new[] { "citizen.vmdl_c", "terrain_albedo.vtex_c", "props_crate.vmat_c", "ambience.vsnd_c", "weapon_pistol.vmdl_c", "skybox_day.vtex_c", "ui_hud.vtex_c", "footsteps_concrete.vsnd_c", "player_controller.prefab", "main.scene" }, 6 ),
		new( "Loading Achievements", 0.4f ),
		new( "Loading Fonts", 0.3f ),
		new( "Loading Scene", 1.6f, new[] { "Generating NavMesh..", "Loading Finished.." }, 1.25f ),
		new( "Starting Game", 1f ),
	};

	const double MockSize = 1_100_000_000;

	static Step MockStep
	{
		get
		{
			var title = _mockPackage.Title;
			var ident = _mockPackage.Ident;
			var t = (float)_mockStarted % Phases.Sum( x => x.Duration );

			foreach ( var phase in Phases )
			{
				if ( t >= phase.Duration )
				{
					t -= phase.Duration;
					continue;
				}

				var name = string.Format( phase.Title, title );

				if ( phase.Download )
				{
					var progress = new LoadingProgress
					{
						Title = name,
						Fraction = t / phase.Duration,
						Mbps = 120 + Math.Sin( t * 1.3 ) * 18,
						TotalSize = MockSize,
					};

					return new( name, null, progress );
				}

				var subtitle = phase.Subtitles is { Length: > 0 } subs
					? string.Format( subs[(int)(t * phase.SubtitleRate) % subs.Length], title, ident )
					: null;

				return new( name, subtitle, null );
			}

			return new( "Starting Game", null, null );
		}
	}
}
