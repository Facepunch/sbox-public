using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

public partial class PanelTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// Doesn't-crash smoke test: the generated tree for a self-including panel throws, but that
	/// throw is swallowed by InternalRenderTree's catch (it logs a warning and finishes the tree),
	/// so this only guards that the catch itself keeps working - it can't observe the throw.
	/// </summary>
	[TestMethod]
	public void RecursivePanel()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new TestComponents.RecursivePanel();
		p.InternalRenderTree();
	}

	/// <summary>
	/// Non-recursive nested elements must build a render tree - the NestedPanel fixture
	/// declares nested divs, so children must exist after InternalRenderTree.
	/// </summary>
	[TestMethod]
	public void NonRecursivePanel()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		// Make sure that non-recursive nested elements still work
		var p = new TestComponents.NestedPanel();
		p.InternalRenderTree();

		Assert.IsTrue( p.ChildrenCount > 0 );
	}
}
