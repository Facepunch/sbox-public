using Sandbox.Layout;

namespace LayoutTests.Core;

[TestClass]
public class FixedLayoutTests
{
	private static LayoutNode Box( float width, float height )
	{
		var node = new LayoutNode();
		node.Style.Width = width;
		node.Style.Height = height;
		return node;
	}

	[TestMethod]
	public void FixedSubtreeCountsFollowStyleAndOwnershipChanges()
	{
		var root = Box( 800, 600 );
		var other = Box( 400, 300 );
		var host = Box( 100, 100 );
		var a = Box( 20, 20 );
		var b = Box( 20, 20 );
		host.AddChild( a );
		host.AddChild( b );
		root.AddChild( host );
		Assert.IsFalse( root.SubtreeHasFixed );
		a.Style.PositionType = PositionType.Fixed;
		b.Style.PositionType = PositionType.Fixed;
		Assert.IsTrue( root.SubtreeHasFixed );
		a.Style.PositionType = PositionType.Relative;
		Assert.IsTrue( root.SubtreeHasFixed );
		host.Style.Display = Display.None;
		Assert.IsTrue( root.SubtreeHasFixed );
		root.RemoveChild( host );
		Assert.IsFalse( root.SubtreeHasFixed );
		other.AddChild( host );
		Assert.IsTrue( other.SubtreeHasFixed );
		host.Style.Display = Display.Contents;
		other.CalculateLayout();
		Assert.AreEqual( 20f, b.LayoutWidth );
		host.RemoveAllChildren();
		Assert.IsFalse( host.SubtreeHasFixed );
		Assert.IsFalse( other.SubtreeHasFixed );
		Assert.IsTrue( b.SubtreeHasFixed );
		root.AddChild( b );
		b.Style.PositionType = PositionType.Absolute;
		Assert.IsFalse( root.SubtreeHasFixed );
	}

	[TestMethod]
	public void CachedOwnerDoesNotOverwriteFixedViewportPosition()
	{
		var root = Box( 800, 600 );
		var host = Box( 100, 100 );
		var node = Box( 20, 30 );
		node.Style.PositionType = PositionType.Fixed;
		node.Style.SetPosition( Edge.Right, 10 );
		host.AddChild( node );
		root.AddChild( host );
		var sibling = Box( 50, 50 );
		root.AddChild( sibling );
		root.CalculateLayout();
		Assert.AreEqual( 770f, node.LayoutLeft );
		node.HasNewLayout = false;
		sibling.Style.Width = 60;
		root.CalculateLayout();
		Assert.AreEqual( 770f, node.LayoutLeft );
		node.Style.SetPosition( Edge.Right, 20 );
		root.CalculateLayout();
		Assert.AreEqual( 760f, node.LayoutLeft );
		Assert.IsFalse( node.IsDirty );
		Assert.IsFalse( root.IsDirty );
	}

	[TestMethod]
	public void ViewportInsetsCrossEveryContainingBlockWithoutChangingOwnership()
	{
		foreach ( var display in new[] { Display.Flex, Display.Block, Display.Grid, Display.Contents } )
			foreach ( var position in new[] { PositionType.Static, PositionType.Relative, PositionType.Absolute, PositionType.Fixed } )
			{
				var root = Box( 800, 600 );
				root.Style.SetPadding( Edge.All, 40 );
				root.Style.SetBorder( Edge.All, 10 );
				var host = Box( 200, 100 );
				host.Style.Display = display;
				host.Style.PositionType = position;
				host.Style.SetPosition( Edge.Left, 70 );
				host.Style.SetPosition( Edge.Top, 90 );
				root.AddChild( host );
				var fixedNode = Box( 100, 50 );
				fixedNode.Style.PositionType = PositionType.Fixed;
				fixedNode.Style.Width = StyleLength.Percent( 25 );
				fixedNode.Style.Height = StyleLength.Percent( 20 );
				fixedNode.Style.SetPosition( Edge.Right, StyleLength.Percent( 10 ) );
				fixedNode.Style.SetPosition( Edge.Bottom, 30 );
				host.AddChild( fixedNode );
				var child = Box( 20, 10 );
				fixedNode.AddChild( child );

				root.CalculateLayout();
				Assert.AreSame( host, fixedNode.Owner );
				Assert.AreEqual( 520f, fixedNode.LayoutLeft, $"{display}/{position}" );
				Assert.AreEqual( 450f, fixedNode.LayoutTop );
				Assert.AreEqual( 200f, fixedNode.LayoutWidth );
				Assert.AreEqual( 120f, fixedNode.LayoutHeight );
				Assert.AreEqual( 0f, child.LayoutLeft );
				Assert.AreEqual( 0f, child.LayoutTop );
				root.Style.Width = 1000;
				root.Style.Height = 800;
				root.CalculateLayout();
				Assert.AreEqual( 650f, fixedNode.LayoutLeft );
				Assert.AreEqual( 610f, fixedNode.LayoutTop );
			}
	}

	[TestMethod]
	public void FixedHasNoIntrinsicFlowOrGridTrackContribution()
	{
		foreach ( var display in new[] { Display.Flex, Display.Block, Display.Grid } )
		{
			var root = Box( 800, 600 );
			root.Style.AlignItems = Align.FlexStart;
			var host = Box( 100, 0 );
			host.Style.Height = StyleLength.Auto;
			host.Style.Display = display;
			root.AddChild( host );
			var flow = Box( 20, 30 );
			host.AddChild( flow );
			var fixedNode = Box( 1000, 2000 );
			fixedNode.Style.PositionType = PositionType.Fixed;
			host.AddChild( fixedNode );
			root.CalculateLayout();
			Assert.AreEqual( 30f, host.LayoutHeight, display.ToString() );
			Assert.AreEqual( 20f, flow.LayoutWidth );
			Assert.AreEqual( 0f, fixedNode.LayoutLeft );
			Assert.AreEqual( 0f, fixedNode.LayoutTop );
		}
	}

	[TestMethod]
	public void CssSolverHandlesStretchAutoMarginsAspectRatioAndShrinkToFit()
	{
		var root = Box( 800, 600 );
		var node = Box( 100, 50 );
		node.Style.PositionType = PositionType.Fixed;
		root.AddChild( node );
		node.Style.SetPosition( Edge.Left, 20 );
		node.Style.SetPosition( Edge.Right, 40 );
		node.Style.SetMargin( Edge.Left, StyleLength.Auto );
		node.Style.SetMargin( Edge.Right, StyleLength.Auto );
		root.CalculateLayout();
		Assert.AreEqual( 340f, node.LayoutLeft );
		node.Style.Width = StyleLength.Auto;
		node.Style.Height = StyleLength.Auto;
		node.Style.AspectRatio = 2;
		node.Style.MaxWidth = 400;
		root.CalculateLayout();
		Assert.AreEqual( 400f, node.LayoutWidth );
		Assert.AreEqual( 200f, node.LayoutHeight );
		node.Style.SetPosition( Edge.Left, StyleLength.Auto );
		node.Style.SetPosition( Edge.Right, StyleLength.Auto );
		node.Style.AspectRatio = float.NaN;
		node.AddChild( Box( 120, 35 ) );
		root.CalculateLayout();
		Assert.AreEqual( 120f, node.LayoutWidth );
		Assert.AreEqual( 35f, node.LayoutHeight );
		Assert.AreEqual( 0f, node.LayoutLeft );
	}

	[TestMethod]
	public void NestedFixedUpdatesHidingTogglingAndMovingRoots()
	{
		var root = Box( 800, 600 );
		var host = Box( 100, 100 );
		root.AddChild( host );
		var node = Box( 20, 30 );
		node.Style.PositionType = PositionType.Fixed;
		node.Style.SetPosition( Edge.Right, 10 );
		host.AddChild( node );
		var nested = Box( 10, 10 );
		nested.Style.PositionType = PositionType.Fixed;
		nested.Style.SetPosition( Edge.Bottom, 10 );
		node.AddChild( nested );
		root.CalculateLayout();
		Assert.AreEqual( 580f, nested.LayoutTop );
		nested.Style.SetPosition( Edge.Bottom, 20 );
		root.CalculateLayout();
		Assert.AreEqual( 570f, nested.LayoutTop );
		host.Style.Display = Display.None;
		root.CalculateLayout();
		Assert.AreEqual( 0f, node.LayoutWidth );
		host.Style.Display = Display.Flex;
		root.CalculateLayout();
		Assert.AreEqual( 770f, node.LayoutLeft );
		node.Style.PositionType = PositionType.Relative;
		root.CalculateLayout();
		Assert.AreNotEqual( 770f, node.LayoutLeft );
		node.Style.PositionType = PositionType.Fixed;
		host.RemoveChild( node );
		var other = Box( 400, 300 );
		other.AddChild( node );
		other.CalculateLayout();
		Assert.AreEqual( 370f, node.LayoutLeft );
		Assert.AreEqual( 270f, nested.LayoutTop );
		Assert.AreSame( other, node.Owner );
	}

	[TestMethod]
	public void FixedContainsAbsoluteDescendantsAndNestedFixedStillUsesViewport()
	{
		var root = Box( 800, 600 );
		var fixedNode = Box( 200, 100 );
		fixedNode.Style.PositionType = PositionType.Fixed;
		fixedNode.Style.Display = Display.Block;
		fixedNode.Style.SetPosition( Edge.Left, 300 );
		fixedNode.Style.SetPosition( Edge.Top, 200 );
		root.AddChild( fixedNode );
		var host = Box( 100, 50 );
		host.Style.Display = Display.Block;
		host.Style.PositionType = PositionType.Static;
		fixedNode.AddChild( host );
		var absolute = Box( 20, 10 );
		absolute.Style.PositionType = PositionType.Absolute;
		absolute.Style.SetPosition( Edge.Right, 10 );
		absolute.Style.SetPosition( Edge.Bottom, 5 );
		host.AddChild( absolute );
		var nested = Box( 10, 10 );
		nested.Style.PositionType = PositionType.Fixed;
		nested.Style.SetPosition( Edge.Right, 10 );
		absolute.AddChild( nested );
		root.CalculateLayout();
		Assert.AreEqual( 170f, absolute.LayoutLeft );
		Assert.AreEqual( 85f, absolute.LayoutTop );
		Assert.AreEqual( 780f, nested.LayoutLeft );
		Assert.AreEqual( 0f, nested.LayoutTop );
		Assert.AreSame( absolute, nested.Owner );
		root.CalculateLayout();
		Assert.AreEqual( 780f, nested.LayoutLeft );
	}

	[TestMethod]
	public void FractionalSizesAndUnsetInsetsIgnoreOwnerAndRtlStaticAnchor()
	{
		var root = new LayoutNode();
		root.Style.Width = 800;
		root.Style.Height = 600;
		root.Style.Direction = Direction.RTL;
		var host = new LayoutNode();
		host.Style.SetMargin( Edge.Left, 0.4f );
		host.Style.SetMargin( Edge.Top, 0.4f );
		root.AddChild( host );
		var node = new LayoutNode();
		node.Style.PositionType = PositionType.Fixed;
		node.Style.Width = 20.4f;
		node.Style.Height = 10.4f;
		host.AddChild( node );
		root.CalculateLayout();
		Assert.AreEqual( 0f, node.LayoutLeft );
		Assert.AreEqual( 0f, node.LayoutTop );
		Assert.AreEqual( 20.4f, node.LayoutWidth, 0.001f );
		Assert.AreEqual( 10.4f, node.LayoutHeight, 0.001f );
		Assert.AreSame( host, node.Owner );
	}
}
