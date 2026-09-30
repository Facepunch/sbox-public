namespace Sandbox.Layout;

internal static partial class LayoutAlgorithm
{
	private static void MeasureNodeWithMeasureFunc(
		LayoutNode node,
		Direction direction,
		float availableWidth,
		float availableHeight,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight )
	{
		if ( widthSizingMode == SizingMode.MaxContent || widthSizingMode == SizingMode.MinContent )
		{
			availableWidth = Num.Undefined;
		}

		if ( heightSizingMode == SizingMode.MaxContent || heightSizingMode == SizingMode.MinContent )
		{
			availableHeight = Num.Undefined;
		}

		ref var layout = ref node.Layout;
		var paddingAndBorderAxisRow = layout.Padding( PhysicalEdge.Left )
			+ layout.Padding( PhysicalEdge.Right )
			+ layout.Border( PhysicalEdge.Left )
			+ layout.Border( PhysicalEdge.Right );
		var paddingAndBorderAxisColumn = layout.Padding( PhysicalEdge.Top )
			+ layout.Padding( PhysicalEdge.Bottom )
			+ layout.Border( PhysicalEdge.Top )
			+ layout.Border( PhysicalEdge.Bottom );

		// We want to make sure we don't call measure with negative size
		var innerWidth = Num.IsUndefined( availableWidth ) ? availableWidth : Num.MaxOrDefined( 0.0f, availableWidth - paddingAndBorderAxisRow );
		var innerHeight = Num.IsUndefined( availableHeight ) ? availableHeight : Num.MaxOrDefined( 0.0f, availableHeight - paddingAndBorderAxisColumn );

		if ( widthSizingMode == SizingMode.StretchFit && heightSizingMode == SizingMode.StretchFit )
		{
			// Don't bother sizing the text if both dimensions are already defined.
			node.Layout.SetMeasuredDimension( Dimension.Width, BoundAxis( node, FlexDirection.Row, direction, availableWidth, ownerWidth, ownerWidth ) );
			node.Layout.SetMeasuredDimension( Dimension.Height, BoundAxis( node, FlexDirection.Column, direction, availableHeight, ownerHeight, ownerWidth ) );
		}
		else
		{
			// Measure the text under the current constraints.
			var measuredSize = node.Measure( innerWidth, Axis.ToMeasureMode( widthSizingMode ), innerHeight, Axis.ToMeasureMode( heightSizingMode ) );

			node.Layout.SetMeasuredDimension( Dimension.Width, BoundAxis( node, FlexDirection.Row, direction,
				widthSizingMode != SizingMode.StretchFit ? measuredSize.Width + paddingAndBorderAxisRow : availableWidth, ownerWidth, ownerWidth ) );

			node.Layout.SetMeasuredDimension( Dimension.Height, BoundAxis( node, FlexDirection.Column, direction,
				heightSizingMode != SizingMode.StretchFit ? measuredSize.Height + paddingAndBorderAxisColumn : availableHeight, ownerHeight, ownerWidth ) );
		}
	}

	/// <summary>
	/// For nodes with no children, use the available values if they were provided, or the minimum size as
	/// indicated by the padding and border sizes.
	/// </summary>
	private static void MeasureNodeWithoutChildren(
		LayoutNode node,
		Direction direction,
		float availableWidth,
		float availableHeight,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight )
	{
		ref var layout = ref node.Layout;

		var width = availableWidth;
		if ( widthSizingMode != SizingMode.StretchFit )
		{
			width = layout.Padding( PhysicalEdge.Left ) + layout.Padding( PhysicalEdge.Right ) + layout.Border( PhysicalEdge.Left ) + layout.Border( PhysicalEdge.Right );
		}
		node.Layout.SetMeasuredDimension( Dimension.Width, BoundAxis( node, FlexDirection.Row, direction, width, ownerWidth, ownerWidth ) );

		var height = availableHeight;
		if ( heightSizingMode != SizingMode.StretchFit )
		{
			height = layout.Padding( PhysicalEdge.Top ) + layout.Padding( PhysicalEdge.Bottom ) + layout.Border( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Bottom );
		}
		node.Layout.SetMeasuredDimension( Dimension.Height, BoundAxis( node, FlexDirection.Column, direction, height, ownerHeight, ownerWidth ) );
	}

	private static bool MeasureNodeWithFixedSize(
		LayoutNode node,
		Direction direction,
		float availableWidth,
		float availableHeight,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight )
	{
		var noRoom = (Num.IsDefined( availableWidth ) && widthSizingMode == SizingMode.FitContent && availableWidth <= 0.0f)
			|| (Num.IsDefined( availableHeight ) && heightSizingMode == SizingMode.FitContent && availableHeight <= 0.0f);

		if ( noRoom || (widthSizingMode == SizingMode.StretchFit && heightSizingMode == SizingMode.StretchFit) )
		{
			if ( noRoom )
			{
				s_nonContentMeasurements++;
			}

			node.Layout.SetMeasuredDimension( Dimension.Width, BoundAxis( node, FlexDirection.Row, direction,
				Num.IsUndefined( availableWidth ) || (widthSizingMode == SizingMode.FitContent && availableWidth < 0.0f) ? 0.0f : availableWidth, ownerWidth, ownerWidth ) );

			node.Layout.SetMeasuredDimension( Dimension.Height, BoundAxis( node, FlexDirection.Column, direction,
				Num.IsUndefined( availableHeight ) || (heightSizingMode == SizingMode.FitContent && availableHeight < 0.0f) ? 0.0f : availableHeight, ownerHeight, ownerWidth ) );

			return true;
		}

		return false;
	}

	/// <summary>
	/// Whether a measure-only request for a single dimension (<paramref name="scope"/>, never
	/// <see cref="MeasureScope.Both"/>) of a container can be answered as the bounded available size without
	/// visiting the subtree (<see cref="MeasureFromAvailableSize"/>). Not when both dimensions are
	/// stretch-fit (the normal path is just as cheap through <see cref="MeasureNodeWithFixedSize"/> and keeps
	/// the cache behaviour for that request), not when only <c>display: contents</c> children are present
	/// (the node measures as a leaf), not for block and grid containers (they keep their full measurement),
	/// and not for a container with a single flexible child that has an explicit flex-basis: a stretch-fit
	/// pass on it zeroes that child's computed basis in <see cref="FlexLayout.Compute"/>, and the algorithm
	/// keeps that value for later passes, so the pass has to run.
	/// </summary>
	private static bool IsAnsweredByAvailableSize(
		LayoutNode node,
		MeasureScope scope,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		uint generationCount )
	{
		if ( (scope == MeasureScope.Width ? widthSizingMode : heightSizingMode) != SizingMode.StretchFit )
		{
			return false;
		}

		if ( widthSizingMode == SizingMode.StretchFit && heightSizingMode == SizingMode.StretchFit )
		{
			return false;
		}

		if ( node.LayoutChildCount == 0 )
		{
			return false;
		}

		if ( node.Style.Display == Display.Grid || node.Style.Display == Display.Block )
		{
			return false;
		}

		return !node.HasSingleStickyFlexChild( generationCount );
	}

	/// <summary>
	/// Answers a measurement that only needs one dimension, and that dimension is stretch-fit,
	/// without visiting the subtree - every path in <see cref="CalculateLayoutImpl"/> (measure function,
	/// leaf, fixed size and the flex algorithm's step 9) sets a stretch-fit dimension to exactly the bounded
	/// available size, and nothing else about the subtree is read. The caller (ComputeFlexBasisForChild, or
	/// a main-axis-only pass sizing its children) only consumes that dimension; the other is left undefined
	/// so a stray read shows up as NaN. The node's margin, border and padding results are not written; the
	/// next real pass on the node does that. The decision is <see cref="IsAnsweredByAvailableSize"/>'s.
	/// </summary>
	private static void MeasureFromAvailableSize(
		LayoutNode node,
		float availableWidth,
		float availableHeight,
		Direction ownerDirection,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight )
	{
		var direction = node.ResolveDirection( ownerDirection );
		node.Layout.Direction = direction;
		node.Layout.HadOverflow = false;

		var width = Num.Undefined;
		if ( widthSizingMode == SizingMode.StretchFit )
		{
			width = BoundAxis( node, FlexDirection.Row, direction, availableWidth - node.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth ), ownerWidth, ownerWidth );
		}

		var height = Num.Undefined;
		if ( heightSizingMode == SizingMode.StretchFit )
		{
			height = BoundAxis( node, FlexDirection.Column, direction, availableHeight - node.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth ), ownerHeight, ownerWidth );
		}

		node.Layout.SetMeasuredDimension( Dimension.Width, width );
		node.Layout.SetMeasuredDimension( Dimension.Height, height );
		CleanupContentsNodesRecursively( node );
	}
}
