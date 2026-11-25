using System.Diagnostics;
using System.Threading;

namespace Sandbox.Rendering;

/// <summary>
/// The sync point between the game and the frame it hands over (docs/reference/borealis-render-thread.md): where scene
/// objects' changes are written into their render objects.
/// </summary>
internal static class RenderSync
{
	static RenderSync()
	{
		// For test runs: BOREALIS_DEFERWRITES=1 defers everywhere, as the game window does
		if ( System.Environment.GetEnvironmentVariable( "BOREALIS_DEFERWRITES" ) == "1" ) DeferWrites = true;
	}

	/// <summary>
	/// Whether scene objects' changes, deletes included, wait for their world's next frame preparation (the sync point) instead
	/// of being written into their render objects as they're made: each changed object is written there once, with every change
	/// since the last frame, and render objects are written nowhere else. On in the game window on Borealis.Gpu unless it runs
	/// serially (<see cref="BorealisGpuFrameThread.Enabled"/>).
	/// </summary>
	internal static bool DeferWrites { get; set; }

	/// <summary>
	/// Hand-overs so far: each frame handed to the render thread starts a new epoch. What the main thread records after a
	/// hand-over (a command list, <see cref="Overlapping"/>) is kept apart from what that frame replays, by epoch.
	/// </summary>
	internal static long Epoch => Volatile.Read( ref epoch );
	static long epoch;

	/// <summary>
	/// Whether a frame handed over may be replaying on another thread while this one records the next: what the main thread
	/// records then is kept apart from what the frame reads (a command list splits, copy-on-write). False in serial mode, where
	/// a frame renders where it's handed over.
	/// </summary>
	internal static bool Overlapping { get; set; }

	/// <summary>Whether this thread renders frames handed over (the render thread).</summary>
	internal static bool OnRenderThread => onRenderThread || ThreadSafe.IsRenderThread;
	[ThreadStatic] static bool onRenderThread;

	/// <summary>Mark this thread as the one frames handed over render on (the render thread), or not.</summary>
	internal static void MarkRenderThread( bool value ) => onRenderThread = value;

	/// <summary>
	/// A frame is handed over: what the main thread records from now is the next frame's (<see cref="Epoch"/>).
	/// </summary>
	internal static void HandOver() => Interlocked.Increment( ref epoch );

	/// <summary>
	/// Whether the render thread is drawing the frame handed over last (from the hand-over until it's drawn): what the main
	/// thread lets go of meanwhile that the frame was handed over with waits for it.
	/// </summary>
	internal static bool RenderThreadDrawing
	{
		get => Volatile.Read( ref renderThreadDrawing );
		set => Volatile.Write( ref renderThreadDrawing, value );
	}
	static bool renderThreadDrawing;

	/// <summary>Command lists the main thread recorded into while a frame handed over could replay them, each copied once.</summary>
	internal static long ListSplits => Interlocked.Read( ref listSplits );
	static long listSplits;

	internal static void CountListSplit() => Interlocked.Increment( ref listSplits );

	/// <summary>Attribute sets the main thread wrote while a frame handed over could read them, each copied once.</summary>
	internal static long AttributeSplits => Interlocked.Read( ref attributeSplits );
	static long attributeSplits;

	internal static void CountAttributeSplit() => Interlocked.Increment( ref attributeSplits );

	/// <summary>
	/// Why game code would run in this frame's drawing, or null: a custom object drawn by game code that the frame's extract
	/// didn't record (<see cref="GameDrawExtract"/>: it's new, or the extract is off). Such a frame draws on the main thread;
	/// the render thread runs no game code. A renderer drawn from a command list is the engine's own drawing of its scene
	/// object, which the render thread draws.
	/// </summary>
	internal static string GameDrawsInFrame
	{
		get
		{
			if ( GameDrawExtract.NotCovered is { } reason && SceneCustomObject.GameDrawn() is { } custom )
			{
				LastGameDrawn = custom.GetType().FullName;
				return reason;
			}

			return null;
		}
	}

	/// <summary>The type of the last custom object that kept a frame on the main thread, for the stats.</summary>
	internal static string LastGameDrawn { get; private set; }

	// Every world drawn on Borealis (WorldRender registers itself), and a copy walked at the sync point, kept
	static readonly List<ISceneWorldRender> worlds = new();
	static ISceneWorldRender[] walking = [];

	/// <summary>A world drawn on Borealis came (its renderer was made): the sync point writes it from now on.</summary>
	internal static void Register( ISceneWorldRender world )
	{
		lock ( worlds ) worlds.Add( world );
	}

	/// <summary>A world's renderer went.</summary>
	internal static void Unregister( ISceneWorldRender world )
	{
		lock ( worlds ) worlds.Remove( world );
	}

	/// <summary>Worlds the sync point writes, for the tests.</summary>
	internal static int Worlds
	{
		get
		{
			lock ( worlds ) return worlds.Count;
		}
	}

	// Whether changes wait for the sync point for a render thread switched on outside the game window, and what was set before
	static bool deferringForRenderThread, deferredBefore, overlappingBefore;

	/// <summary>
	/// For a render thread drawing outside the game window (the editor's game view while the game plays): changes wait for the
	/// sync point and what the main thread records is kept apart from the frame handed over (<see cref="DeferWrites"/>,
	/// <see cref="Overlapping"/>), as in the game window; switched off, they're as they were before. Switching joins the render
	/// thread and writes what waited: switched on mid-frame, the frame's sync point has already run.
	/// </summary>
	internal static void DeferForRenderThread( bool on )
	{
		if ( deferringForRenderThread == on ) return;
		BorealisGpuRenderBackend.JoinRenderThread();
		if ( on )
		{
			deferredBefore = DeferWrites;
			overlappingBefore = Overlapping;
			DeferWrites = true;
			Overlapping = true;
			deferringForRenderThread = true;
			Run();
			return;
		}

		Run();
		DeferWrites = deferredBefore;
		Overlapping = overlappingBefore;
		deferringForRenderThread = false;
	}

	/// <summary>Sync points run so far, for the tests.</summary>
	internal static long Syncs { get; private set; }

	static readonly Sandbox.Diagnostics.PerformanceStats.Timings SyncTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis sync point" );

	/// <summary>
	/// The sync point, once a frame before the frame's output renders anything (<c>EngineLoop.OnClientOutput</c>) while changes
	/// are deferred (<see cref="DeferWrites"/>): every live world with changes waiting is written (<see cref="ISceneWorldRender.Sync"/>),
	/// a world drawn only inside another's view (a skybox's, a camera's extra world) as much as the one the window shows, so the
	/// frame's views find the state the game left. Nothing otherwise: changes are written as they're made.
	/// </summary>
	internal static void Run()
	{
		if ( !DeferWrites ) return;
		ThreadSafe.AssertIsMainThread();
		using var timing = SyncTiming.Scope();
		using var zone = global::Borealis.Zones.SyncPoint.Start();
		Syncs++;

		int count;
		lock ( worlds )
		{
			count = worlds.Count;
			if ( walking.Length < count ) walking = new ISceneWorldRender[Math.Max( count, walking.Length * 2 )];
			worlds.CopyTo( walking );
		}

		try
		{
			for ( int i = 0; i < count; i++ ) walking[i].Sync();
		}
		finally
		{
			// Not held past the frame: a world that goes is let go of
			Array.Clear( walking, 0, count );
		}
	}

	// Synchronous readbacks: calls, the time they waited, and the call sites seen
	static long readbacks, readbackTicks;
	static readonly HashSet<string> readbackSites = new();

	/// <summary>Synchronous readbacks so far (a thread waiting for the GPU to read something back), for the tests.</summary>
	internal static long Readbacks => Interlocked.Read( ref readbacks );

	/// <summary>
	/// Time a synchronous readback (<c>GpuBuffer.GetData</c>, <c>Texture.GetPixels</c>, a bitmap render): the calling thread
	/// waits for the GPU there, and with a render thread for it too. Each is counted, and each call site is logged once with
	/// how long it waited, found from the stack on the first call and at each power of two of calls (a stack each would
	/// allocate every frame for a game reading back every frame).
	/// </summary>
	internal static ReadbackScope Readback( string what ) => new( what, Stopwatch.GetTimestamp() );

	internal readonly ref struct ReadbackScope( string what, long start )
	{
		public void Dispose()
		{
			if ( what is null ) return;
			var ticks = Stopwatch.GetTimestamp() - start;
			// In a game's drawing recorded in the extract: a sync point that drawing makes, counted as one
			if ( Graphics.Recording is not null ) GameDrawExtract.CountSyncPoint( what );
			var n = Interlocked.Increment( ref readbacks );
			Interlocked.Add( ref readbackTicks, ticks );
			if ( (n & (n - 1)) != 0 ) return;

			var site = CallSite();
			lock ( readbackSites )
			{
				if ( !readbackSites.Add( $"{what} from {site}" ) ) return;
			}

			Log.Info( $"Synchronous readback ({what}) from {site}: waited {ticks * 1000.0 / Stopwatch.Frequency:F2} ms for the GPU" );
		}
	}

	// Main-thread waits for the render thread's drawing, and their call sites, as readbacks are counted
	static long renderWaits;
	static readonly HashSet<string> renderWaitSites = new();

	/// <summary>Main-thread uses of the device that waited for the render thread's drawing, for the tests and stats.</summary>
	internal static long RenderWaits => Interlocked.Read( ref renderWaits );

	/// <summary>
	/// Time the main thread waiting for the render thread's drawing before it uses the device: each wait is counted, and each
	/// call site is logged once with how long it waited, found from the stack on the first wait and at each power of two.
	/// </summary>
	internal static RenderWaitScope RenderWait() => new( Stopwatch.GetTimestamp() );

	internal readonly ref struct RenderWaitScope( long start )
	{
		public void Dispose()
		{
			var ticks = Stopwatch.GetTimestamp() - start;
			var n = Interlocked.Increment( ref renderWaits );
			if ( (n & (n - 1)) != 0 ) return;

			var site = new StackTrace( 2, false ).ToString();
			lock ( renderWaitSites )
			{
				if ( !renderWaitSites.Add( site ) ) return;
			}

			Log.Info( $"The main thread waited {ticks * 1000.0 / Stopwatch.Frequency:F2} ms for the render thread at {site}" );
		}
	}

	// The first frame outside the engine and the renderer (game or tool code), else the outermost engine frame named
	static string CallSite()
	{
		var frames = new StackTrace( 2, false ).GetFrames();
		string engine = null;
		foreach ( var frame in frames )
		{
			if ( frame.GetMethod() is not { DeclaringType: { } type } method ) continue;
			var assembly = type.Assembly.GetName().Name ?? "";
			var name = $"{type.FullName}.{method.Name}";
			if ( !assembly.StartsWith( "Sandbox.Engine" ) && !assembly.StartsWith( "Sandbox.Rendering" ) && !assembly.StartsWith( "Borealis" ) && !assembly.StartsWith( "System." ) ) return name;
			if ( engine is null && !type.FullName.StartsWith( "Sandbox.Rendering.RenderSync" ) && !type.Name.StartsWith( "BorealisGpu" ) && !type.Name.StartsWith( "SceneBridge" ) ) engine = name;
		}

		return engine ?? "unknown";
	}

	/// <summary>Say how many synchronous readbacks there were, how long they waited, and from where.</summary>
	[ConCmd( "r_sync_readbacks", Help = "How many synchronous GPU readbacks ran (each a stall for the GPU), how long they waited, and their call sites" )]
	internal static void PrintReadbacks()
	{
		Log.Info( $"{Readbacks} synchronous readbacks, {Interlocked.Read( ref readbackTicks ) * 1000.0 / Stopwatch.Frequency:F1} ms waiting" );
		lock ( readbackSites )
		{
			foreach ( var site in readbackSites ) Log.Info( $"  {site}" );
		}
	}
}
