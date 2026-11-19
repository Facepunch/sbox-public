using Borealis;
using BorealisGpuSample;

namespace BorealisGpuTests;

/// <summary>
/// The renderer allocates nothing in steady state (owner, 2026-11-17): once a scene has drawn a few frames and nothing
/// loads or changes, Borealis's frames on the main thread and its recording jobs on workers allocate no managed memory
/// (<see cref="RendererAllocations"/>). The scene: s&amp;box models (one skinned), boxes and spheres, some of them
/// moving, a sun with cascaded shadows, point and spot lights, drawn GPU-driven and recorded in parallel. The engine's
/// side (post processing, UI, particles, custom objects, the Borealis.Gpu frame handover) is held to the same in the
/// integration tests (<c>RendererAllocationTests</c>) and counted in game by the same counter (perf stats).
/// </summary>
[TestClass]
[DoNotParallelize]
public class AllocationTests : GpuTest
{
	// Frames in a row that must allocate nothing, and the most it may take to get there (pipelines and caches warm up first)
	const int SettledFrames = 8, MaxFrames = 200;

	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void SteadyFramesDontAllocate()
	{
		using var scene = new BorealisScene( Device );
		scene.System.ParallelRecording = true;
		scene.GpuDriven = true;

		var bytes = new List<long>( MaxFrames );
		var settled = 0;
		for ( int frame = 0; frame < MaxFrames && settled < SettledFrames; frame++ )
		{
			Device.BeginFrame();
			RendererAllocations.TakeFrame();
			scene.Render( 640, 400, frame / 60f );
			var allocated = RendererAllocations.TakeFrame();
			bytes.Add( allocated );
			settled = allocated == 0 ? settled + 1 : 0;
		}

		Device.WaitIdle();
		TestContext.WriteLine( $"renderer allocations per frame: {string.Join( ", ", bytes )}" );
		Assert.AreEqual( SettledFrames, settled, $"the renderer kept allocating: never {SettledFrames} frames in a row with nothing in {MaxFrames}. Bytes per frame: {string.Join( ", ", bytes )}" );
	}
}
