using System.Text.RegularExpressions;

namespace Sandbox;

/// <summary>
/// Watch folders, dispatch events on changed files
/// </summary>
[SkipHotload]
public sealed class FileWatch : IDisposable
{
	/// <summary>
	/// Bit of a hack until we can do better. Don't trigger any watchers until this time.
	/// </summary>
	internal static float SuppressWatchers { get; set; }

	private static Logger log = new Logger( "FileWatch" );
	internal static List<BaseFileSystem> WithChanges = new List<BaseFileSystem>();

	BaseFileSystem system;

	public bool Enabled { get; set; }
	public List<string> Changes { get; private set; }

	private Regex regexTest;
	public List<string> watchFiles;


	internal FileWatch( BaseFileSystem system, string path )
	{
		this.system = system;
		Enabled = true;

		var pattern = Regex.Escape( path.ToLower() ).Replace( @"\*", ".*" ).Replace( @"\?", "." );
		regexTest = new Regex( $"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled );
	}

	internal FileWatch( BaseFileSystem system )
	{
		this.system = system;
		Enabled = true;
	}

	public void Dispose()
	{
		OnChanges = default;
		OnChangedFile = default;

		system?.RemoveWatcher( this );
		system = null;
	}

	private bool InterestedInFile( string file )
	{
		// watchers
		if ( watchFiles != null && !watchFiles.Any( x => string.Equals( file, x, StringComparison.OrdinalIgnoreCase ) ) )
			return false;

		// Regex test
		if ( regexTest != null && !regexTest.IsMatch( file ) )
			return false;

		// Default is interested
		return true;
	}

	void TriggerCallback( List<string> value )
	{
		if ( !Enabled ) return;

		if ( Changes == null )
			Changes = new List<string>();

		Changes.Clear();

		foreach ( var change in value )
		{
			if ( !InterestedInFile( change ) ) continue;

			Changes.Add( change );
		}

		if ( Changes.Count == 0 )
			return;

		try
		{
			//log.Trace( $"FileWatch.TriggerCallback ({Path})" );
			OnChanges?.Invoke( this );

			foreach ( var change in Changes )
			{
				OnChangedFile?.Invoke( change );
			}
		}
		catch ( System.Exception e )
		{
			log.Error( e );
		}
	}

	/// <summary>
	/// Called once per batch of files changed
	/// </summary>
	public event Action<FileWatch> OnChanges;

	/// <summary>
	/// Called for each file changed
	/// </summary>
	public event Action<string> OnChangedFile;
	internal static RealTimeSince TimeSinceLastChange;

	/// <summary>
	/// This is used for unit tests, to assure that a change is detected
	/// </summary>
	internal static async Task<bool> TickUntilFileChanged( string wildcard )
	{
		var sw = Stopwatch.StartNew();
		while ( sw.Elapsed.TotalSeconds < 2 )
		{
			await Task.Delay( 10 );

			lock ( WithChanges )
			{
				if ( WithChanges.Count > 0 )
				{
					Log.Info( string.Join( "\n", WithChanges.SelectMany( x => x.changedFiles ) ) );

					if ( WithChanges.Any( x => x.changedFiles.Any( x => x.WildcardMatch( wildcard ) ) ) )
					{
						// do the real tick to send the messages
						TimeSinceLastChange = 1;
						Tick();
						return true;
					}
				}
			}
		}

		return false;
	}


	// TODO - move this into BaseFileSystem
	public static void Tick()
	{
		Dictionary<BaseFileSystem, List<string>> changes = null;

		if ( TimeSinceLastChange < 0.1f ) return;

		// Don't lock and loop WithChanges 
		// incase a callback triggers a changed file
		// and we end up deadlocked
		lock ( WithChanges )
		{
			//
			// Hack, we sometimes want to suppress this hotload for a number of seconds
			// This blanket suppression is maybe not the best way, could do it via wildcards or something
			//
			if ( SuppressWatchers > RealTime.Now )
			{
				WithChanges.Clear();
				return;
			}


			if ( WithChanges.Count == 0 ) return;

			WithChanges.RemoveAll( x => !x.IsValid || x.changedFiles == null );

			changes = WithChanges.ToDictionary( x => x, x => x.changedFiles.ToList() );

			foreach ( var fs in WithChanges )
			{
				fs.changedFiles.Clear();
			}

			WithChanges.Clear();
		}

		if ( changes == null )
			return;

		foreach ( var filesystem in changes )
		{
			foreach ( var watcher in filesystem.Key.watchers.ToArray() )
			{
				watcher.TriggerCallback( filesystem.Value );

				if ( filesystem.Key.PendingDispose )
					filesystem.Key.Dispose();
			}
		}
	}

	internal void AddFile( string file )
	{
		watchFiles ??= new List<string>();
		if ( !watchFiles.Contains( file ) )
			watchFiles.Add( file );
	}
}
