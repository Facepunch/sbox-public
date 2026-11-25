namespace Sandbox;

/// <summary>
/// A map's load leaves the managed heap with GBs committed once the game has closed, which ordinary collections in the
/// menu don't give back. One aggressive collection a little after the close, while the menu's up, compacts and decommits it.
/// </summary>
internal static class ClosedGameMemory
{
	static int closes;

	internal static void GameClosed()
	{
		var close = ++closes;
		if ( Application.IsDedicatedServer || Application.IsHeadless || Application.IsUnitTest || Application.IsBenchmark || Application.IsEditor ) return;

		_ = CollectLater( close );
	}

	static async Task CollectLater( int close )
	{
		// Native holds resources the close touched for 10 seconds, and the game's finalizers and assemblies take a few
		// collections after that to go
		await Task.Delay( TimeSpan.FromSeconds( 15 ) );
		if ( close != closes || !Game.IsMainMenuVisible || LoadingScreen.IsVisible ) return;

		GC.Collect( GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true );
	}
}
