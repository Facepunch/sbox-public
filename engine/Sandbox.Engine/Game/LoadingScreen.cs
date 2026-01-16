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
		}
	}

	/// <summary>
	/// A title to show
	/// </summary>
	public static string Title { get; set; } = "Loading..";

	/// <summary>
	/// A subtitle to show
	/// </summary>
	public static string Subtitle { get; set; } = "";

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
