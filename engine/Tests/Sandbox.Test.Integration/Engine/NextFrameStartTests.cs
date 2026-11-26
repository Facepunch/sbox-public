using Sandbox.Tasks;
using System.Threading;

namespace EngineTests;

/// <summary>
/// <see cref="FrameStartWaiters.Next"/>: where a scene's load that waited for Borealis's content resumes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class NextFrameStartTests
{
	[TestInitialize]
	public void TestInitialize()
	{
		SyncContext.Init();
		SyncContext.Reset();
	}

	/// <summary>
	/// It resumes in the first tasks of a later frame, however often the frame it was asked in runs its tasks: a load that
	/// waited for content then finishes before the game ticks, as one that waited for nothing does. Before, the load
	/// resumed in whichever tasks ran next, often a frame's last, and the game's first tick and render took a frame of
	/// their own after the loading screen (220-270 ms after a load on Flatgrass and Construct).
	/// </summary>
	[TestMethod]
	public void ResumesOnlyOnceTheFrameHasMovedOn()
	{
		Assert.AreEqual( SyncContext.MainThread, SynchronizationContext.Current );
		var task = FrameStartWaiters.Next();
		Assert.AreSame( task, FrameStartWaiters.Next(), "waiters in one frame share a signal" );
		FrameStartWaiters.Complete();
		for ( int i = 0; i < 5; i++ ) SyncContext.MainThread.ProcessQueue();
		Assert.IsFalse( task.IsCompleted, "not within the frame it was asked in" );

		Application.FrameCount++;
		Assert.IsFalse( task.IsCompleted, "advancing the counter doesn't fire frame start" );
		var next = FrameStartWaiters.Next();
		FrameStartWaiters.Complete();
		SyncContext.MainThread.ProcessQueue();
		Assert.IsTrue( task.IsCompletedSuccessfully, "in the next frame's first tasks" );
		Assert.IsFalse( next.IsCompleted, "a waiter added in this frame is for the following frame" );
		Application.FrameCount++;
		FrameStartWaiters.Complete();
		Assert.IsTrue( next.IsCompletedSuccessfully );
	}
}
