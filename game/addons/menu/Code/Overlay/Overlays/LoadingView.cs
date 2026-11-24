using Sandbox;

namespace MenuProject;

/// <summary>
/// Supplies the engine's loading status and download progress to the menu.
/// </summary>
public static class LoadingView
{
	/// <summary>
	/// Whether the engine is showing the loading screen.
	/// </summary>
	public static bool IsVisible => LoadingScreen.IsVisible;

	/// <summary>
	/// The package being loaded.
	/// </summary>
	public static Package Package => LoadingScreen.Package;

	/// <summary>
	/// The media selected for the loading screen.
	/// </summary>
	public static string Media => LoadingScreen.Media;

	/// <summary>
	/// The title of the current loading step.
	/// </summary>
	public static string Title => LoadingScreen.Title;

	/// <summary>
	/// Additional detail about the current step.
	/// </summary>
	public static string Subtitle => LoadingScreen.Subtitle;

	/// <summary>
	/// The number of active loading tasks.
	/// </summary>
	public static int TaskCount => LoadingScreen.Tasks.Count;

	/// <summary>
	/// How far through the step in the title it is, 0 to 1, when it isn't a download - updating a
	/// package's shaders. Null when the step can't say.
	/// </summary>
	public static double? StepFraction => LoadingScreen.StepFraction;

	/// <summary>
	/// How far the whole load is across its stages, 0 to 1. Null when no game is loading.
	/// </summary>
	public static double? LoadFraction => LoadingScreen.LoadFraction;

	/// <summary>
	/// The title of the first active loading task, if any.
	/// </summary>
	public static string FirstTask => LoadingScreen.Tasks.FirstOrDefault()?.Title;

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
			// Polled every frame by the overlay, nothing to build while nothing is loading
			if ( LoadingScreen.Downloads.Count == 0 && LoadingScreen.Progress is not { Fraction: > 0 and < 1 } )
				return [];

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
	/// Cancels the current load.
	/// </summary>
	public static void Cancel() => MenuUtility.CancelLoading();
}
