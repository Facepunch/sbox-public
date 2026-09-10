using System;
using System.Collections.Generic;
using Sandbox.Layout;

namespace LayoutTests.Core;

[TestClass]
public class LayoutCoreHardeningTests
{
	[TestMethod]
	public void TreeRejectsSelfAndAncestorCycles()
	{
		var root = new LayoutNode();
		var child = new LayoutNode();
		var grandchild = new LayoutNode();
		root.AddChild( child );
		child.AddChild( grandchild );

		Assert.ThrowsException<InvalidOperationException>( () => root.AddChild( root ) );
		Assert.ThrowsException<InvalidOperationException>( () => grandchild.AddChild( root ) );

		Assert.AreSame( root, child.Owner );
		Assert.AreSame( child, grandchild.Owner );
		Assert.AreEqual( 1, root.ChildCount );
		Assert.AreEqual( 1, child.ChildCount );
		Assert.AreEqual( 0, grandchild.ChildCount );
	}

	[TestMethod]
	public void ChildrenCannotMutateTheTree()
	{
		var root = new LayoutNode();
		var child = new LayoutNode();
		root.AddChild( child );

		var children = (ICollection<LayoutNode>)root.Children;
		Assert.IsTrue( children.IsReadOnly );
		Assert.ThrowsException<NotSupportedException>( () => children.Add( new LayoutNode() ) );
		Assert.AreSame( root, child.Owner );
		Assert.AreEqual( 1, root.ChildCount );
	}

	[TestMethod]
	public void ResultAffectingCallbacksAndFlagsDirtyAncestors()
	{
		var root = new LayoutNode();
		var child = new LayoutNode
		{
			MeasureFunc = static ( _, _, _, _, _ ) => new LayoutSize( 10, 10 ),
			BaselineFunc = static ( _, _, _ ) => 8,
		};
		root.AddChild( child );
		root.CalculateLayout();

		child.MeasureFunc = static ( _, _, _, _, _ ) => new LayoutSize( 20, 10 );
		Assert.IsTrue( child.IsDirty );
		Assert.IsTrue( root.IsDirty );
		root.CalculateLayout();
		Assert.AreEqual( 20, child.LayoutWidth );

		child.BaselineFunc = static ( _, _, _ ) => 7;
		Assert.IsTrue( root.IsDirty );
		root.CalculateLayout();

		child.IsReferenceBaseline = true;
		Assert.IsTrue( root.IsDirty );
		root.CalculateLayout();

		child.AlwaysFormsContainingBlock = true;
		Assert.IsTrue( root.IsDirty );
	}

	[TestMethod]
	public void ResetClearsDirtiedCallbackBeforeResettingStyle()
	{
		var node = new LayoutNode();
		node.Style.Width = 20;
		node.CalculateLayout();
		var dirtiedCalls = 0;
		node.DirtiedCallback = _ => dirtiedCalls++;

		node.Reset();

		Assert.AreEqual( 0, dirtiedCalls );
		Assert.IsNull( node.DirtiedCallback );
		Assert.IsTrue( node.IsDirty );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CssDefaultsApplyToNewAndResetNodes( bool reset )
	{
		var node = new LayoutNode();
		if ( reset )
		{
			node.Style.FlexDirection = FlexDirection.Column;
			node.Style.AlignContent = Align.FlexStart;
			node.Style.JustifyContent = Justify.FlexEnd;
			node.Style.AlignItems = Align.Center;
			node.Style.FlexShrink = 0;
			node.Style.FlexGrow = 2;
			node.Style.FlexBasis = 50;
			node.Style.Width = 80;
			node.Style.Height = 40;
			node.CalculateLayout();
			node.Reset();
		}

		Assert.AreEqual( FlexDirection.Row, node.Style.FlexDirection );
		Assert.AreEqual( Align.Stretch, node.Style.AlignContent );
		Assert.AreEqual( Justify.Stretch, node.Style.JustifyContent );
		Assert.AreEqual( Align.Stretch, node.Style.AlignItems );
		Assert.AreEqual( StyleLength.Auto, node.Style.FlexBasis );
		Assert.AreEqual( StyleLength.Auto, node.Style.Width );
		Assert.AreEqual( StyleLength.Auto, node.Style.Height );

		var root = new LayoutNode();
		root.Style.Width = 100;
		node.Style.Width = 80;
		var sibling = new LayoutNode();
		sibling.Style.Width = 80;
		root.AddChild( node );
		root.AddChild( sibling );
		root.CalculateLayout();
		Assert.AreEqual( 50f, node.LayoutWidth );
		Assert.AreEqual( 50f, sibling.LayoutLeft );
		Assert.AreEqual( 50f, sibling.LayoutWidth );

		root.Style.Width = 200;
		root.CalculateLayout();
		Assert.AreEqual( 80f, node.LayoutWidth );
		Assert.AreEqual( 80f, sibling.LayoutLeft );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FractionalSizesAreNotRounded( bool measured )
	{
		var node = new LayoutNode();
		if ( measured )
			node.MeasureFunc = static ( _, _, _, _, _ ) => new LayoutSize( 20.4f, 10.4f );
		else
		{
			node.Style.Width = 20.4f;
			node.Style.Height = 10.4f;
		}

		node.CalculateLayout();
		Assert.AreEqual( 20.4f, node.LayoutWidth, 0.001f );
		Assert.AreEqual( 10.4f, node.LayoutHeight, 0.001f );
		node.CalculateLayout();
		Assert.AreEqual( 20.4f, node.LayoutWidth, 0.001f );
		Assert.AreEqual( 10.4f, node.LayoutHeight, 0.001f );
	}

	[TestMethod]
	[DataRow( -1f, 10.4f, 0f, 10.4f )]
	[DataRow( float.NaN, 10.4f, 0f, 10.4f )]
	[DataRow( 20.4f, -1f, 20.4f, 0f )]
	[DataRow( 20.4f, float.NaN, 20.4f, 0f )]
	[DataRow( -1f, float.NaN, 0f, 0f )]
	[DataRow( float.NaN, -1f, 0f, 0f )]
	public void InvalidMeasuredDimensionsAreSanitized( float width, float height, float expectedWidth, float expectedHeight )
	{
		var node = new LayoutNode { MeasureFunc = ( _, _, _, _, _ ) => new LayoutSize( width, height ) };
		var size = node.Measure( float.NaN, MeasureMode.Undefined, float.NaN, MeasureMode.Undefined );
		Assert.AreEqual( expectedWidth, size.Width );
		Assert.AreEqual( expectedHeight, size.Height );

		node.CalculateLayout();
		Assert.AreEqual( expectedWidth, node.LayoutWidth, 0.001f );
		Assert.AreEqual( expectedHeight, node.LayoutHeight, 0.001f );
	}

	[TestMethod]
	public void LayoutCacheIncludesPercentageReferenceSize()
	{
		var node = new LayoutNode();
		node.Style.Width = 100;
		node.Style.Height = 20;
		node.Style.SetPadding( Edge.Left, StyleLength.Percent( 10 ) );

		node.CalculateLayout( 200, 100 );
		Assert.AreEqual( 20, node.LayoutPadding( PhysicalEdge.Left ) );

		node.CalculateLayout( 400, 100 );
		Assert.AreEqual( 40, node.LayoutPadding( PhysicalEdge.Left ) );
	}

	[TestMethod]
	public void MeasurementCacheIncludesPercentageReferenceSize()
	{
		var node = new LayoutNode();
		node.Style.SetPadding( Edge.Left, StyleLength.Percent( 10 ) );
		var measureCalls = 0;
		node.MeasureFunc = ( _, _, _, _, _ ) =>
		{
			measureCalls++;
			return new LayoutSize( 10, 10 );
		};
		node.ProcessDimensions();
		var generation = LayoutNode.NextGeneration();

		LayoutAlgorithm.CalculateLayoutInternal( node, 100, 20, Direction.LTR, SizingMode.FitContent, SizingMode.FitContent, 200, 100, false, 0, generation );
		node.SetDirty( false );
		LayoutAlgorithm.CalculateLayoutInternal( node, 100, 20, Direction.LTR, SizingMode.FitContent, SizingMode.FitContent, 400, 100, false, 0, generation );

		Assert.AreEqual( 2, measureCalls );
		Assert.AreEqual( 40, node.LayoutPadding( PhysicalEdge.Left ) );
	}

	[TestMethod]
	public void LayoutCacheRestoresOverflowResult()
	{
		var root = new LayoutNode();
		root.Style.Width = 100;
		root.Style.Height = 20;
		var child = new LayoutNode();
		child.Style.Width = 200;
		child.Style.FlexShrink = 0;
		root.AddChild( child );

		root.CalculateLayout();
		Assert.IsTrue( root.LayoutHadOverflow );

		root.Layout.HadOverflow = false;
		root.CalculateLayout();
		Assert.IsTrue( root.LayoutHadOverflow );
	}

	[TestMethod]
	public void ReentrantLayoutDoesNotLeakMeasurementTaint()
	{
		var inner = new LayoutNode();
		inner.Style.MaxWidth = 0;
		inner.AddChild( new LayoutNode() );

		var measureCalls = 0;
		var outer = new LayoutNode();
		outer.MeasureFunc = ( _, _, _, _, _ ) =>
		{
			measureCalls++;
			inner.CalculateLayout();
			return new LayoutSize( 10, 10 );
		};

		outer.CalculateLayout();
		Assert.IsTrue( outer.Layout.CachedLayout.ContentBased );
		outer.CalculateLayout();

		Assert.AreEqual( 1, measureCalls );
	}
}
