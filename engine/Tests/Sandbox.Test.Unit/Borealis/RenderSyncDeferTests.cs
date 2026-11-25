using Sandbox.Rendering;

namespace BorealisTests;

/// <summary>
/// The sync point switched on for a render thread outside the game window (docs/reference/borealis-render-thread.md, the
/// editor's game view): changes wait for it while the game plays, and are written as they're made again once play stops.
/// </summary>
[TestClass, DoNotParallelize]
public class RenderSyncDeferTests
{
	[TestMethod]
	public void DeferringForARenderThreadWritesWhatWaitedAndPutsItBack()
	{
		ThreadSafe.MarkMainThread();
		var deferred = RenderSync.DeferWrites;
		var overlapping = RenderSync.Overlapping;
		try
		{
			RenderSync.DeferWrites = false;
			RenderSync.Overlapping = false;
			var syncs = RenderSync.Syncs;

			RenderSync.DeferForRenderThread( true );
			Assert.IsTrue( RenderSync.DeferWrites, "changes wait for the sync point" );
			Assert.IsTrue( RenderSync.Overlapping, "what the main thread records is kept apart from the frame handed over" );
			Assert.AreEqual( syncs + 1, RenderSync.Syncs, "switched on mid-frame, after the frame's sync point: written now" );

			RenderSync.DeferForRenderThread( true );
			Assert.AreEqual( syncs + 1, RenderSync.Syncs, "nothing more while it stays on" );

			RenderSync.DeferForRenderThread( false );
			Assert.AreEqual( syncs + 2, RenderSync.Syncs, "what waited is written before changes are written as they're made" );
			Assert.IsFalse( RenderSync.DeferWrites, "changes are written as they're made again" );
			Assert.IsFalse( RenderSync.Overlapping );
		}
		finally
		{
			RenderSync.DeferForRenderThread( false );
			RenderSync.DeferWrites = deferred;
			RenderSync.Overlapping = overlapping;
		}
	}

	[TestMethod]
	public void SwitchingOffKeepsADeferralSetElsewhere()
	{
		ThreadSafe.MarkMainThread();
		var deferred = RenderSync.DeferWrites;
		var overlapping = RenderSync.Overlapping;
		try
		{
			// As the game window sets it, or BOREALIS_DEFERWRITES
			RenderSync.DeferWrites = true;
			RenderSync.Overlapping = false;

			RenderSync.DeferForRenderThread( true );
			RenderSync.DeferForRenderThread( false );
			Assert.IsTrue( RenderSync.DeferWrites );
			Assert.IsFalse( RenderSync.Overlapping );
		}
		finally
		{
			RenderSync.DeferForRenderThread( false );
			RenderSync.DeferWrites = deferred;
			RenderSync.Overlapping = overlapping;
		}
	}
}
