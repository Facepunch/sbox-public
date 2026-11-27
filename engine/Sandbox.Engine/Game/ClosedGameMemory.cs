namespace Sandbox;

/// <summary>
/// Gives back the memory a game left free once it has closed to the menu. A map's load raises the managed heap to a few
/// GB live (Construct's models, materials and scene); once the game has gone that's free, but the collector keeps it:
/// free space between what's still live, and regions it hands back only so fast after each collection. Background
/// collections a few seconds apart from 10 seconds after the close, until what's live stops shrinking, turn what the game
/// left into free regions the collector gives back over the next seconds, without stopping the threads for more than a
/// few milliseconds. It doesn't compact: that stops every thread while it moves what the menu keeps live, a frozen frame.
/// </summary>
internal static class ClosedGameMemory
{
	/// <summary>
	/// Seconds from the close to the first background collection: the engine keeps resources the close touched for up to 10
	/// seconds (<see cref="NativeResourceCache"/>).
	/// </summary>
	internal const float Seconds = 10;

	/// <summary>
	/// Seconds between background collections until what's live stops shrinking: the game's objects with finalizers, and
	/// its unloading assemblies, take a few collections to go, and compacting with them still in it takes twice as long.
	/// </summary>
	internal const float StepSeconds = 2;

	/// <summary>Background collections at most.</summary>
	internal const int Steps = 30;

	/// <summary>Bytes; a quarter of this less live than the last collection counts as still shrinking.</summary>
	internal const long Threshold = 256L << 20;

	/// <summary>Counts closes, so one queued before another close or a load is dropped.</summary>
	static int closes;

	/// <summary>Not on a dedicated server, in a terminal, a test, a benchmark or the editor.</summary>
	static bool Enabled => !Application.IsDedicatedServer && !Application.IsHeadless && !Application.IsUnitTest && !Application.IsBenchmark && !Application.IsEditor;

	/// <summary>A game has closed to the menu: give its memory back in a few seconds if the menu's still up then.</summary>
	internal static void GameClosed()
	{
		closes++;
		if ( Enabled ) _ = ReturnLater( closes );
	}

	static async Task ReturnLater( int close )
	{
		await Task.Delay( TimeSpan.FromSeconds( Seconds ) );
		var lastLive = long.MaxValue;
		for ( int step = 0; step < Steps; step++ )
		{
			if ( close != closes || !Game.IsMainMenuVisible || LoadingScreen.IsVisible ) return;
			var info = GC.GetGCMemoryInfo( GCKind.Background );
			var live = info.HeapSizeBytes - info.FragmentedBytes;
			if ( step > 0 && live > lastLive - Threshold / 4 ) break;
			lastLive = live;
			GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced, blocking: false );
			await Task.Delay( TimeSpan.FromSeconds( StepSeconds ) );
		}
	}

	/// <summary>Committed bytes not live, as of the last collection: free regions and the free space between objects.</summary>
	internal static long Unused( GCMemoryInfo info ) => info.TotalCommittedBytes - (info.HeapSizeBytes - info.FragmentedBytes);

	/// <summary>
	/// Collect aggressively if the heap keeps at least <paramref name="threshold"/> bytes committed that aren't live, so the
	/// collector compacts and decommits them; the bytes it gave back (0 when it didn't run).
	/// </summary>
	internal static long Return( long threshold = Threshold )
	{
		var before = GC.GetGCMemoryInfo();
		if ( Unused( before ) < threshold ) return 0;

		var paused = GC.GetTotalPauseDuration();
		GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true );
		var after = GC.GetGCMemoryInfo();
		var given = before.TotalCommittedBytes - after.TotalCommittedBytes;
		Log.Info( $"Gave back {given >> 20} MiB of the managed heap ({after.TotalCommittedBytes >> 20} MiB committed, {(GC.GetTotalPauseDuration() - paused).TotalMilliseconds:F0} ms)" );
		return given;
	}

	/// <summary>Counts scene loads, so a compaction queued for one is dropped when another starts.</summary>
	static int loads;

	/// <summary>
	/// A scene has loaded: once the renderer has its models and textures, give back the free space the load left between
	/// what stays live (2.4 GB after a big editor scene) with one aggressive collection, a pause right after the load.
	/// </summary>
	internal static async Task CompactAfterLoad( Scene scene )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || Application.IsUnitTest || Application.IsBenchmark ) return;

		var load = ++loads;
		await BorealisGpuRenderBackend.WaitForContent( scene );
		if ( load != loads || !scene.IsValid ) return;

		Return();
		_ = SettleAfterLoad( load, scene );
	}

	/// <summary>Seconds after a load's compaction before the second one: what the first frames and the editor's own work after the load leave.</summary>
	internal const float SettleSeconds = 60;

	/// <summary>
	/// The minute after a load leaves another GB or two the collector doesn't take back on its own (2 GB on a big editor
	/// scene): a background collection makes it measurable, then <see cref="Return"/> compacts if it's worth it.
	/// </summary>
	static async Task SettleAfterLoad( int load, Scene scene )
	{
		await Task.Delay( TimeSpan.FromSeconds( SettleSeconds ) );
		if ( load != loads || !scene.IsValid ) return;

		GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced, blocking: false );
		await Task.Delay( TimeSpan.FromSeconds( StepSeconds ) );
		if ( load != loads || !scene.IsValid ) return;

		Return();
	}
}
