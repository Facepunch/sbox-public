using System.Diagnostics;

namespace Sandbox;

/// <summary>
/// Loads UI thumbnails (<c>thumb:</c>, mounts, packages, web images) without hitching the game.
/// <para>
/// Setting <c>background-image: url( thumb:... )</c> straight on a panel loads the texture inline on the
/// frame the style is applied - fine for one icon, but the first open of a spawn page asks for dozens at
/// once, and mounted models have to be loaded and rendered to make theirs, so the game freezes. Instead
/// icons ask here each frame they're on screen, and the queue starts a few loads a frame inside a small
/// time budget. Finished textures are kept, so recycled grid cells and later visits are instant.
/// </para>
/// </summary>
public static class ThumbnailLoader
{
	public enum State
	{
		Loading,
		Ready,
		Failed
	}

	/// <summary>
	/// Main-thread time a frame may spend on thumbnail work. The first job of a frame always runs, so one
	/// slow load can't stall the queue forever.
	/// </summary>
	const double FrameBudgetMs = 1.5;

	/// <summary>
	/// Async loads allowed in the air at once, so a page of web thumbnails doesn't flood the downloader.
	/// </summary>
	const int MaxInFlight = 8;

	/// <summary>
	/// A queued path nobody has asked for in this many frames scrolled away or got hidden, so it's dropped.
	/// It's queued again if it comes back.
	/// </summary>
	const int StaleFrames = 3;

	static readonly Dictionary<string, Texture> Loaded = new( StringComparer.OrdinalIgnoreCase );
	static readonly HashSet<string> Failed = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Waiting paths, oldest first, and the frame each was last asked for.
	/// </summary>
	static readonly List<string> Queue = new();
	static readonly Dictionary<string, int> Wanted = new( StringComparer.OrdinalIgnoreCase );
	static readonly HashSet<string> InFlight = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Paths the async loader gave nothing back for, to be tried with the plain loader.
	/// </summary>
	static readonly HashSet<string> NeedsSync = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Schemes (<c>thumb</c>, <c>mount</c>...) the async loader can't do but the plain one can, so later
	/// loads skip straight to it.
	/// </summary>
	static readonly HashSet<string> SyncOnlySchemes = new( StringComparer.OrdinalIgnoreCase );

	static int _frame;
	static double _spentMs;
	static int _jobs;

	/// <summary>
	/// The texture for <paramref name="path"/> if it's ready. Otherwise, when <paramref name="want"/> is set,
	/// it's queued - keep asking each frame you still need it, anything not asked for gets dropped.
	/// </summary>
	public static State Get( string path, bool want, out Texture texture )
	{
		texture = null;
		if ( string.IsNullOrWhiteSpace( path ) ) return State.Failed;

		if ( Loaded.TryGetValue( path, out texture ) ) return State.Ready;
		if ( Failed.Contains( path ) ) return State.Failed;

		if ( want && !InFlight.Contains( path ) )
		{
			if ( !Wanted.ContainsKey( path ) ) Queue.Add( path );
			Wanted[path] = _frame;
		}

		return State.Loading;
	}

	/// <summary>
	/// For work that isn't a texture load but is just as heavy, like setting up a 3D preview. True when this
	/// frame still has thumbnail budget - do the work, then pass the timestamp from before it to <see cref="Spend"/>.
	/// </summary>
	public static bool TryTakeSlot()
	{
		if ( _jobs > 0 && _spentMs >= FrameBudgetMs ) return false;

		_jobs++;
		return true;
	}

	/// <summary>
	/// Charge the frame's budget for work started at <paramref name="startTimestamp"/> (<see cref="Stopwatch.GetTimestamp"/>).
	/// </summary>
	public static void Spend( long startTimestamp ) => _spentMs += ElapsedMs( startTimestamp );

	/// <summary>
	/// Forget everything loaded, e.g. after content changed on disk.
	/// </summary>
	public static void Clear()
	{
		Loaded.Clear();
		Failed.Clear();
		Queue.Clear();
		Wanted.Clear();
		NeedsSync.Clear();
	}

	/// <summary>
	/// Once a frame, before the UI ticks: start a new budget and spend it on the queue.
	/// </summary>
	internal static void Pump()
	{
		_frame++;
		_spentMs = 0;
		_jobs = 0;

		for ( var i = 0; i < Queue.Count; )
		{
			var path = Queue[i];

			// not asked for lately, it scrolled off or the menu closed
			if ( !Wanted.TryGetValue( path, out var lastWanted ) || _frame - lastWanted > StaleFrames || Loaded.ContainsKey( path ) || Failed.Contains( path ) )
			{
				Drop( i, path );
				continue;
			}

			var sync = NeedsSync.Contains( path ) || SyncOnlySchemes.Contains( Scheme( path ) );

			// async loads only cost the time to kick them off, but don't flood the downloader
			if ( !sync && InFlight.Count >= MaxInFlight )
			{
				i++;
				continue;
			}

			if ( !TryTakeSlot() ) break;

			var start = Stopwatch.GetTimestamp();
			Queue.RemoveAt( i );
			Wanted.Remove( path );

			if ( sync )
				LoadNow( path );
			else
				LoadAsync( path );

			Spend( start );
		}
	}

	static void Drop( int index, string path )
	{
		Queue.RemoveAt( index );
		Wanted.Remove( path );
	}

	static void LoadNow( string path )
	{
		var fallback = NeedsSync.Remove( path );
		Texture texture = null;

		try
		{
			texture = Texture.Load( path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't load thumbnail '{path}': {e.Message}" );
		}

		// The plain loader managed what the async one couldn't - go straight to it for this scheme from now on.
		// Never for the web, a plain load there would block on the download.
		if ( fallback && texture.IsValid() && !IsWeb( path ) )
		{
			var scheme = Scheme( path );
			if ( scheme.Length > 0 ) SyncOnlySchemes.Add( scheme );
		}

		Finish( path, texture );
	}

	static async void LoadAsync( string path )
	{
		InFlight.Add( path );
		Texture texture = null;

		try
		{
			texture = await Texture.LoadAsync( path );
		}
		catch
		{
			// some schemes only load through the plain loader, tried below
		}

		InFlight.Remove( path );

		if ( texture.IsValid() || IsWeb( path ) )
		{
			Finish( path, texture );
			return;
		}

		// Back through the queue, at the front, so the plain load still happens inside a frame's budget
		NeedsSync.Add( path );
		if ( !Wanted.ContainsKey( path ) ) Queue.Insert( 0, path );
		Wanted[path] = _frame;
	}

	static void Finish( string path, Texture texture )
	{
		if ( texture.IsValid() )
			Loaded[path] = texture;
		else
			Failed.Add( path );
	}

	static bool IsWeb( string path ) => path.StartsWith( "http://", StringComparison.OrdinalIgnoreCase ) || path.StartsWith( "https://", StringComparison.OrdinalIgnoreCase );

	static string Scheme( string path )
	{
		var colon = path.IndexOf( ':' );
		return colon > 0 ? path[..colon] : "";
	}

	static double ElapsedMs( long startTimestamp ) => (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
}

/// <summary>
/// Runs the <see cref="ThumbnailLoader"/> queue once a frame, before the UI ticks.
/// </summary>
internal sealed class ThumbnailLoaderSystem : GameObjectSystem<ThumbnailLoaderSystem>
{
	public ThumbnailLoaderSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Pump, "ThumbnailLoader" );
	}

	void Pump()
	{
		// editor scenes made to render thumbnails get a system too, only the game's drives the queue
		if ( Scene != Game.ActiveScene ) return;

		ThumbnailLoader.Pump();
	}
}
