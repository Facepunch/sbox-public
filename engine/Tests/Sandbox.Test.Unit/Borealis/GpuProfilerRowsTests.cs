using System.Collections.Generic;
using System.Linq;
using Sandbox.Diagnostics;

namespace BorealisTests;

/// <summary>
/// The dev tools' GPU passes on Borealis.Gpu: its timed lists at the top, each pass under the list or pass it ran inside,
/// and a name repeated in a frame (each view's prepass) kept apart by its occurrence.
/// </summary>
[TestClass]
public class GpuProfilerRowsTests
{
	[TestMethod]
	public void PassesNestUnderTheListsTheyRanIn()
	{
		var scopes = new List<GpuProfilerStats.GpuScope>
		{
			new( "Depth Prepass", false, 1.2, 1.5 ),
			new( "Borealis frame", true, 1.0, 2.0 ),
			new( "Engine block", true, 0.0, 0.5 ),
			new( "Opaque Forward", false, 1.5, 1.9 ),
			new( "GPU-driven cull", false, 1.5, 1.6 ),
			new( "Depth Prepass", false, 2.2, 2.4 ),
			new( "Borealis segment", true, 2.1, 2.5 ),
		};

		var rows = new List<GpuProfilerStats.Row>();
		GpuProfilerStats.BuildRows( scopes, rows, new List<int>(), new Dictionary<string, int>() );

		string[] names = ["Engine block", "Borealis frame", "Depth Prepass", "Opaque Forward", "GPU-driven cull", "Borealis segment", "Depth Prepass"];
		int[] parents = [-1, -1, 1, 1, 3, -1, 5];
		CollectionAssert.AreEqual( names, rows.Select( r => r.Name ).ToArray() );
		CollectionAssert.AreEqual( parents, rows.Select( r => r.Parent ).ToArray() );
		Assert.AreNotEqual( rows[2].StableId, rows[6].StableId, "each view's prepass is its own row" );
		Assert.AreEqual( 0.3f, rows[2].Duration, 1e-4f );
	}
}
