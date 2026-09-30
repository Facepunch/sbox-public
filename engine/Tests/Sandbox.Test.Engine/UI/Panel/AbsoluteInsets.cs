using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

/// <summary>
/// An absolutely positioned panel with opposing insets takes its size from them. Which box those
/// insets resolve against is the whole question - see issue 11747.
/// </summary>
[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class AbsoluteInsetsTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// The simple case - straight child of a sized root.
	/// </summary>
	[TestMethod]
	public void InsetsFillTheRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var backdrop = new Panel { Parent = root };
		backdrop.Style.Set( "position: absolute; top: 0; left: 0; right: 0; bottom: 0;" );

		root.Layout();

		Assert.AreEqual( new Rect( 0, 0, 1000, 1000 ), backdrop.Box.Rect );
	}

	/// <summary>
	/// What the razor component in the issue actually builds: the component's own panel has no
	/// style of its own, and the only thing in it is the absolute backdrop.
	/// </summary>
	[TestMethod]
	public void InsetsFillAnUnstyledParent()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var component = new Panel { Parent = root };

		var backdrop = new Panel { Parent = component };
		backdrop.Style.Set( "position: absolute; top: 0; left: 0; right: 0; bottom: 0;" );

		root.Layout();

		Assert.AreEqual( new Rect( 0, 0, 1000, 1000 ), backdrop.Box.Rect, $"parent was {component.Box.Rect}" );
	}

	/// <summary>
	/// The same again, only the backdrop turns up after a layout has already run - the @if in the
	/// issue toggling it into the tree.
	/// </summary>
	[TestMethod]
	public void InsetsFillAnUnstyledParentWhenAddedLate()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var component = new Panel { Parent = root };

		root.Layout();

		var backdrop = new Panel { Parent = component };
		backdrop.Style.Set( "position: absolute; top: 0; left: 0; right: 0; bottom: 0;" );

		root.Layout();

		Assert.AreEqual( new Rect( 0, 0, 1000, 1000 ), backdrop.Box.Rect, $"parent was {component.Box.Rect}" );
	}

	/// <summary>
	/// A parent that is sized itself is the containing block either way - this one has never been
	/// in question, and pins down that absolute stays relative to its parent.
	/// </summary>
	[TestMethod]
	public void InsetsFillASizedParent()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var component = new Panel { Parent = root };
		component.Style.Set( "width: 400px; height: 300px; left: 100px; top: 50px; position: absolute;" );

		var backdrop = new Panel { Parent = component };
		backdrop.Style.Set( "position: absolute; top: 0; left: 0; right: 0; bottom: 0;" );

		root.Layout();

		Assert.AreEqual( new Rect( 100, 50, 400, 300 ), backdrop.Box.Rect );
	}
}
