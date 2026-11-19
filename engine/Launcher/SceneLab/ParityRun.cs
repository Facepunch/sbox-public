namespace Sandbox.SceneLab;

/// <summary>
/// <c>-parity</c>: presets captured natively one after another in this process - every preset, or those named
/// (<c>-parity "Heavy Scene;Decals"</c>, semicolons because names have commas) - with a table of results at the end and exit
/// code 1 on any failure. Each preset captures as <c>-capture</c> does: at the capture frame, and again after its change. A
/// preset marked <see cref="SceneLabScene.Isolated"/> (or all, with <c>-isolated</c>) loads fresh, after the render target
/// pool has forgotten the last preset's effect history. Frames go to <c>-parity-out</c> (<c>screenshots/scenelab/parity</c>).
/// <c>-save-native name</c> copies each frame into that folder under it; <c>-compare-native name</c> compares each, pixel for
/// pixel, with the ones saved there - native against itself across a change.
/// </summary>
internal sealed class ParityRun
{
	readonly record struct Job( SceneLabScene Scene );

	// A preset that hasn't finished in this long - a map that never loads - is reported and skipped
	static readonly double TimeoutSeconds = Args.Float( "-parity-timeout", 180 );

	readonly List<Job> jobs = new();
	readonly List<(string Scene, List<string> Results, bool Failed)> results = new();
	readonly string saveNative = Args.String( "-save-native" );
	readonly string compareNative = Args.String( "-compare-native" );
	readonly System.Diagnostics.Stopwatch jobTime = new();
	int index = -1;

	// Frames left with nothing loaded before the next job starts: the render target pool frees what went unused for 8
	// frames (RenderTarget.EndOfFrame), so an isolated pass gets fresh targets, as its own process would, rather than the
	// last pass's effect history
	int idleFrames;
	const int IdleFrames = 10;

	/// <summary>
	/// Where frames go.
	/// </summary>
	public string Folder { get; }

	public ParityRun( string sceneNames )
	{
		Folder = System.IO.Path.GetFullPath( Args.String( "-parity-out" ) ?? System.IO.Path.Combine( SceneLabWindow.OutputFolder(), "parity" ) );
		System.IO.Directory.CreateDirectory( Folder );

		var names = string.IsNullOrWhiteSpace( sceneNames ) || sceneNames.StartsWith( '-' ) ? [] : sceneNames.Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
		var found = names.Select( x => (Name: x, Scene: Find( x )) ).ToArray();
		var presets = names.Length == 0 ? SceneLabScene.Presets : found.Where( x => x.Scene is not null ).Select( x => x.Scene ).ToArray();

		// A misspelt or renamed preset fails the run, rather than quietly comparing less
		foreach ( var (name, _) in found.Where( x => x.Scene is null ) ) results.Add( (name, new List<string> { "no such preset" }, true) );

		foreach ( var scene in presets ) jobs.Add( new Job( scene ) );
	}

	static SceneLabScene Find( string name )
	{
		var scene = SceneLabScene.Presets.FirstOrDefault( x => x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		if ( scene is null ) Log.Warning( $"Parity: no scene called \"{name}\"" );
		return scene;
	}

	static bool IsolatedScene( SceneLabScene scene ) => scene.Isolated || Args.Has( "-isolated" );

	Job Current => jobs[index];

	/// <summary>
	/// The capture path for the preset being captured.
	/// </summary>
	public string CapturePath => System.IO.Path.Combine( Folder, $"{FileName( Current.Scene.Name )}.png" );

	/// <summary>
	/// Whether the preset being captured is captured isolated.
	/// </summary>
	public bool Isolated => IsolatedScene( Current.Scene );

	/// <summary>
	/// The frames' base name: lower case, runs of non-alphanumeric characters replaced with one underscore.
	/// </summary>
	static string FileName( string name ) => System.Text.RegularExpressions.Regex.Replace( name, "[^A-Za-z0-9]+", "_" ).Trim( '_' ).ToLowerInvariant();

	/// <summary>
	/// Load the first preset.
	/// </summary>
	public void Start( SceneLabWindow window )
	{
		if ( !Advance( window ) ) window.FinishParity();
	}

	/// <summary>
	/// Whether the run is idling between jobs, with nothing to render.
	/// </summary>
	public bool Idle => idleFrames > 0;

	/// <summary>
	/// Every frame, before rendering: count down an idle, then start the job; skip a preset that's taken too long.
	/// </summary>
	public void Tick( SceneLabWindow window )
	{
		if ( idleFrames > 0 )
		{
			if ( --idleFrames == 0 && !Begin( window ) && !Advance( window ) ) window.FinishParity();
			return;
		}

		if ( index < 0 || index >= jobs.Count || jobTime.Elapsed.TotalSeconds < TimeoutSeconds ) return;

		Report( $"TIMEOUT after {TimeoutSeconds:0} s", failed: true );
		if ( !Advance( window ) ) window.FinishParity();
	}

	/// <summary>
	/// A capture's result for the preset being captured.
	/// </summary>
	public void Report( string text, bool failed )
	{
		var name = Current.Scene.Name;
		if ( results.Count == 0 || results[^1].Scene != name ) results.Add( (name, new List<string>(), false) );

		var entry = results[^1];
		entry.Results.Add( text );
		results[^1] = (entry.Scene, entry.Results, entry.Failed || failed);
	}

	/// <summary>
	/// The preset's captures are done: check its frames, then load the next. False when there are no more.
	/// </summary>
	public bool Advance( SceneLabWindow window )
	{
		if ( index >= 0 && index < jobs.Count ) CheckNative();

		if ( ++index >= jobs.Count ) return false;

		// A preset starts clean, so a frame from an earlier run can't stand in for one this run didn't write
		var job = Current;
		DeleteFrames( job.Scene );

		// An isolated preset waits for the pool to forget the last scene's targets
		if ( IsolatedScene( job.Scene ) )
		{
			window.Unload();
			idleFrames = IdleFrames;
			return true;
		}

		return Begin( window ) || Advance( window );
	}

	/// <summary>
	/// Load the current job. False when it failed to load, which is reported.
	/// </summary>
	bool Begin( SceneLabWindow window )
	{
		var job = Current;
		Log.Info( $"Parity: {job.Scene.Name}{(Isolated ? " (isolated)" : "")} - {index + 1} of {jobs.Count}" );
		jobTime.Restart();

		try
		{
			window.StartCapture( job.Scene );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Parity: {job.Scene.Name} failed to load" );
			Report( $"ERROR loading: {e.Message}", failed: true );
			return false;
		}
	}

	void DeleteFrames( SceneLabScene scene )
	{
		var file = FileName( scene.Name );
		foreach ( var path in System.IO.Directory.EnumerateFiles( Folder, $"{file}*.png" ) )
		{
			var leaf = System.IO.Path.GetFileNameWithoutExtension( path );
			if ( leaf == file || leaf.StartsWith( $"{file}." ) || leaf.StartsWith( $"{file}_changed" ) ) System.IO.File.Delete( path );
		}
	}

	/// <summary>
	/// Save or compare the preset's native frames (<c>-save-native</c>, <c>-compare-native</c>).
	/// </summary>
	void CheckNative()
	{
		if ( saveNative is null && compareNative is null ) return;

		var file = FileName( Current.Scene.Name );
		foreach ( var frame in new[] { $"{file}.native.png", $"{file}_changed.native.png" } )
		{
			var path = System.IO.Path.Combine( Folder, frame );
			if ( !System.IO.File.Exists( path ) ) continue;

			if ( saveNative is not null )
			{
				var dir = System.IO.Path.Combine( Folder, saveNative );
				System.IO.Directory.CreateDirectory( dir );
				System.IO.File.Copy( path, System.IO.Path.Combine( dir, frame ), overwrite: true );
			}

			if ( compareNative is not null )
			{
				var saved = System.IO.Path.Combine( Folder, compareNative, frame );
				if ( !System.IO.File.Exists( saved ) )
				{
					Report( "native: nothing saved to compare", failed: true );
					continue;
				}

				var same = Pixels( saved ).AsSpan().SequenceEqual( Pixels( path ) );
				Report( same ? "native: identical" : "native: CHANGED", failed: !same );
			}
		}
	}

	static Color32[] Pixels( string path )
	{
		using var bitmap = Bitmap.CreateFromBytes( System.IO.File.ReadAllBytes( path ) );
		return bitmap.GetPixels32();
	}

	/// <summary>
	/// The table of results, logged and written to <c>parity.txt</c> in the folder. Returns the number of presets that failed.
	/// </summary>
	public int Summarise()
	{
		var text = new System.Text.StringBuilder();
		foreach ( var (scene, entries, _) in results )
			text.AppendLine( $"{scene,-36} {string.Join( " / ", entries )}" );

		var failed = results.Count( x => x.Failed );
		text.AppendLine();
		text.AppendLine( failed > 0 ? $"{failed} failed. Frames are in {Folder}" : $"All passed. Frames are in {Folder}" );

		System.IO.File.WriteAllText( System.IO.Path.Combine( Folder, "parity.txt" ), text.ToString() );
		foreach ( var line in text.ToString().Split( '\n' ) ) Log.Info( $"Parity: {line.TrimEnd()}" );
		return failed;
	}
}
