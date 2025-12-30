using Sandbox.Engine;
using Sandbox.UI;

namespace UITest.Panels;

public partial class PanelTests
{
	[ClassInitialize]
	public static void ClassInitialize( TestContext context )
	{

	}

	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	[TestMethod]
	public void RecursivePanel()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new TestComponents.RecursivePanel();
		p.InternalRenderTree();
	}

	[TestMethod]
	public void NonRecursivePanel()
	{
		var r = new RootPanel();
		r.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		// Make sure that non-recursive nested elements still work
		var p = new TestComponents.NestedPanel();
		p.InternalRenderTree();
	}
}
