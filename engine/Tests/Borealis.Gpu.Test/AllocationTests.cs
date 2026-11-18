using Borealis;
using BorealisGpuSample;

namespace BorealisGpuTests;

/// <summary>
/// The renderer allocates nothing in steady state (owner, 2026-11-17): once a scene has drawn a few frames and nothing
/// loads or changes, Borealis's frames on the main thread and its recording jobs on workers allocate no managed memory
/// (<see cref="RendererAllocations"/>). The scene: s&amp;box models (one skinned), boxes and spheres, some of them
/// moving, a sun with cascaded shadows, point and spot lights, recorded in parallel. The engine's side (post processing,
/// UI, custom objects, the Borealis.Gpu frame handover) is counted in game by the same counter (perf stats). Until every
/// cut is in, frames are held to a budget that only goes down.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AllocationTests : GpuTest
{
	const int WarmFrames = 40, MeasuredFrames = 30;

	// The most any frame may allocate once warm, lowered with each cut until it is 0 (the goal). 2026-11-18: 2-6 KB a frame.
	const long BudgetPerFrame = 8 * 1024;

	[TestMethod]
	public void SteadyFramesDontAllocate()
	{
		using var scene = new BorealisScene( Device );
		scene.System.ParallelRecording = true;

		var bytes = new long[MeasuredFrames];
		for ( int frame = 0; frame < WarmFrames + MeasuredFrames; frame++ )
		{
			Device.BeginFrame();
			RendererAllocations.TakeFrame();
			scene.Render( 640, 400, frame / 60f );
			var allocated = RendererAllocations.TakeFrame();
			if ( frame >= WarmFrames ) bytes[frame - WarmFrames] = allocated;
		}

		Device.WaitIdle();
		var total = bytes.Sum();
		TestContext.WriteLine( $"renderer allocations per frame after {WarmFrames} warm frames: mean {total / MeasuredFrames} B, max {bytes.Max()} B ({string.Join( ", ", bytes )})" );
		Assert.IsTrue( bytes.Max() <= BudgetPerFrame, $"the renderer allocated up to {bytes.Max()} B in a frame once warm ({total / MeasuredFrames} B on average), over its budget of {BudgetPerFrame} B" );
	}
}
