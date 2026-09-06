using System;
using System.Collections.Generic;
using Sandbox.Layout;

namespace LayoutTests.Core;

[TestClass]
public class LayoutRegressionTests
{
	[TestMethod]
	[DataRow( Display.Flex, 40f, 0f )]
	[DataRow( Display.Block, 100f, 100f )]
	[DataRow( Display.Grid, 100f, 100f )]
	public void ConflictingRootBoundsRespectFormattingContext( object display, float width, float height )
	{
		var root = new LayoutNode();
		root.Style.Display = (Display)display;
		root.Style.MinWidth = 100;
		root.Style.MaxWidth = 40;
		root.Style.MinHeight = 100;
		root.Style.MaxHeight = 0;
		var child = new LayoutNode();
		child.Style.Width = 20;
		child.Style.Height = 20;
		root.AddChild( child );

		root.CalculateLayout();
		Assert.AreEqual( width, root.LayoutWidth );
		Assert.AreEqual( height, root.LayoutHeight );
		root.Style.SetPadding( Edge.Vertical, 3 );
		root.CalculateLayout();
		Assert.AreEqual( MathF.Max( 6, height ), root.LayoutHeight, "Padding remains the border-box floor." );
	}

	[TestMethod]
	[DataRow( Display.Flex, Display.Flex, 40f, 0f )]
	[DataRow( Display.Flex, Display.Block, 100f, 100f )]
	[DataRow( Display.Flex, Display.Grid, 100f, 100f )]
	[DataRow( Display.Block, Display.Flex, 100f, 100f )]
	[DataRow( Display.Block, Display.Block, 100f, 100f )]
	[DataRow( Display.Block, Display.Grid, 100f, 100f )]
	[DataRow( Display.Grid, Display.Flex, 100f, 100f )]
	[DataRow( Display.Grid, Display.Block, 100f, 100f )]
	[DataRow( Display.Grid, Display.Grid, 100f, 100f )]
	public void ConflictingChildBoundsRespectOwnAndOwnerDisplay( object ownerDisplay, object childDisplay, float width, float height )
	{
		foreach ( var contentsDepth in new[] { 0, 2 } )
		{
			var root = new LayoutNode();
			root.Style.Display = (Display)ownerDisplay;
			root.Style.Width = 300;
			root.Style.Height = 300;
			var owner = root;
			for ( int i = 0; i < contentsDepth; i++ )
			{
				var contents = new LayoutNode();
				contents.Style.Display = Display.Contents;
				owner.AddChild( contents );
				owner = contents;
			}
			var child = new LayoutNode();
			child.Style.Display = (Display)childDisplay;
			child.Style.MinWidth = 100;
			child.Style.MaxWidth = 40;
			child.Style.MinHeight = 100;
			child.Style.MaxHeight = 0;
			owner.AddChild( child );
			// Exercise both the leaf shortcut and the container's own layout algorithm.
			for ( int pass = 0; pass < 2; pass++ )
			{
				root.CalculateLayout();
				Assert.AreEqual( width, child.LayoutWidth, $"Contents depth {contentsDepth}, pass {pass}" );
				Assert.AreEqual( height, child.LayoutHeight, $"Contents depth {contentsDepth}, pass {pass}" );
				if ( pass == 0 ) child.AddChild( new LayoutNode() );
			}
		}
	}

	[TestMethod]
	[DataRow( Display.Flex )]
	[DataRow( Display.Block )]
	[DataRow( Display.Grid )]
	public void AbsoluteBlockItemsUseCssConflictingBounds( object childDisplay )
	{
		var root = new LayoutNode();
		root.Style.Display = Display.Block;
		root.Style.Width = 200;
		root.Style.Height = 200;
		var contents = new LayoutNode();
		contents.Style.Display = Display.Contents;
		root.AddChild( contents );
		var child = new LayoutNode();
		child.Style.Display = (Display)childDisplay;
		child.Style.PositionType = PositionType.Absolute;
		child.Style.MinWidth = 100;
		child.Style.MaxWidth = 40;
		child.Style.MinHeight = 100;
		child.Style.MaxHeight = 0;
		child.Style.SetPosition( Edge.Right, 10 );
		child.Style.SetPosition( Edge.Bottom, 20 );
		contents.AddChild( child );
		root.CalculateLayout();
		Assert.AreEqual( 100f, child.LayoutWidth );
		Assert.AreEqual( 100f, child.LayoutHeight );
		Assert.AreEqual( 90f, child.LayoutLeft );
		Assert.AreEqual( 80f, child.LayoutTop );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ConflictingBoundsFollowOwnerDisplayChanges( bool measured )
	{
		var root = new LayoutNode();
		root.Style.Width = 300;
		root.Style.Height = 300;
		var contents = new LayoutNode();
		contents.Style.Display = Display.Contents;
		root.AddChild( contents );
		var child = new LayoutNode();
		child.Style.MinWidth = 100;
		child.Style.MaxWidth = 40;
		child.Style.MinHeight = 100;
		child.Style.MaxHeight = 0;
		if ( measured ) child.MeasureFunc = static ( _, _, _, _, _ ) => new LayoutSize( 20, 20 );
		contents.AddChild( child );
		foreach ( var display in new[] { Display.Flex, Display.Block, Display.Grid, Display.Flex } )
		{
			root.Style.Display = display;
			root.CalculateLayout();
			Assert.AreEqual( display == Display.Flex ? 40f : 100f, child.LayoutWidth, display.ToString() );
			Assert.AreEqual( display == Display.Flex ? 0f : 100f, child.LayoutHeight, display.ToString() );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MaxHeightZeroCollapsesFlexChildWithMinimum( bool measured )
	{
		var root = new LayoutNode();
		root.Style.FlexDirection = FlexDirection.Column;
		root.Style.Width = 100;
		var child = new LayoutNode();
		child.Style.MinHeight = 50;
		child.Style.MaxHeight = 0;
		if ( measured ) child.MeasureFunc = static ( _, _, _, _, _ ) => new LayoutSize( 10, 20 );
		root.AddChild( child );
		root.CalculateLayout();
		Assert.AreEqual( 0f, child.LayoutHeight );
		Assert.AreEqual( 0f, root.LayoutHeight );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( 10f )]
	public void BlockMeasurementComputesEscapingMarginsBeforeFlexBasis( float height )
	{
		var root = new LayoutNode();
		root.Style.FlexDirection = FlexDirection.Column;
		root.Style.Width = 100;
		var outer = new LayoutNode();
		outer.Style.Display = Display.Block;
		var inner = new LayoutNode();
		inner.Style.Display = Display.Block;
		inner.Style.Height = height;
		var child = new LayoutNode();
		child.Style.Display = Display.Block;
		child.Style.Height = height;
		child.Style.SetMargin( Edge.Top, 20 );
		inner.AddChild( child );
		outer.AddChild( inner );
		root.AddChild( outer );

		for ( int pass = 0; pass < 3; pass++ )
		{
			var margin = 20 + pass * 10;
			child.Style.SetMargin( Edge.Top, margin );
			root.CalculateLayout();
			Assert.AreEqual( height + margin, root.LayoutHeight, $"Pass {pass}: root" );
			Assert.AreEqual( height + margin, outer.LayoutHeight, $"Pass {pass}: flex basis" );
			Assert.AreEqual( (float)margin, inner.LayoutTop );
			Assert.AreEqual( 0f, child.LayoutTop );
		}
	}

	[TestMethod]
	[DataRow( Display.Flex )]
	[DataRow( Display.Block )]
	public void AutoWidthContainerShapesMeasuredLeafOnce( object display )
	{
		var root = new LayoutNode();
		root.Style.Display = (Display)display;
		var calls = 0;
		var child = new LayoutNode
		{
			MeasureFunc = ( _, _, _, _, _ ) =>
			{
				calls++;
				return new LayoutSize( 80, 20 );
			},
		};
		root.AddChild( child );
		root.CalculateLayout();
		Assert.AreEqual( 80f, root.LayoutWidth );
		Assert.AreEqual( 20f, root.LayoutHeight );
		Assert.AreEqual( 1, calls );
		root.CalculateLayout();
		Assert.AreEqual( 1, calls );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MeasuredLeafOwnerComparisonIsOnlyNeededForPercentages( bool percentages )
	{
		var calls = 0;
		var node = new LayoutNode
		{
			MeasureFunc = ( _, _, _, _, _ ) =>
			{
				calls++;
				return new LayoutSize( 10, 10 );
			},
		};
		node.Style.SetPadding( Edge.Left, percentages ? StyleLength.Percent( 10 ) : 10 );
		node.ProcessDimensions();
		var generation = LayoutNode.NextGeneration();
		LayoutAlgorithm.CalculateLayoutInternal( node, 100, 50, Direction.LTR, SizingMode.FitContent, SizingMode.FitContent, 200, 100, false, 0, generation );
		LayoutAlgorithm.CalculateLayoutInternal( node, 100, 50, Direction.LTR, SizingMode.FitContent, SizingMode.FitContent, 400, 200, true, 0, generation );
		LayoutAlgorithm.CalculateLayoutInternal( node, 100, 50, Direction.LTR, SizingMode.FitContent, SizingMode.FitContent, 600, 300, true, 0, generation );
		Assert.AreEqual( percentages ? 3 : 1, calls );
		Assert.AreEqual( percentages ? 60f : 10f, node.LayoutPadding( PhysicalEdge.Left ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ContainerReuseStillResolvesDescendantPercentages( bool performLayout )
	{
		var container = new LayoutNode();
		container.Style.FlexDirection = FlexDirection.Column;
		var child = new LayoutNode();
		child.Style.MinHeight = StyleLength.Percent( 50 );
		container.AddChild( child );
		container.ProcessDimensions();
		var generation = LayoutNode.NextGeneration();
		LayoutAlgorithm.CalculateLayoutInternal( container, 100, 200, Direction.LTR, SizingMode.StretchFit, SizingMode.StretchFit, 100, 200, performLayout, 0, generation );
		var visited = LayoutAlgorithm.CalculateLayoutInternal( container, 100, 200, Direction.LTR, SizingMode.StretchFit, SizingMode.StretchFit, 100, 400, performLayout, 0, generation );
		Assert.IsTrue( visited, "Owner changes must not reuse a container entry based only on its own percentage-free style." );
		LayoutAlgorithm.CalculateLayoutInternal( container, 100, 400, Direction.LTR, SizingMode.StretchFit, SizingMode.StretchFit, 100, 400, true, 0, generation );
		Assert.AreEqual( 200f, child.LayoutHeight );
	}

	[TestMethod]
	public void DescendantPercentagesBlockLooseContainerMeasurementReuse()
	{
		var container = new LayoutNode();
		container.Style.FlexDirection = FlexDirection.Column;
		var child = new LayoutNode();
		child.Style.Height = StyleLength.Percent( 50 );
		container.AddChild( child );
		container.ProcessDimensions();
		var generation = LayoutNode.NextGeneration();
		LayoutAlgorithm.CalculateLayoutInternal( container, 100, 400, Direction.LTR, SizingMode.StretchFit, SizingMode.FitContent, 100, 400, false, 0, generation, MeasureScope.Height );
		Assert.AreEqual( 200f, container.Layout.MeasuredDimension( Dimension.Height ) );
		LayoutAlgorithm.CalculateLayoutInternal( container, 100, 300, Direction.LTR, SizingMode.StretchFit, SizingMode.FitContent, 100, 400, false, 0, generation, MeasureScope.Height );
		Assert.AreEqual( 150f, container.Layout.MeasuredDimension( Dimension.Height ), "The previous 200px result still fits, but the percentage basis changed." );
	}

	[TestMethod]
	[DataRow( "auto" )]
	[DataRow( " \tAUTO\r\n" )]
	public void AutoPlacementDoesNotAllocate( string text )
	{
		GridParser.TryParsePlacement( text, out _ );
		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
			GridParser.TryParsePlacement( text, out _ );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( 0L, allocated );
		Assert.IsTrue( GridParser.TryParsePlacement( text, out var result ) );
		Assert.AreEqual( GridPlacement.Auto, result );
		Assert.IsFalse( GridParser.TryParsePlacement( "auto extra", out _ ) );
	}

	[TestMethod]
	[DataRow( Display.Flex, 40, 1 )]
	[DataRow( Display.Flex, 1, 600 )]
	[DataRow( Display.Block, 40, 1 )]
	[DataRow( Display.Block, 1, 600 )]
	[DataRow( Display.Grid, 1, 600 )]
	[DataRow( Display.Grid, 1, 1500 )]
	public void WarmLayoutsBeyondOldPoolLimitsDoNotAllocate( object displayValue, int depth, int breadth )
	{
		var display = (Display)displayValue;
		var root = new LayoutNode();
		var nodes = new List<LayoutNode> { root };
		var current = root;
		for ( int d = 0; d < depth; d++ )
		{
			current.Style.Display = display;
			current.Style.Width = 1000;
			current.Style.Height = 1000;
			if ( display == Display.Grid )
				current.Style.GridTemplateColumns = new TrackList( TrackSizingFunction.Points( 10 ) );
			for ( int i = 0; i < breadth; i++ )
			{
				var child = new LayoutNode();
				child.Style.Width = 1;
				child.Style.Height = 1;
				current.AddChild( child );
				nodes.Add( child );
			}
			current = current.GetChild( 0 );
		}

		long allocated = 0;
		for ( int pass = 0; pass < 12; pass++ )
		{
			foreach ( var node in nodes ) node.MarkDirty();
			var before = GC.GetAllocatedBytesForCurrentThread();
			root.CalculateLayout();
			if ( pass >= 8 ) allocated += GC.GetAllocatedBytesForCurrentThread() - before;
		}
		Assert.AreEqual( 0L, allocated, $"{display}, depth {depth}, breadth {breadth}: bytes across four warm dirty layouts" );
	}

	[TestMethod]
	[DataRow( 0, 128 )]
	[DataRow( 4096, 8 )]
	[DataRow( 8192, 0 )]
	public void NodeListRetentionHasCountCapacityAndTotalBounds( int capacity, int expectedRetained )
	{
		var lists = new List<LayoutNode>[160];
		var returned = new HashSet<List<LayoutNode>>();
		for ( int i = 0; i < lists.Length; i++ )
		{
			lists[i] = LayoutAlgorithm.RentList();
			lists[i].Capacity = capacity;
			returned.Add( lists[i] );
		}
		foreach ( var list in lists ) LayoutAlgorithm.ReturnList( list );
		var retained = 0;
		for ( int i = 0; i < lists.Length; i++ )
		{
			lists[i] = LayoutAlgorithm.RentList();
			if ( returned.Contains( lists[i] ) ) retained++;
			Assert.AreEqual( 0, lists[i].Count );
		}
		Assert.AreEqual( expectedRetained, retained );
		foreach ( var list in lists ) LayoutAlgorithm.ReturnList( list );
	}

	[TestMethod]
	[DataRow( 0, 4096 )]
	[DataRow( 4096, 8 )]
	[DataRow( 8192, 0 )]
	public void GridRetentionHasCountCapacityAndTotalBounds( int capacity, int expectedRetained )
	{
		var items = new object[5000];
		var returned = new HashSet<object>();
		for ( int i = 0; i < items.Length; i++ )
		{
			items[i] = GridPool<object>.Rent();
			returned.Add( items[i] );
		}
		foreach ( var item in items ) GridPool<object>.Return( item, capacity );
		var retained = 0;
		for ( int i = 0; i < items.Length; i++ )
		{
			if ( returned.Contains( GridPool<object>.Rent() ) ) retained++;
		}
		Assert.AreEqual( expectedRetained, retained );
	}
}
