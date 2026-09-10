using Sandbox.Layout;

namespace LayoutTests.FlexConformance;

[TestClass]
public class FixtureGeometryTests
{
	[TestMethod]
	public void AccumulatesUnroundedAncestorsWithoutChangingLayoutOrCache()
	{
		var root = Box( 0.4f, 100 );
		var child = Box( 0.4f, 10 );
		var grandchild = Box( 0.4f, 0.4f );
		root.AddChild( child );
		child.AddChild( grandchild );
		root.CalculateLayout();

		var raw = FixtureGeometry.GetRect( grandchild, false );
		Assert.AreEqual( (0.4f, 0.4f, 0.4f, 0.4f), raw );
		var generation = grandchild.Layout.GenerationCount;
		var cachedLayout = grandchild.Layout.CachedLayout;
		root.HasNewLayout = child.HasNewLayout = grandchild.HasNewLayout = false;

		for ( int i = 0; i < 2; i++ )
		{
			Assert.AreEqual( (0f, 0f, 1f, 1f), FixtureGeometry.GetRect( grandchild, true ) );
			Assert.AreEqual( raw, FixtureGeometry.GetRect( grandchild, false ) );
			Assert.AreEqual( generation, grandchild.Layout.GenerationCount );
			Assert.AreEqual( cachedLayout, grandchild.Layout.CachedLayout );
			Assert.IsFalse( root.HasNewLayout || child.HasNewLayout || grandchild.HasNewLayout );
			Assert.IsFalse( root.IsDirty || child.IsDirty || grandchild.IsDirty );
		}

		root.CalculateLayout();
		Assert.AreEqual( raw, FixtureGeometry.GetRect( grandchild, false ) );
	}

	[TestMethod]
	[DataRow( 1.2f, 2f )]
	[DataRow( 1f, 1f )]
	[DataRow( 0.99995f, 1f )]
	public void MeasuredTextFloorsPositionsAndPreservesIntegralSizes( float size, float expectedSize )
	{
		var root = Box( 0.4f, 100 );
		var text = Box( -0.6f, size );
		text.MeasureFunc = ( _, _, _, _, _ ) => new LayoutSize( size, size );
		root.AddChild( text );
		root.CalculateLayout();

		Assert.AreEqual( (-1f, -1f, expectedSize, expectedSize), FixtureGeometry.GetRect( text, true ) );
		Assert.AreEqual( (-0.6f, -0.6f, size, size), FixtureGeometry.GetRect( text, false ) );
	}

	[TestMethod]
	[DataRow( -0.5f, 0f )]
	[DataRow( 0.49995f, 1f )]
	[DataRow( -0.5002f, -1f )]
	public void RoundsNegativeAndNearHalfPositionsLikeUpstream( float position, float expected )
	{
		var node = Box( position, 1 );
		node.CalculateLayout();
		Assert.AreEqual( (expected, expected, 1f, 1f), FixtureGeometry.GetRect( node, true ) );
	}

	[TestMethod]
	public void FixedPositionsResetTheAccumulatedOrigin()
	{
		var root = Box( 0.4f, 100 );
		var fixedNode = Box( 0.4f, 10 );
		fixedNode.Style.PositionType = PositionType.Fixed;
		var child = Box( 0.4f, 0.4f );
		root.AddChild( fixedNode );
		fixedNode.AddChild( child );
		root.CalculateLayout();

		Assert.AreEqual( (0f, 0f, 0f, 0f), FixtureGeometry.GetRect( child, true ) );
	}

	private static LayoutNode Box( float position, float size )
	{
		var node = new LayoutNode();
		node.Style.PositionType = PositionType.Absolute;
		node.Style.SetPosition( Edge.Left, StyleLength.Points( position ) );
		node.Style.SetPosition( Edge.Top, StyleLength.Points( position ) );
		node.Style.Width = StyleLength.Points( size );
		node.Style.Height = StyleLength.Points( size );
		return node;
	}
}
