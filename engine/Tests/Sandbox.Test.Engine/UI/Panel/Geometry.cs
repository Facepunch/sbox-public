using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class PanelGeometryTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// After layout the Box exposes the border-box rect, the padding-shrunk
	/// inner rect, the border-shrunk clip rect and the margin-grown outer
	/// rect, plus the raw padding/border sizes.
	/// </summary>
	[TestMethod]
	public void BoxRectsAfterLayout()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = root };
		p.Style.Set( "position: absolute; left: 100px; top: 200px; width: 300px; height: 150px; padding: 10px; border: 5px solid red;" );
		root.Layout();

		Assert.AreEqual( 100, p.Box.Rect.Left );
		Assert.AreEqual( 200, p.Box.Rect.Top );
		Assert.AreEqual( 400, p.Box.Rect.Right );
		Assert.AreEqual( 350, p.Box.Rect.Bottom );

		// The Box convenience accessors mirror the main rect
		Assert.AreEqual( 100, p.Box.Left );
		Assert.AreEqual( 200, p.Box.Top );
		Assert.AreEqual( 400, p.Box.Right );
		Assert.AreEqual( 350, p.Box.Bottom );

		// Padding shrinks the inner rect, borders shrink the clip rect
		Assert.AreEqual( new Rect( 110, 210, 280, 130 ), p.Box.RectInner );
		Assert.AreEqual( new Rect( 105, 205, 290, 140 ), p.Box.ClipRect );

		// No margin - the outer rect matches the border box
		Assert.AreEqual( p.Box.Rect, p.Box.RectOuter );

		Assert.AreEqual( 10, p.Box.Padding.Left );
		Assert.AreEqual( 5, p.Box.Border.Left );
	}

	/// <summary>
	/// IsInside treats the panel's rect edges as inclusive and rejects points
	/// outside of it.
	/// </summary>
	[TestMethod]
	public void PointInsideRect()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = root };
		p.Style.Set( "position: absolute; left: 100px; top: 100px; width: 100px; height: 100px;" );
		root.Layout();

		Assert.IsTrue( p.IsInside( new Vector2( 150, 150 ) ) );
		Assert.IsTrue( p.IsInside( new Vector2( 100, 100 ) ) );
		Assert.IsTrue( p.IsInside( new Vector2( 200, 200 ) ) );
		Assert.IsFalse( p.IsInside( new Vector2( 99, 150 ) ) );
		Assert.IsFalse( p.IsInside( new Vector2( 150, 201 ) ) );
	}

	/// <summary>
	/// With a border radius the rounded corner areas are excluded from
	/// IsInside even though they're within the bounding rect.
	/// </summary>
	[TestMethod]
	public void PointInsideRespectsBorderRadius()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = root };
		p.Style.Set( "position: absolute; left: 100px; top: 100px; width: 100px; height: 100px; border-radius: 50px;" );
		root.Layout();

		// Center and edge midpoints are inside the circle
		Assert.IsTrue( p.IsInside( new Vector2( 150, 150 ) ) );
		Assert.IsTrue( p.IsInside( new Vector2( 150, 101 ) ) );
		Assert.IsTrue( p.IsInside( new Vector2( 101, 150 ) ) );

		// The four rect corners fall outside the rounding
		Assert.IsFalse( p.IsInside( new Vector2( 101, 101 ) ) );
		Assert.IsFalse( p.IsInside( new Vector2( 199, 101 ) ) );
		Assert.IsFalse( p.IsInside( new Vector2( 199, 199 ) ) );
		Assert.IsFalse( p.IsInside( new Vector2( 101, 199 ) ) );
	}

	/// <summary>
	/// FindInRect yields the panel itself followed by every visible
	/// descendant intersecting the queried rect, and skips hidden subtrees.
	/// </summary>
	[TestMethod]
	public void FindInRectIntersection()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var near = new Panel { Parent = root };
		near.Style.Set( "position: absolute; left: 100px; top: 100px; width: 50px; height: 50px;" );

		var far = new Panel { Parent = root };
		far.Style.Set( "position: absolute; left: 500px; top: 500px; width: 50px; height: 50px;" );

		var hidden = new Panel { Parent = root };
		hidden.Style.Set( "position: absolute; left: 110px; top: 110px; width: 10px; height: 10px; display: none;" );

		root.Layout();

		var found = root.FindInRect( new Rect( 90, 90, 100, 100 ), false ).ToArray();

		CollectionAssert.AreEqual( new Panel[] { root, near }, found );
	}

	/// <summary>
	/// With fullyInside the search prunes whole subtrees: the root fails the
	/// containment test so nothing is returned at all, while querying the
	/// contained child directly does find it.
	/// </summary>
	[TestMethod]
	public void FindInRectFullyInsidePrunesAtRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var child = new Panel { Parent = root };
		child.Style.Set( "position: absolute; left: 100px; top: 100px; width: 50px; height: 50px;" );
		root.Layout();

		var query = new Rect( 90, 90, 100, 100 );

		// The root isn't fully inside the query, so the whole tree is pruned
		Assert.AreEqual( 0, root.FindInRect( query, true ).Count() );

		// Asking the contained child itself finds it
		CollectionAssert.AreEqual( new Panel[] { child }, child.FindInRect( query, true ).ToArray() );
	}

	/// <summary>
	/// FindRootPanel walks up to the owning RootPanel and returns null for
	/// detached panels; FindPopupPanel returns the topmost parentless panel.
	/// </summary>
	[TestMethod]
	public void RootPanelLookup()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var mid = new Panel { Parent = root };
		var leaf = new Panel { Parent = mid };

		Assert.AreSame( root, leaf.FindRootPanel() );
		Assert.AreSame( root, root.FindRootPanel() );
		Assert.AreSame( root, leaf.FindPopupPanel() );

		var detached = new Panel();
		Assert.IsNull( detached.FindRootPanel() );
		Assert.AreSame( detached, detached.FindPopupPanel() );
	}

	/// <summary>
	/// opacity: 0 makes a panel and its whole subtree invisible while the
	/// child itself stays visible-self, and restoring the opacity brings
	/// everything back.
	/// </summary>
	[TestMethod]
	public void VisibilityFollowsOpacity()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var parent = new Panel { Parent = root };
		parent.Style.Set( "width: 100px; height: 100px;" );
		var child = new Panel { Parent = parent };
		root.Layout();

		Assert.IsTrue( parent.IsVisible );
		Assert.IsTrue( child.IsVisible );

		parent.Style.Set( "opacity: 0;" );
		root.Layout();

		Assert.IsFalse( parent.IsVisibleSelf );
		Assert.IsFalse( parent.IsVisible );
		Assert.IsFalse( child.IsVisible );
		Assert.IsTrue( child.IsVisibleSelf );

		parent.Style.Set( "opacity: 1;" );
		root.Layout();

		Assert.IsTrue( parent.IsVisible );
		Assert.IsTrue( child.IsVisible );
	}

	/// <summary>
	/// Screen/panel position conversion offsets by the panel's rect, and the
	/// delta conversion normalises into 0-1 across the panel's size.
	/// </summary>
	[TestMethod]
	public void ScreenPositionConversion()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = root };
		p.Style.Set( "position: absolute; left: 100px; top: 200px; width: 300px; height: 150px;" );
		root.Layout();

		Assert.AreEqual( new Vector2( 10, 20 ), p.ScreenPositionToPanelPosition( new Vector2( 110, 220 ) ) );
		Assert.AreEqual( new Vector2( 110, 220 ), p.PanelPositionToScreenPosition( new Vector2( 10, 20 ) ) );
		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), p.ScreenPositionToPanelDelta( new Vector2( 250, 275 ) ) );
	}

	/// <summary>
	/// The root's UI scale cascades into ScaleToScreen/ScaleFromScreen and
	/// multiplies every styled length when the panel is laid out.
	/// </summary>
	[TestMethod]
	public void ScaleCascadesFromRoot()
	{
		var root = new ScalingRootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.SetUiScale( 2.0f );
		root.Layout();

		Assert.AreEqual( 2.0f, root.ScaleToScreen );

		var p = new Panel { Parent = root };

		// ScaleToScreen is inherited from the parent as soon as we attach
		Assert.AreEqual( 2.0f, p.ScaleToScreen );
		Assert.AreEqual( 0.5f, p.ScaleFromScreen );

		p.Style.Set( "position: absolute; left: 100px; top: 100px; width: 100px; height: 50px;" );
		root.Layout();

		Assert.AreEqual( 2.0f, p.ScaleToScreen );
		Assert.AreEqual( new Rect( 200, 200, 200, 100 ), p.Box.Rect );
	}
}

/// <summary>
/// RootPanel subclass that lets tests choose the UI scale, which is normally
/// derived from the screen size.
/// </summary>
public class ScalingRootPanel : RootPanel
{
	public void SetUiScale( float scale )
	{
		Scale = scale;
	}
}
