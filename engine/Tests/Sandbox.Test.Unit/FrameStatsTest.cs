using Sandbox.Diagnostics;
using Sandbox.Rendering;

namespace SystemTest;

[TestClass]
public class FrameStatsTest
{
	/// <summary>
	/// A frame's stats are what Borealis counted: objects, culling, draws, triangles and views come from its counters,
	/// and the counters only Source 2's scene views kept read 0.
	/// </summary>
	[TestMethod]
	public void FrameStatsComeFromBorealisCounters()
	{
		var counters = new ManagedFrameCounters
		{
			Renders = 2,
			Objects = 100,
			ObjectsVisible = 60,
			ObjectsSizeCulled = 7,
			ObjectsOcclusionCulled = 5,
			Draws = 30,
			DepthDraws = 20,
			ShadowDraws = 10,
			CustomDraws = 3,
			Triangles = 123456,
			ShadowViews = 4,
		};

		var stats = new FrameStats( counters, 9 );

		Assert.AreEqual( 60, stats.ObjectsRendered );
		Assert.AreEqual( 100, stats.ObjectsPreCull );
		Assert.AreEqual( 100, stats.ObjectsTested );
		Assert.AreEqual( 7, stats.ObjectsCulledByScreenSize );
		Assert.AreEqual( 5, stats.ObjectsCulledByOcclusion );
		Assert.AreEqual( 63, stats.DrawCalls );
		Assert.AreEqual( 123456, stats.TrianglesRendered );
		Assert.AreEqual( 6, stats.SceneViewsRendered );
		Assert.AreEqual( 9, stats.PendingStreamingRequests );
		Assert.AreEqual( 2, stats.Managed.Renders );
		Assert.AreEqual( 0, stats.MaterialChanges );
		Assert.AreEqual( 0, stats.DisplayLists );
		Assert.AreEqual( 0, stats.ShadowMaps );
		Assert.AreEqual( 0ul, stats.TexturePoolUsedBytes );
	}
}
