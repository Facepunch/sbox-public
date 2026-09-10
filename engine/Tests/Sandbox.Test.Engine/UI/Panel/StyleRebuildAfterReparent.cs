using Sandbox.UI;

namespace UITests.Panels;

/// <summary>
/// A panel whose selectors were waiting on one root's rebuild pass, then moved to another root
/// before that pass ran, must still get its styles rebuilt under the new root. Menu rows do
/// exactly this: they leave a list as it closes and join a fresh one when it reopens.
/// </summary>
[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class StyleRebuildAfterReparentTest
{
	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		root.StyleSheet.Parse( ".lit { width: 50px; height: 10px; }" );
		return root;
	}

	[TestMethod]
	public void PendingRebuildOnADeadRootDoesNotBlockTheNewRoot()
	{
		var a = CreateRoot();
		var panel = new Panel { Parent = a };
		a.Layout();

		// Queues a selector rebuild on root A, which never runs it
		panel.AddClass( "lit" );

		panel.Parent = null;
		a.Delete( true );

		var b = CreateRoot();
		panel.Parent = b;
		b.Layout();
		b.Layout();

		Assert.AreEqual( 50, panel.Box.Rect.Width, 0.5f, "class rule applied under the new root" );

		// And changes keep working from here on
		panel.RemoveClass( "lit" );
		b.Layout();
		b.Layout();

		Assert.AreNotEqual( 50, panel.Box.Rect.Width, 0.5f, "class rule removed" );
	}
}
