using Sandbox.UI;
using System;

namespace UITests;

/// <summary>
/// The roots screen input is tested against, which replaced a per-frame Where/OrderByDescending.
/// </summary>
[TestClass]
[DoNotParallelize] // Panels share global UI state
public class ScreenInputRootsTests
{
	/// <summary>
	/// Highest z-index first, equal z-indices in root order, world panels left out - what
	/// OrderByDescending gave - and building the list again doesn't allocate.
	/// </summary>
	[TestMethod]
	public void HighestZIndexFirstInRootOrderWithoutAllocating()
	{
		ThreadSafe.MarkMainThread();

		var ui = new UISystem();
		var low = new RootPanel( ui );
		var highA = new RootPanel( ui );
		var none = new RootPanel( ui );
		var highB = new RootPanel( ui );
		var world = new RootPanel( ui ) { IsWorldPanel = true };
		var negative = new RootPanel( ui );

		low.Style.ZIndex = 1;
		highA.Style.ZIndex = 5;
		highB.Style.ZIndex = 5;
		world.Style.ZIndex = 10;
		negative.Style.ZIndex = -1;

		try
		{
			foreach ( var root in new[] { low, highA, none, highB, world, negative } )
				root.Layout();

			var expected = ui.GetActiveRoots().Where( p => !p.IsWorldPanel ).OrderByDescending( x => x.ComputedStyle?.ZIndex ?? 0 ).ToArray();
			CollectionAssert.AreEqual( new[] { highA, highB, low, none, negative }, expected );
			CollectionAssert.AreEqual( expected, ui.GetScreenInputRoots().ToArray() );

			long before = GC.GetAllocatedBytesForCurrentThread();
			ui.GetScreenInputRoots();
			Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
		}
		finally
		{
			foreach ( var root in new[] { low, highA, none, highB, world, negative } )
				root.Delete( true );
		}
	}
}
