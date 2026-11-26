using System.Diagnostics;

namespace Sandbox;

/// <summary>
/// Queues thumbnails requested by visible UI cells and limits the work started each frame.
/// Recycled cells reuse the loaded textures.
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
	static readonly Queue<string> Queue = new();
	static readonly Dictionary<string, int> Wanted = new( StringComparer.OrdinalIgnoreCase );
	static readonly HashSet<string> InFlight = new( StringComparer.OrdinalIgnoreCase );

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
			if ( !Wanted.ContainsKey( path ) ) Queue.Enqueue( path );
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
	public static void Spend( long startTimestamp ) => _spentMs += Stopwatch.GetElapsedTime( startTimestamp ).TotalMilliseconds;

	/// <summary>
	/// Forget everything loaded, e.g. after content changed on disk.
	/// </summary>
	public static void Clear()
	{
		Loaded.Clear();
		Failed.Clear();
		Queue.Clear();
		Wanted.Clear();
	}

	/// <summary>
	/// Once a frame, before the UI ticks: start a new budget and spend it on the queue.
	/// </summary>
	internal static void Pump()
	{
		_frame++;
		_spentMs = 0;
		_jobs = 0;

		while ( Queue.TryPeek( out var path ) )
		{
			if ( _frame - Wanted[path] > StaleFrames )
			{
				Queue.Dequeue();
				Wanted.Remove( path );
				continue;
			}

			if ( InFlight.Count >= MaxInFlight || !TryTakeSlot() ) break;

			var start = Stopwatch.GetTimestamp();
			Queue.Dequeue();
			Wanted.Remove( path );
			LoadAsync( path );
			Spend( start );
		}
	}

	static async void LoadAsync( string path )
	{
		InFlight.Add( path );
		Texture texture = null;

		try
		{
			var entity = MountedEntity( path );
			texture = entity is null ? await Texture.LoadAsync( path ) : await entity.LoadMountedIconAsync();
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't load thumbnail '{path}': {e.Message}" );
		}

		InFlight.Remove( path );

		if ( texture.IsValid() )
			Loaded[path] = texture;
		else
			Failed.Add( path );
	}

	/// <summary>
	/// The entity a <c>thumb:</c> path is for when its art comes from a mounted game. Its prefab has nothing to render
	/// until the mount loads it, so the entity makes its icon from the mount instead.
	/// </summary>
	static ScriptedEntity MountedEntity( string path )
	{
		if ( !path.StartsWith( "thumb:", StringComparison.OrdinalIgnoreCase ) || !path.EndsWith( ".sent", StringComparison.OrdinalIgnoreCase ) )
			return null;

		var entity = ResourceLibrary.Get<ScriptedEntity>( path[6..] );
		return string.IsNullOrEmpty( entity?.RequiredMount ) ? null : entity;
	}

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
