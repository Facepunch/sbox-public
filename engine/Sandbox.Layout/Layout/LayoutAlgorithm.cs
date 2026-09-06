namespace Sandbox.Layout;

/// <summary>
/// Shared root entry, box resolution, and display-type dispatch.
/// </summary>
internal static partial class LayoutAlgorithm
{
	internal static void ZeroOutLayoutRecursively( LayoutNode node )
	{
		node.Layout.Reset();
		node.InlineFragments = Array.Empty<InlineFragment>();
		node.Layout.SetDimension( Dimension.Width, 0 );
		node.Layout.SetDimension( Dimension.Height, 0 );
		node.HasNewLayout = true;

		foreach ( var child in node.ChildList )
		{
			ZeroOutLayoutRecursively( child );
		}
	}

	private static void CleanupContentsNodesRecursively( LayoutNode node )
	{
		// Only display: contents children need cleaning, and the node counts those as they come and go.
		if ( !node.HasContentsChildren )
		{
			return;
		}

		foreach ( var child in node.ChildList )
		{
			if ( child.Style.Display == Display.Contents )
			{
				child.Layout.Reset();
				child.InlineFragments = Array.Empty<InlineFragment>();
				child.Layout.SetDimension( Dimension.Width, 0 );
				child.Layout.SetDimension( Dimension.Height, 0 );
				child.HasNewLayout = true;
				child.SetDirty( false );

				CleanupContentsNodesRecursively( child );
			}
		}
	}

	/// <summary>
	/// Sets the node's own margin, border and padding results and handles the leaf / measure-function /
	/// fixed-size shortcuts before dispatching on display type. Returns which measured dimensions were
	/// actually computed (see <see cref="MeasureScope"/>).
	/// </summary>
	private static MeasureScope CalculateLayoutImpl(
		LayoutNode node,
		float availableWidth,
		float availableHeight,
		Direction ownerDirection,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight,
		bool performLayout,
		int depth,
		uint generationCount,
		MeasureScope scope )
	{
		if ( Num.IsUndefined( availableWidth ) && widthSizingMode != SizingMode.MaxContent && widthSizingMode != SizingMode.MinContent )
		{
			throw new InvalidOperationException( "availableWidth is indefinite so widthSizingMode must be SizingMode.MaxContent" );
		}

		if ( Num.IsUndefined( availableHeight ) && heightSizingMode != SizingMode.MaxContent && heightSizingMode != SizingMode.MinContent )
		{
			throw new InvalidOperationException( "availableHeight is indefinite so heightSizingMode must be SizingMode.MaxContent" );
		}

		// Set the resolved resolution in the node's layout.
		var direction = node.ResolveDirection( ownerDirection );
		node.Layout.Direction = direction;
		node.Layout.HadOverflow = false;
		if ( performLayout ) node.InlineFragments = Array.Empty<InlineFragment>();

		var flexRowDirection = Axis.ResolveDirection( FlexDirection.Row, direction );
		var flexColumnDirection = Axis.ResolveDirection( FlexDirection.Column, direction );

		var startEdge = direction == Direction.LTR ? PhysicalEdge.Left : PhysicalEdge.Right;
		var endEdge = direction == Direction.LTR ? PhysicalEdge.Right : PhysicalEdge.Left;

		var marginRowLeading = node.Style.ComputeInlineStartMargin( flexRowDirection, direction, ownerWidth );
		node.Layout.SetMargin( startEdge, marginRowLeading );
		var marginRowTrailing = node.Style.ComputeInlineEndMargin( flexRowDirection, direction, ownerWidth );
		node.Layout.SetMargin( endEdge, marginRowTrailing );
		var marginColumnLeading = node.Style.ComputeInlineStartMargin( flexColumnDirection, direction, ownerWidth );
		node.Layout.SetMargin( PhysicalEdge.Top, marginColumnLeading );
		var marginColumnTrailing = node.Style.ComputeInlineEndMargin( flexColumnDirection, direction, ownerWidth );
		node.Layout.SetMargin( PhysicalEdge.Bottom, marginColumnTrailing );

		var marginAxisRow = marginRowLeading + marginRowTrailing;
		var marginAxisColumn = marginColumnLeading + marginColumnTrailing;

		node.Layout.SetBorder( startEdge, node.Style.ComputeInlineStartBorder( flexRowDirection, direction ) );
		node.Layout.SetBorder( endEdge, node.Style.ComputeInlineEndBorder( flexRowDirection, direction ) );
		node.Layout.SetBorder( PhysicalEdge.Top, node.Style.ComputeInlineStartBorder( flexColumnDirection, direction ) );
		node.Layout.SetBorder( PhysicalEdge.Bottom, node.Style.ComputeInlineEndBorder( flexColumnDirection, direction ) );

		node.Layout.SetPadding( startEdge, node.Style.ComputeInlineStartPadding( flexRowDirection, direction, ownerWidth ) );
		node.Layout.SetPadding( endEdge, node.Style.ComputeInlineEndPadding( flexRowDirection, direction, ownerWidth ) );
		node.Layout.SetPadding( PhysicalEdge.Top, node.Style.ComputeInlineStartPadding( flexColumnDirection, direction, ownerWidth ) );
		node.Layout.SetPadding( PhysicalEdge.Bottom, node.Style.ComputeInlineEndPadding( flexColumnDirection, direction, ownerWidth ) );

		if ( node.HasMeasureFunc )
		{
			MeasureNodeWithMeasureFunc(
				node,
				direction,
				availableWidth - marginAxisRow,
				availableHeight - marginAxisColumn,
				widthSizingMode,
				heightSizingMode,
				ownerWidth,
				ownerHeight
			);

			// Clean and update all display: contents nodes with a direct path to the current node as they
			// will not be traversed
			CleanupContentsNodesRecursively( node );
			return MeasureScope.Both;
		}

		var childCount = node.LayoutChildCount;
		if ( childCount == 0 && !(node.Style.Display == Display.Block && node.InlineContent is not null) )
		{
			MeasureNodeWithoutChildren(
				node,
				direction,
				availableWidth - marginAxisRow,
				availableHeight - marginAxisColumn,
				widthSizingMode,
				heightSizingMode,
				ownerWidth,
				ownerHeight
			);
			CleanupContentsNodesRecursively( node );
			return MeasureScope.Both;
		}

		// If we're not being asked to perform a full layout we can skip the algorithm if we already know the size
		// Block parents also consume escaping margins and collapse-through state, even when the size is fixed.
		if ( !performLayout && node.Style.Display != Display.Block && node.InlineContent is null && MeasureNodeWithFixedSize(
			node,
			direction,
			availableWidth - marginAxisRow,
			availableHeight - marginAxisColumn,
			widthSizingMode,
			heightSizingMode,
			ownerWidth,
			ownerHeight
		) )
		{
			CleanupContentsNodesRecursively( node );
			return MeasureScope.Both;
		}

		// Clean and update all display: contents nodes with a direct path to the current node as they will
		// not be traversed
		CleanupContentsNodesRecursively( node );

		switch ( node.Style.Display )
		{
			case Display.Grid:
				GridLayout.Compute(
					node,
					availableWidth,
					availableHeight,
					direction,
					widthSizingMode,
					heightSizingMode,
					ownerWidth,
					ownerHeight,
					performLayout,
					depth,
					generationCount,
					marginAxisRow,
					marginAxisColumn
				);
				return MeasureScope.Both;
			case Display.Block:
				BlockLayout.Compute(
					node,
					availableWidth,
					availableHeight,
					direction,
					widthSizingMode,
					heightSizingMode,
					ownerWidth,
					ownerHeight,
					performLayout,
					depth,
					generationCount,
					marginAxisRow,
					marginAxisColumn
				);
				return MeasureScope.Both;
		}

		return FlexLayout.Compute(
			node,
			availableWidth,
			availableHeight,
			ownerDirection,
			direction,
			widthSizingMode,
			heightSizingMode,
			ownerWidth,
			ownerHeight,
			performLayout,
			depth,
			generationCount,
			marginAxisRow,
			marginAxisColumn,
			childCount,
			scope
		);
	}

	/// <summary>
	/// Lay out a root node inside the given owner size.
	/// </summary>
	internal static void CalculateLayout( LayoutNode node, float ownerWidth, float ownerHeight, Direction ownerDirection )
	{
		var previousTaint = s_nonContentMeasurements;
		s_nonContentMeasurements = 0;
		try
		{
			CalculateLayoutRoot( node, ownerWidth, ownerHeight, ownerDirection );
		}
		finally
		{
			s_nonContentMeasurements = previousTaint;
		}
	}

	private static void CalculateLayoutRoot( LayoutNode node, float ownerWidth, float ownerHeight, Direction ownerDirection )
	{
		// Increment the generation count. This will force the recursive routine to visit all dirty nodes at
		// least once. Subsequent visits will be skipped if the input parameters don't change.
		var generationCount = LayoutNode.NextGeneration();
		node.ProcessDimensions();
		var direction = node.ResolveDirection( ownerDirection );
		var style = node.Style;

		float width;
		SizingMode widthSizingMode;
		if ( node.HasDefiniteLength( Dimension.Width, ownerWidth ) )
		{
			width = node.GetResolvedDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) + node.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth );
			widthSizingMode = SizingMode.StretchFit;
		}
		else if ( Num.IsDefined( style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) ) )
		{
			width = style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth );
			widthSizingMode = SizingMode.FitContent;
		}
		else
		{
			width = ownerWidth;
			widthSizingMode = Num.IsUndefined( width ) ? SizingMode.MaxContent : SizingMode.StretchFit;
		}

		float height;
		SizingMode heightSizingMode;
		if ( node.HasDefiniteLength( Dimension.Height, ownerHeight ) )
		{
			height = node.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) + node.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth );
			heightSizingMode = SizingMode.StretchFit;
		}
		else if ( Num.IsDefined( style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) ) )
		{
			height = style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth );
			heightSizingMode = SizingMode.FitContent;
		}
		else
		{
			height = ownerHeight;
			heightSizingMode = Num.IsUndefined( height ) ? SizingMode.MaxContent : SizingMode.StretchFit;
		}

		if ( CalculateLayoutInternal( node, width, height, ownerDirection, widthSizingMode, heightSizingMode, ownerWidth, ownerHeight, true, 0, generationCount ) )
		{
			node.SetPosition( node.Layout.Direction, ownerWidth, ownerHeight );
			LayoutNode viewport = null;
			LayoutFixedDescendants( node, ref viewport, node.LayoutWidth, node.LayoutHeight, direction, 1, generationCount );
		}
	}
}
