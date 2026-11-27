using Sandbox.Diagnostics;

namespace EngineTests;

[TestClass]
public class ProfilerTimelineActivityTests
{
	static Profiler.Span Span( int index, double start, double end, int depth = 0 ) => new( index, 1, 1, -1, depth, start, end, end - start, false );

	[TestMethod]
	public void TinyScopesMergeByPixelAndDepthAndResolveWhenZoomed()
	{
		Profiler.Span[] spans = [Span( 0, 1.125, 1.25 ), Span( 1, 1.375, 1.5 ), Span( 2, 1.625, 1.75, 1 )];
		DevToolsProfilerTimeline.BuildLane( spans, 0, 100, 100, out var visible, out var marks );
		Assert.AreEqual( 0, visible.Count );
		Assert.AreEqual( 2, marks.Count );
		var merged = marks.Single( x => x.Depth == 0 );
		Assert.AreEqual( 1, merged.Pixel );
		Assert.AreEqual( 2, merged.Count );
		Assert.AreEqual( 1.125, merged.StartMs );
		Assert.AreEqual( 1.5, merged.EndMs );
		DevToolsProfilerTimeline.BuildLane( spans, 1, 2, 100, out visible, out marks );
		Assert.AreEqual( 3, visible.Count );
		Assert.AreEqual( 0, marks.Count );
	}

	[TestMethod]
	public void ViewportEdgesAndZeroDurationEventsStayInBounds()
	{
		Profiler.Span[] spans = [Span( 0, -2, -1 ), Span( 1, 0, 0 ), Span( 2, 0, 0.125 ), Span( 3, 99.875, 100.125 ), Span( 4, 100, 101 )];
		DevToolsProfilerTimeline.BuildLane( spans, 0, 100, 100, out var visible, out var marks );
		Assert.AreEqual( 0, visible.Count );
		CollectionAssert.AreEqual( new[] { 0, 99 }, marks.Select( x => x.Pixel ).Order().ToArray() );
	}

	[TestMethod]
	public void RegularBarsKeepTheirOriginalDurations()
	{
		Profiler.Span[] spans = [Span( 0, 1, 4 ), Span( 1, 5, 5.025 ), Span( 2, 6, 7, 64 )];
		DevToolsProfilerTimeline.BuildLane( spans, 0, 10, 100, out var visible, out var marks );
		Assert.AreEqual( spans[0], visible.Single() );
		Assert.AreEqual( 1, marks.Count );
		Assert.AreEqual( 5, marks.Single().StartMs );
	}

	[TestMethod]
	public void SubTenthPixelScopesAreCulledUntilZoomed()
	{
		Profiler.Span[] spans = [Span( 0, 0, 0.05 ), Span( 1, 0, 0.1, 1 )];
		DevToolsProfilerTimeline.BuildLane( spans, 0, 100, 100, out var visible, out var marks );
		Assert.AreEqual( 0, visible.Count );
		Assert.AreEqual( 1, marks.Count );
		Assert.AreEqual( 1, marks.Single().Depth );
		DevToolsProfilerTimeline.BuildLane( spans, 0, 1, 100, out visible, out marks );
		Assert.AreEqual( 2, visible.Count );
		Assert.AreEqual( 0, marks.Count );
	}
}
