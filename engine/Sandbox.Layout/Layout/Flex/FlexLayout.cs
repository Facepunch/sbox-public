using static Sandbox.Layout.LayoutAlgorithm;

namespace Sandbox.Layout;

/// <summary>
/// Flexbox calculation, flex-basis resolution, and free-space distribution.
/// </summary>
internal static partial class FlexLayout
{
	private static void ComputeFlexBasisForChild(
		LayoutNode node,
		LayoutNode child,
		float width,
		SizingMode widthMode,
		float height,
		float ownerWidth,
		float ownerHeight,
		SizingMode heightMode,
		Direction direction,
		int depth,
		uint generationCount,
		bool minContentWidth,
		bool minContentHeight )
	{
		var mainAxis = Axis.ResolveDirection( node.Style.FlexDirection, direction );
		var isMainAxisRow = Axis.IsRow( mainAxis );
		var mainAxisSize = isMainAxisRow ? width : height;
		var mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;

		var childWidth = Num.Undefined;
		var childHeight = Num.Undefined;
		SizingMode childWidthSizingMode;
		SizingMode childHeightSizingMode;

		var resolvedFlexBasis = child.ResolveFlexBasis( direction, mainAxis, mainAxisOwnerSize, ownerWidth );
		var previousFlexBasis = child.Layout.ComputedFlexBasis;
		var isRowStyleDimDefined = child.HasDefiniteLength( Dimension.Width, ownerWidth );
		var isColumnStyleDimDefined = child.HasDefiniteLength( Dimension.Height, ownerHeight );

		if ( Num.IsDefined( resolvedFlexBasis ) && Num.IsDefined( mainAxisSize ) )
		{
			if ( Num.IsUndefined( child.Layout.ComputedFlexBasis ) )
			{
				var paddingAndBorder = PaddingAndBorderForAxis( child, mainAxis, direction, ownerWidth );
				child.Layout.ComputedFlexBasis = Num.MaxOrDefined( resolvedFlexBasis, paddingAndBorder );
			}
		}
		else if ( isMainAxisRow && isRowStyleDimDefined )
		{
			// The width is definite, so use that as the flex basis.
			var paddingAndBorder = PaddingAndBorderForAxis( child, FlexDirection.Row, direction, ownerWidth );
			child.Layout.ComputedFlexBasis = Num.MaxOrDefined( child.GetResolvedDimension( direction, Dimension.Width, ownerWidth, ownerWidth ), paddingAndBorder );
		}
		else if ( !isMainAxisRow && isColumnStyleDimDefined )
		{
			// The height is definite, so use that as the flex basis.
			var paddingAndBorder = PaddingAndBorderForAxis( child, FlexDirection.Column, direction, ownerWidth );
			child.Layout.ComputedFlexBasis = Num.MaxOrDefined( child.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth ), paddingAndBorder );
		}
		else
		{
			// Compute the flex basis and hypothetical main size (i.e. the clamped flex basis). Under a
			// min-content constraint the children contribute their min-content sizes.
			childWidthSizingMode = minContentWidth ? SizingMode.MinContent : SizingMode.MaxContent;
			childHeightSizingMode = minContentHeight ? SizingMode.MinContent : SizingMode.MaxContent;

			var marginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, ownerWidth );
			var marginColumn = child.Style.ComputeMarginForAxis( FlexDirection.Column, ownerWidth );

			if ( isRowStyleDimDefined )
			{
				childWidth = child.GetResolvedDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) + marginRow;
				childWidthSizingMode = SizingMode.StretchFit;
			}
			if ( isColumnStyleDimDefined )
			{
				childHeight = child.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) + marginColumn;
				childHeightSizingMode = SizingMode.StretchFit;
			}

			// The W3C spec doesn't say anything about the 'overflow' property, but all major browsers appear
			// to implement the following logic.
			if ( (!isMainAxisRow && node.Style.Overflow == Overflow.Scroll) || node.Style.Overflow != Overflow.Scroll )
			{
				if ( Num.IsUndefined( childWidth ) && Num.IsDefined( width ) )
				{
					childWidth = width;
					childWidthSizingMode = SizingMode.FitContent;
				}
			}

			if ( (isMainAxisRow && node.Style.Overflow == Overflow.Scroll) || node.Style.Overflow != Overflow.Scroll )
			{
				if ( Num.IsUndefined( childHeight ) && Num.IsDefined( height ) )
				{
					childHeight = height;
					childHeightSizingMode = SizingMode.FitContent;
				}
			}

			var childStyle = child.Style;
			if ( Num.IsDefined( childStyle.AspectRatio ) )
			{
				if ( !isMainAxisRow && childWidthSizingMode == SizingMode.StretchFit )
				{
					childHeight = marginColumn + (childWidth - marginRow) / childStyle.AspectRatio;
					childHeightSizingMode = SizingMode.StretchFit;
				}
				else if ( isMainAxisRow && childHeightSizingMode == SizingMode.StretchFit )
				{
					childWidth = marginRow + (childHeight - marginColumn) * childStyle.AspectRatio;
					childWidthSizingMode = SizingMode.StretchFit;
				}
			}

			// If child has no defined size in the cross axis and is set to stretch, set the cross axis to be
			// measured exactly with the available inner width

			var hasExactWidth = Num.IsDefined( width ) && widthMode == SizingMode.StretchFit;
			var childWidthStretch = Axis.ResolveChildAlignment( node, child ) == Align.Stretch && childWidthSizingMode != SizingMode.StretchFit;
			if ( !isMainAxisRow && !isRowStyleDimDefined && hasExactWidth && childWidthStretch )
			{
				childWidth = width;
				childWidthSizingMode = SizingMode.StretchFit;
				if ( Num.IsDefined( childStyle.AspectRatio ) )
				{
					childHeight = (childWidth - marginRow) / childStyle.AspectRatio;
					childHeightSizingMode = SizingMode.StretchFit;
				}
			}

			var hasExactHeight = Num.IsDefined( height ) && heightMode == SizingMode.StretchFit;
			var childHeightStretch = Axis.ResolveChildAlignment( node, child ) == Align.Stretch && childHeightSizingMode != SizingMode.StretchFit;
			if ( isMainAxisRow && !isColumnStyleDimDefined && hasExactHeight && childHeightStretch )
			{
				childHeight = height;
				childHeightSizingMode = SizingMode.StretchFit;

				if ( Num.IsDefined( childStyle.AspectRatio ) )
				{
					childWidth = (childHeight - marginColumn) * childStyle.AspectRatio;
					childWidthSizingMode = SizingMode.StretchFit;
				}
			}

			ConstrainMaxSizeForMode( child, direction, FlexDirection.Row, ownerWidth, ownerWidth, ref childWidthSizingMode, ref childWidth );
			ConstrainMaxSizeForMode( child, direction, FlexDirection.Column, ownerHeight, ownerWidth, ref childHeightSizingMode, ref childHeight );

			// Measure the child. Only its main-axis size is used (the flex basis), so ask for just that: a
			// flex container child can then skip sizing its own children on the cross axis (see
			// Compute). The complete pass measures both dimensions here.
			CalculateLayoutInternal( child, childWidth, childHeight, direction, childWidthSizingMode, childHeightSizingMode, ownerWidth, ownerHeight, false, depth, generationCount,
				isMainAxisRow ? MeasureScope.Width : MeasureScope.Height );

			child.Layout.ComputedFlexBasis = Num.MaxOrDefined(
				child.Layout.MeasuredDimension( Axis.DimensionOf( mainAxis ) ),
				PaddingAndBorderForAxis( child, mainAxis, direction, ownerWidth )
			);
		}

		child.Layout.ComputedFlexBasisGeneration = generationCount;
		if ( Num.IsDefined( resolvedFlexBasis ) )
		{
			NoteFlexBasisChange( child, previousFlexBasis );
		}
	}

	private static float ComputeFlexBasisForChildren(
		LayoutNode node,
		List<LayoutNode> children,
		float availableInnerWidth,
		float availableInnerHeight,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		Direction direction,
		FlexDirection mainAxis,
		bool performLayout,
		int depth,
		uint generationCount,
		bool minContentWidth,
		bool minContentHeight )
	{
		var totalOuterFlexBasis = 0.0f;
		LayoutNode singleFlexChild = null;
		var sizingModeMainDim = Axis.IsRow( mainAxis ) ? widthSizingMode : heightSizingMode;
		var childrenBaselineSensitive = node.Layout.BaselineSensitive || node.IsBaselineContainer;

		// If there is only one child with flex grow and shrink, its ComputedFlexBasis can be set to zero
		// instead of measuring and flexing the child to exactly match the remaining space.
		if ( sizingModeMainDim == SizingMode.StretchFit )
		{
			foreach ( var child in children )
			{
				if ( child.IsNodeFlexible() )
				{
					if ( singleFlexChild is not null || Num.InexactEquals( child.ResolveFlexGrow(), 0.0f ) || Num.InexactEquals( child.ResolveFlexShrink(), 0.0f ) )
					{
						// Stop if there is already a flexible child or this child cannot both grow and shrink.
						singleFlexChild = null;
						break;
					}
					else
					{
						singleFlexChild = child;
					}
				}
			}
		}

		foreach ( var child in children )
		{
			child.ProcessDimensions();
			if ( child.Style.Display == Display.None )
			{
				ZeroOutLayoutRecursively( child );
				child.HasNewLayout = true;
				child.SetDirty( false );
				continue;
			}

			// See PropagateBaselineSensitivity; absolutely positioned children are skipped by CalculateBaseline.
			child.Layout.BaselineSensitive = childrenBaselineSensitive && !child.Style.IsOutOfFlow;

			if ( performLayout )
			{
				// Set the initial position (relative to the owner).
				var childDirection = child.ResolveDirection( direction );
				child.SetPosition( childDirection, availableInnerWidth, availableInnerHeight );
			}

			if ( child.Style.IsOutOfFlow )
			{
				continue;
			}

			if ( child == singleFlexChild )
			{
				var previousFlexBasis = child.Layout.ComputedFlexBasis;
				child.Layout.ComputedFlexBasisGeneration = generationCount;
				child.Layout.ComputedFlexBasis = 0;
				if ( Num.IsDefined( child.ResolveFlexBasis(
					direction,
					mainAxis,
					Axis.IsRow( mainAxis ) ? availableInnerWidth : availableInnerHeight,
					availableInnerWidth
				) ) )
				{
					NoteFlexBasisChange( child, previousFlexBasis );
				}
			}
			else
			{
				ComputeFlexBasisForChild(
					node,
					child,
					availableInnerWidth,
					widthSizingMode,
					availableInnerHeight,
					availableInnerWidth,
					availableInnerHeight,
					heightSizingMode,
					direction,
					depth,
					generationCount,
					minContentWidth,
					minContentHeight
				);
			}

			totalOuterFlexBasis += child.Layout.ComputedFlexBasis + child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );
		}

		return totalOuterFlexBasis;
	}

	/// <summary>
	/// It distributes the free space to the flexible items and ensures that the size of the flex items abide
	/// the min and max constraints. At the end of this function the child nodes would have proper size.
	/// Prior using this function please ensure that <see cref="DistributeFreeSpaceFirstPass"/> is called.
	/// </summary>
	private static float DistributeFreeSpaceSecondPass(
		FlexLine flexLine,
		LayoutNode node,
		FlexDirection mainAxis,
		FlexDirection crossAxis,
		Direction direction,
		float ownerWidth,
		float mainAxisOwnerSize,
		float availableInnerMainDim,
		float availableInnerCrossDim,
		float availableInnerWidth,
		float availableInnerHeight,
		bool mainAxisOverflows,
		SizingMode sizingModeCrossDim,
		bool performLayout,
		int depth,
		uint generationCount,
		bool minContentCross,
		MeasureScope childScope )
	{
		float childFlexBasis;
		float flexShrinkScaledFactor;
		float flexGrowFactor;
		float deltaFreeSpace = 0;
		var isMainAxisRow = Axis.IsRow( mainAxis );
		var isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;

		foreach ( var currentLineChild in flexLine.ItemsInFlow )
		{
			childFlexBasis = BoundAxisWithinMinAndMax( currentLineChild, direction, mainAxis, currentLineChild.Layout.ComputedFlexBasis, mainAxisOwnerSize, ownerWidth );
			var updatedMainSize = childFlexBasis;

			if ( Num.IsDefined( flexLine.Layout.RemainingFreeSpace ) && flexLine.Layout.RemainingFreeSpace < 0 )
			{
				flexShrinkScaledFactor = -currentLineChild.ResolveFlexShrink() * childFlexBasis;

				// Is this child able to shrink?
				if ( flexShrinkScaledFactor != 0 )
				{
					float childSize;

					if ( Num.IsDefined( flexLine.Layout.TotalFlexShrinkScaledFactors ) && flexLine.Layout.TotalFlexShrinkScaledFactors == 0 )
					{
						childSize = childFlexBasis + flexShrinkScaledFactor;
					}
					else
					{
						childSize = childFlexBasis + (flexLine.Layout.RemainingFreeSpace / flexLine.Layout.TotalFlexShrinkScaledFactors) * flexShrinkScaledFactor;
					}

					updatedMainSize = BoundAxis( currentLineChild, mainAxis, direction, childSize, availableInnerMainDim, availableInnerWidth );
				}
			}
			else if ( Num.IsDefined( flexLine.Layout.RemainingFreeSpace ) && flexLine.Layout.RemainingFreeSpace > 0 )
			{
				flexGrowFactor = currentLineChild.ResolveFlexGrow();

				// Is this child able to grow?
				if ( !float.IsNaN( flexGrowFactor ) && flexGrowFactor != 0 )
				{
					updatedMainSize = BoundAxis( currentLineChild, mainAxis, direction,
						childFlexBasis + flexLine.Layout.RemainingFreeSpace / flexLine.Layout.TotalFlexGrowFactors * flexGrowFactor, availableInnerMainDim, availableInnerWidth );
				}
			}

			deltaFreeSpace += updatedMainSize - childFlexBasis;

			var marginMain = currentLineChild.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );
			var marginCross = currentLineChild.Style.ComputeMarginForAxis( crossAxis, availableInnerWidth );

			var childCrossSize = Num.Undefined;
			var childMainSize = updatedMainSize + marginMain;
			SizingMode childCrossSizingMode;
			var childMainSizingMode = SizingMode.StretchFit;

			var childStyle = currentLineChild.Style;
			if ( Num.IsDefined( childStyle.AspectRatio ) )
			{
				childCrossSize = isMainAxisRow ? (childMainSize - marginMain) / childStyle.AspectRatio : (childMainSize - marginMain) * childStyle.AspectRatio;
				childCrossSizingMode = SizingMode.StretchFit;

				childCrossSize += marginCross;
			}
			else if ( !float.IsNaN( availableInnerCrossDim )
				&& !currentLineChild.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim )
				&& sizingModeCrossDim == SizingMode.StretchFit
				&& !(isNodeFlexWrap && mainAxisOverflows)
				&& Axis.ResolveChildAlignment( node, currentLineChild ) == Align.Stretch
				&& !currentLineChild.Style.FlexStartMarginIsAuto( crossAxis, direction )
				&& !currentLineChild.Style.FlexEndMarginIsAuto( crossAxis, direction ) )
			{
				childCrossSize = availableInnerCrossDim;
				childCrossSizingMode = SizingMode.StretchFit;
			}
			else if ( !currentLineChild.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim ) )
			{
				childCrossSize = availableInnerCrossDim;
				childCrossSizingMode = Num.IsUndefined( childCrossSize ) ? (minContentCross ? SizingMode.MinContent : SizingMode.MaxContent) : SizingMode.FitContent;
			}
			else
			{
				childCrossSize = currentLineChild.GetResolvedDimension( direction, Axis.DimensionOf( crossAxis ), availableInnerCrossDim, availableInnerWidth ) + marginCross;
				var isLoosePercentageMeasurement = currentLineChild.GetProcessedDimension( Axis.DimensionOf( crossAxis ) ).Unit == Unit.Percent && sizingModeCrossDim != SizingMode.StretchFit;
				childCrossSizingMode = Num.IsUndefined( childCrossSize ) || isLoosePercentageMeasurement ? SizingMode.MaxContent : SizingMode.StretchFit;
			}

			ConstrainMaxSizeForMode( currentLineChild, direction, mainAxis, availableInnerMainDim, availableInnerWidth, ref childMainSizingMode, ref childMainSize );
			ConstrainMaxSizeForMode( currentLineChild, direction, crossAxis, availableInnerCrossDim, availableInnerWidth, ref childCrossSizingMode, ref childCrossSize );

			var requiresStretchLayout = !currentLineChild.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim )
				&& Axis.ResolveChildAlignment( node, currentLineChild ) == Align.Stretch
				&& !currentLineChild.Style.FlexStartMarginIsAuto( crossAxis, direction )
				&& !currentLineChild.Style.FlexEndMarginIsAuto( crossAxis, direction );

			var childWidth = isMainAxisRow ? childMainSize : childCrossSize;
			var childHeight = !isMainAxisRow ? childMainSize : childCrossSize;

			var childWidthSizingMode = isMainAxisRow ? childMainSizingMode : childCrossSizingMode;
			var childHeightSizingMode = !isMainAxisRow ? childMainSizingMode : childCrossSizingMode;

			var isLayoutPass = performLayout && !requiresStretchLayout;

			// Recursively call the layout algorithm for this child with the updated main size.
			CalculateLayoutInternal( currentLineChild, childWidth, childHeight, node.Layout.Direction, childWidthSizingMode, childHeightSizingMode, availableInnerWidth, availableInnerHeight,
				isLayoutPass, depth, generationCount, childScope );

			node.Layout.HadOverflow = node.Layout.HadOverflow || currentLineChild.Layout.HadOverflow;
		}

		return deltaFreeSpace;
	}

	/// <summary>
	/// It distributes the free space to the flexible items. For those flexible items whose min and max
	/// constraints are triggered, those flex item's clamped size is removed from the remaining free space.
	/// </summary>
	private static void DistributeFreeSpaceFirstPass(
		FlexLine flexLine,
		Direction direction,
		FlexDirection mainAxis,
		float ownerWidth,
		float mainAxisOwnerSize,
		float availableInnerMainDim,
		float availableInnerWidth )
	{
		float flexShrinkScaledFactor;
		float flexGrowFactor;
		float baseMainSize;
		float boundMainSize;
		float deltaFreeSpace = 0;

		foreach ( var currentLineChild in flexLine.ItemsInFlow )
		{
			var childFlexBasis = BoundAxisWithinMinAndMax( currentLineChild, direction, mainAxis, currentLineChild.Layout.ComputedFlexBasis, mainAxisOwnerSize, ownerWidth );

			if ( flexLine.Layout.RemainingFreeSpace < 0 )
			{
				flexShrinkScaledFactor = -currentLineChild.ResolveFlexShrink() * childFlexBasis;

				// Is this child able to shrink?
				if ( Num.IsDefined( flexShrinkScaledFactor ) && flexShrinkScaledFactor != 0 )
				{
					baseMainSize = childFlexBasis + flexLine.Layout.RemainingFreeSpace / flexLine.Layout.TotalFlexShrinkScaledFactors * flexShrinkScaledFactor;
					boundMainSize = BoundAxis( currentLineChild, mainAxis, direction, baseMainSize, availableInnerMainDim, availableInnerWidth );
					if ( Num.IsDefined( baseMainSize ) && Num.IsDefined( boundMainSize ) && baseMainSize != boundMainSize )
					{
						// By excluding this item's size and flex factor from remaining, this item's min/max
						// constraints should also trigger in the second pass resulting in the item's size
						// calculation being identical in the first and second passes.
						deltaFreeSpace += boundMainSize - childFlexBasis;
						flexLine.Layout.TotalFlexShrinkScaledFactors -= -currentLineChild.ResolveFlexShrink() * currentLineChild.Layout.ComputedFlexBasis;
					}
				}
			}
			else if ( Num.IsDefined( flexLine.Layout.RemainingFreeSpace ) && flexLine.Layout.RemainingFreeSpace > 0 )
			{
				flexGrowFactor = currentLineChild.ResolveFlexGrow();

				// Is this child able to grow?
				if ( Num.IsDefined( flexGrowFactor ) && flexGrowFactor != 0 )
				{
					baseMainSize = childFlexBasis + flexLine.Layout.RemainingFreeSpace / flexLine.Layout.TotalFlexGrowFactors * flexGrowFactor;
					boundMainSize = BoundAxis( currentLineChild, mainAxis, direction, baseMainSize, availableInnerMainDim, availableInnerWidth );

					if ( Num.IsDefined( baseMainSize ) && Num.IsDefined( boundMainSize ) && baseMainSize != boundMainSize )
					{
						// By excluding this item's size and flex factor from remaining, this item's min/max
						// constraints should also trigger in the second pass resulting in the item's size
						// calculation being identical in the first and second passes.
						deltaFreeSpace += boundMainSize - childFlexBasis;
						flexLine.Layout.TotalFlexGrowFactors -= flexGrowFactor;
					}
				}
			}
		}

		flexLine.Layout.RemainingFreeSpace -= deltaFreeSpace;
	}

	/// <summary>
	/// Do two passes over the flex items to figure out how to distribute the remaining space.
	///
	/// The first pass finds the items whose min/max constraints trigger, freezes them at those sizes, and
	/// excludes those sizes from the remaining space.
	///
	/// The second pass sets the size of each flexible item. It distributes the remaining space amongst the
	/// items whose min/max constraints didn't trigger in the first pass. For the other items, it sets their
	/// sizes by forcing their min/max constraints to trigger again.
	///
	/// This two pass approach for resolving min/max constraints deviates from the spec. The spec
	/// (https://www.w3.org/TR/CSS-flexbox-1/#resolve-flexible-lengths) describes a process that needs to be
	/// repeated a variable number of times. The algorithm implemented here won't handle all cases but it
	/// was simpler to implement and it mitigates performance concerns because we know exactly how many
	/// passes it'll do.
	///
	/// At the end of this function the child nodes would have the proper size assigned to them.
	/// </summary>
	private static void ResolveFlexibleLength(
		LayoutNode node,
		FlexLine flexLine,
		FlexDirection mainAxis,
		FlexDirection crossAxis,
		Direction direction,
		float ownerWidth,
		float mainAxisOwnerSize,
		float availableInnerMainDim,
		float availableInnerCrossDim,
		float availableInnerWidth,
		float availableInnerHeight,
		bool mainAxisOverflows,
		SizingMode sizingModeCrossDim,
		bool performLayout,
		int depth,
		uint generationCount,
		bool minContentCross,
		MeasureScope childScope )
	{
		var originalFreeSpace = flexLine.Layout.RemainingFreeSpace;

		// First pass: detect the flex items whose min/max constraints trigger
		DistributeFreeSpaceFirstPass( flexLine, direction, mainAxis, ownerWidth, mainAxisOwnerSize, availableInnerMainDim, availableInnerWidth );

		// Second pass: resolve the sizes of the flexible items
		var distributedFreeSpace = DistributeFreeSpaceSecondPass( flexLine, node, mainAxis, crossAxis, direction, ownerWidth, mainAxisOwnerSize, availableInnerMainDim, availableInnerCrossDim,
			availableInnerWidth, availableInnerHeight, mainAxisOverflows, sizingModeCrossDim, performLayout, depth, generationCount, minContentCross, childScope );

		flexLine.Layout.RemainingFreeSpace = originalFreeSpace - distributedFreeSpace;
	}

	private static void JustifyMainAxis(
		LayoutNode node,
		FlexLine flexLine,
		FlexDirection mainAxis,
		FlexDirection crossAxis,
		Direction direction,
		SizingMode sizingModeMainDim,
		SizingMode sizingModeCrossDim,
		float mainAxisOwnerSize,
		float ownerWidth,
		float availableInnerMainDim,
		float availableInnerCrossDim,
		float availableInnerWidth,
		bool performLayout,
		bool mainAxisOnly )
	{
		var style = node.Style;

		var leadingPaddingAndBorderMain = node.Style.ComputeFlexStartPaddingAndBorder( mainAxis, direction, ownerWidth );
		var trailingPaddingAndBorderMain = node.Style.ComputeFlexEndPaddingAndBorder( mainAxis, direction, ownerWidth );

		var gap = node.Style.ComputeGapForAxis( mainAxis, availableInnerMainDim );

		// For fit-content sizing on the main axis, keep RemainingFreeSpace at zero when no minimum main
		// main dimension is not given
		if ( sizingModeMainDim == SizingMode.FitContent && flexLine.Layout.RemainingFreeSpace > 0 )
		{
			if ( style.GetMinDimension( Axis.DimensionOf( mainAxis ) ).IsDefined
				&& Num.IsDefined( style.ResolvedMinDimension( direction, Axis.DimensionOf( mainAxis ), mainAxisOwnerSize, ownerWidth ) ) )
			{
				// This condition makes sure that if the size of main dimension(after considering child nodes
				// main dim, leading and trailing padding etc) falls below min dimension, then the
				// Recalculate RemainingFreeSpace against the minimum main-axis size.

				// `minAvailableMainDim` denotes minimum available space in which child can be laid out, it
				// will exclude space consumed by padding and border.
				var minAvailableMainDim = style.ResolvedMinDimension( direction, Axis.DimensionOf( mainAxis ), mainAxisOwnerSize, ownerWidth ) - leadingPaddingAndBorderMain - trailingPaddingAndBorderMain;
				var occupiedSpaceByChildNodes = availableInnerMainDim - flexLine.Layout.RemainingFreeSpace;
				flexLine.Layout.RemainingFreeSpace = Num.MaxOrDefined( 0.0f, minAvailableMainDim - occupiedSpaceByChildNodes );
			}
			else
			{
				flexLine.Layout.RemainingFreeSpace = 0;
			}
		}

		// In order to position the elements in the main axis, we have two controls. The space between the
		// beginning and the first element and the space between each two elements.
		float leadingMainDim = 0;
		float betweenMainDim = gap;
		var justifyContent = flexLine.Layout.RemainingFreeSpace >= 0 ? node.Style.JustifyContent : Axis.FallbackAlignment( node.Style.JustifyContent );

		if ( flexLine.NumberOfAutoMargins == 0 )
		{
			switch ( justifyContent )
			{
				case Justify.Center:
					leadingMainDim = flexLine.Layout.RemainingFreeSpace / 2;
					break;
				case Justify.FlexEnd:
					leadingMainDim = flexLine.Layout.RemainingFreeSpace;
					break;
				case Justify.SpaceBetween:
					if ( flexLine.ItemsInFlow.Count > 1 )
					{
						betweenMainDim += flexLine.Layout.RemainingFreeSpace / (flexLine.ItemsInFlow.Count - 1);
					}
					break;
				case Justify.SpaceEvenly:
					// Space is distributed evenly across all elements
					leadingMainDim = flexLine.Layout.RemainingFreeSpace / (flexLine.ItemsInFlow.Count + 1);
					betweenMainDim += leadingMainDim;
					break;
				case Justify.SpaceAround:
					// Space on the edges is half of the space between elements
					leadingMainDim = 0.5f * flexLine.Layout.RemainingFreeSpace / flexLine.ItemsInFlow.Count;
					betweenMainDim += leadingMainDim * 2;
					break;
				case Justify.FlexStart:
				case Justify.Stretch:
					break;
			}
		}

		flexLine.Layout.MainDimension = leadingPaddingAndBorderMain + leadingMainDim;
		flexLine.Layout.CrossDimension = 0;

		float maxAscentForCurrentLine = 0;
		float maxDescentForCurrentLine = 0;
		// A main-axis-only pass never runs on a baseline-aligned container (CalculateLayoutInternal forces
		// those to measure both axes), and the cross size it would compute here is discarded anyway.
		var isNodeBaselineLayout = !mainAxisOnly && IsBaselineLayout( node );
		var lastChild = flexLine.ItemsInFlow.Count > 0 ? flexLine.ItemsInFlow[^1] : null;

		foreach ( var child in flexLine.ItemsInFlow )
		{
			ref var childLayout = ref child.Layout;
			if ( child.Style.FlexStartMarginIsAuto( mainAxis, direction ) && flexLine.Layout.RemainingFreeSpace > 0.0f )
			{
				flexLine.Layout.MainDimension += flexLine.Layout.RemainingFreeSpace / flexLine.NumberOfAutoMargins;
			}

			if ( performLayout )
			{
				child.Layout.SetPosition( Axis.FlexStartEdge( mainAxis ), childLayout.Position( Axis.FlexStartEdge( mainAxis ) ) + flexLine.Layout.MainDimension );
			}

			if ( child != lastChild )
			{
				flexLine.Layout.MainDimension += betweenMainDim;
			}

			if ( child.Style.FlexEndMarginIsAuto( mainAxis, direction ) && flexLine.Layout.RemainingFreeSpace > 0.0f )
			{
				flexLine.Layout.MainDimension += flexLine.Layout.RemainingFreeSpace / flexLine.NumberOfAutoMargins;
			}

			var canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;
			if ( canSkipFlex )
			{
				// If the flex step was skipped, the measured dimensions were not computed, so
				// DimensionWithMargin cannot be used.
				flexLine.Layout.MainDimension += child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth ) + childLayout.ComputedFlexBasis;
				flexLine.Layout.CrossDimension = availableInnerCrossDim;
			}
			else
			{
				// The main dimension is the sum of all the elements dimension plus the spacing.
				flexLine.Layout.MainDimension += child.DimensionWithMargin( mainAxis, availableInnerWidth );

				if ( isNodeBaselineLayout )
				{
					// If the child is baseline aligned then the cross dimension is calculated by adding
					// maxAscent and maxDescent from the baseline.
					var ascent = CalculateBaseline( child ) + child.Style.ComputeFlexStartMargin( FlexDirection.Column, direction, availableInnerWidth );
					var descent = child.Layout.MeasuredDimension( Dimension.Height ) + child.Style.ComputeMarginForAxis( FlexDirection.Column, availableInnerWidth ) - ascent;

					maxAscentForCurrentLine = Num.MaxOrDefined( maxAscentForCurrentLine, ascent );
					maxDescentForCurrentLine = Num.MaxOrDefined( maxDescentForCurrentLine, descent );
				}
				else
				{
					// The cross dimension is the max of the elements dimension since there can only be one
					// element in that cross dimension in the case when the items are not baseline aligned
					flexLine.Layout.CrossDimension = Num.MaxOrDefined( flexLine.Layout.CrossDimension, child.DimensionWithMargin( crossAxis, availableInnerWidth ) );
				}
			}
		}

		flexLine.Layout.MainDimension += trailingPaddingAndBorderMain;

		if ( isNodeBaselineLayout )
		{
			flexLine.Layout.CrossDimension = maxAscentForCurrentLine + maxDescentForCurrentLine;
		}
	}

	[ThreadStatic] private static Stack<FlexLine> s_flexLinePool;
	[ThreadStatic] private static int s_pooledFlexLineItems;
	private const int MaxPooledFlexLines = 128;
	private const int MaxPooledFlexLineItems = 4096;
	private const int MaxTotalPooledFlexLineItems = 32768;

	private static FlexLine RentFlexLine()
	{
		s_flexLinePool ??= new Stack<FlexLine>();
		if ( s_flexLinePool.Count == 0 ) return new FlexLine();
		var line = s_flexLinePool.Pop();
		s_pooledFlexLineItems -= line.ItemsInFlow.Capacity;
		return line;
	}

	private static void ReturnFlexLine( FlexLine line )
	{
		var retain = line.ItemsInFlow.Capacity <= MaxPooledFlexLineItems;
		line.Reset();
		if ( retain && s_flexLinePool.Count < MaxPooledFlexLines && s_pooledFlexLineItems + line.ItemsInFlow.Capacity <= MaxTotalPooledFlexLineItems )
		{
			s_flexLinePool.Push( line );
			s_pooledFlexLineItems += line.ItemsInFlow.Capacity;
		}
	}

	/// <summary>
	/// This is the main routine that implements a subset of the flexbox layout algorithm described in the
	/// W3C CSS documentation: https://www.w3.org/TR/CSS3-flexbox/.
	///
	/// Limitations of this algorithm, compared to the full standard:
	///  * The <c>ZIndex</c> property (or any form of z-ordering) is not supported. Nodes are stacked in document order.
	///  * The 'order' property is not supported. The order of flex items is always defined by document order.
	///  * The 'visibility' property is always assumed to be 'visible'. Values of 'collapse' and 'hidden' are not supported.
	///  * There is no support for forced breaks.
	///  * It does not support vertical inline directions (top-to-bottom or bottom-to-top text).
	///
	/// Deviations from standard:
	///  * Section 4.5 of the spec indicates that all flex items have a default minimum main size. For text
	///    blocks, for example, this is the width of the widest word. Calculating the minimum width is
	///    expensive, so we forego it and assume a default minimum main size of 0.
	///  * Min/Max sizes in the main axis are not honored when resolving flexible lengths.
	///  * The spec indicates that the default value for <c>FlexDirection</c> is <c>row</c>, but the algorithm below
	///    assumes a default of 'column'.
	///
	/// Input parameters:
	///    - node: current node to be sized and laid out
	///    - availableWidth &amp; availableHeight: available size to be used for sizing the node or undefined
	///      if the size is not available; interpretation depends on layout flags
	///    - ownerDirection: the inline (text) direction within the owner (left-to-right or right-to-left)
	///    - widthSizingMode: indicates the sizing rules for the width (see below for explanation)
	///    - heightSizingMode: indicates the sizing rules for the height (see below for explanation)
	///    - performLayout: specifies whether the caller is interested in just the dimensions of the node or
	///      it requires the entire node and its subtree to be laid out (with final positions)
	///
	/// Details:
	///    This routine is called recursively to lay out subtrees of flexbox elements. It treats
	///    <see cref="LayoutNode.Style"/> as read-only input. It sets <see cref="LayoutResults.Direction"/>
	///    and the measured dimensions for the input node, plus positions and line indices for its children.
	///    Measured dimensions include the node's border and padding but not its margins.
	///
	///    When calling <see cref="LayoutAlgorithm.CalculateLayoutInternal"/>, an undefined
	///    available size requires <see cref="SizingMode.MaxContent"/> for that dimension.
	/// </summary>
	internal static MeasureScope Compute(
		LayoutNode node,
		float availableWidth,
		float availableHeight,
		Direction ownerDirection,
		Direction direction,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight,
		bool performLayout,
		int depth,
		uint generationCount,
		float marginAxisRow,
		float marginAxisColumn,
		int childCount,
		MeasureScope scope )
	{
		// A min-content request uses max-content sizing with children contributing their min-content
		// sizes: exact for single-line containers, approximate for wrapping containers.
		var minContentWidth = widthSizingMode == SizingMode.MinContent;
		var minContentHeight = heightSizingMode == SizingMode.MinContent;
		if ( minContentWidth )
		{
			widthSizingMode = SizingMode.MaxContent;
		}

		if ( minContentHeight )
		{
			heightSizingMode = SizingMode.MaxContent;
		}

		var childBuffer = RentList();
		var children = node.GetLayoutChildren( childBuffer );

		// STEP 1: CALCULATE VALUES FOR REMAINDER OF ALGORITHM
		var mainAxis = Axis.ResolveDirection( node.Style.FlexDirection, direction );
		var crossAxis = Axis.ResolveCrossDirection( mainAxis, direction );
		var isMainAxisRow = Axis.IsRow( mainAxis );
		var isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;

		var mainDimension = isMainAxisRow ? MeasureScope.Width : MeasureScope.Height;
		var crossDimension = isMainAxisRow ? MeasureScope.Height : MeasureScope.Width;

		// A measure-only pass that only has to produce the main-axis size (the caller wants
		// this node's flex basis) skips everything cross-axis: the children are still flexed on the main
		// axis but are themselves only asked for their main-axis size, which is stretch-fit at that point
		// and therefore free through shared measurement. The cross size is not computed unless it is
		// stretch-fit too, in which case the pass is a complete measurement after all. A request for the
		// cross-axis size needs the main axis resolved anyway, so it is a full measurement.
		var mainAxisOnly = !performLayout && scope == mainDimension;
		var computedScope = mainAxisOnly && (isMainAxisRow ? heightSizingMode : widthSizingMode) != SizingMode.StretchFit
			? mainDimension
			: MeasureScope.Both;
		var childScope = mainAxisOnly ? mainDimension : crossDimension;

		var mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;
		var crossAxisOwnerSize = isMainAxisRow ? ownerHeight : ownerWidth;

		var paddingAndBorderAxisMain = PaddingAndBorderForAxis( node, mainAxis, direction, ownerWidth );
		var paddingAndBorderAxisCross = PaddingAndBorderForAxis( node, crossAxis, direction, ownerWidth );
		var leadingPaddingAndBorderCross = node.Style.ComputeFlexStartPaddingAndBorder( crossAxis, direction, ownerWidth );

		var sizingModeMainDim = isMainAxisRow ? widthSizingMode : heightSizingMode;
		var sizingModeCrossDim = isMainAxisRow ? heightSizingMode : widthSizingMode;

		var paddingAndBorderAxisRow = isMainAxisRow ? paddingAndBorderAxisMain : paddingAndBorderAxisCross;
		var paddingAndBorderAxisColumn = isMainAxisRow ? paddingAndBorderAxisCross : paddingAndBorderAxisMain;

		// STEP 2: DETERMINE AVAILABLE SIZE IN MAIN AND CROSS DIRECTIONS

		var availableInnerWidth = CalculateAvailableInnerDimension(
			node,
			direction,
			Dimension.Width,
			availableWidth - marginAxisRow,
			paddingAndBorderAxisRow,
			ownerWidth,
			ownerWidth
		);
		var availableInnerHeight = CalculateAvailableInnerDimension(
			node,
			direction,
			Dimension.Height,
			availableHeight - marginAxisColumn,
			paddingAndBorderAxisColumn,
			ownerHeight,
			ownerWidth
		);

		var availableInnerMainDim = isMainAxisRow ? availableInnerWidth : availableInnerHeight;
		var availableInnerCrossDim = isMainAxisRow ? availableInnerHeight : availableInnerWidth;

		// STEP 3: DETERMINE FLEX BASIS FOR EACH ITEM

		// Computed basis + margins + gap
		float totalMainDim = 0;
		totalMainDim += ComputeFlexBasisForChildren(
			node,
			children,
			availableInnerWidth,
			availableInnerHeight,
			widthSizingMode,
			heightSizingMode,
			direction,
			mainAxis,
			performLayout,
			depth,
			generationCount,
			minContentWidth,
			minContentHeight
		);

		if ( childCount > 1 )
		{
			totalMainDim += node.Style.ComputeGapForAxis( mainAxis, availableInnerMainDim ) * (childCount - 1);
		}

		var mainAxisOverflows = (sizingModeMainDim != SizingMode.MaxContent) && totalMainDim > availableInnerMainDim;

		if ( isNodeFlexWrap && mainAxisOverflows && sizingModeMainDim == SizingMode.FitContent )
		{
			sizingModeMainDim = SizingMode.StretchFit;
		}

		// STEP 4: COLLECT FLEX ITEMS INTO FLEX LINES

		// Index of the beginning of the current line
		int startOfLineIndex = 0;

		// Number of lines.
		int lineCount = 0;

		// Accumulated cross dimensions of all lines so far.
		float totalLineCrossDim = 0;

		var crossAxisGap = node.Style.ComputeGapForAxis( crossAxis, availableInnerCrossDim );

		// Max main dimension of all the lines.
		float maxLineMainDim = 0;

		var flexLine = RentFlexLine();

		for ( ; startOfLineIndex < children.Count; lineCount++ )
		{
			CalculateFlexLine(
				flexLine,
				node,
				ownerDirection,
				ownerWidth,
				mainAxisOwnerSize,
				availableInnerWidth,
				availableInnerMainDim,
				children,
				ref startOfLineIndex,
				lineCount
			);

			// If we don't need to measure the cross axis, we can skip the entire flex step.
			var canSkipFlex = !performLayout && sizingModeCrossDim == SizingMode.StretchFit;

			// STEP 5: RESOLVING FLEXIBLE LENGTHS ON MAIN AXIS
			// Calculate the remaining available space that needs to be allocated. If the main dimension
			// size isn't known, it is computed based on the line length, so there's no more space left to
			// distribute.

			var sizeBasedOnContent = false;
			// If we don't measure with exact main dimension we want to ensure we don't violate min and max
			if ( sizingModeMainDim != SizingMode.StretchFit )
			{
				var style = node.Style;
				var minInnerWidth = style.ResolvedMinDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) - paddingAndBorderAxisRow;
				var maxInnerWidth = style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) - paddingAndBorderAxisRow;
				var minInnerHeight = style.ResolvedMinDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) - paddingAndBorderAxisColumn;
				var maxInnerHeight = style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) - paddingAndBorderAxisColumn;

				var minInnerMainDim = isMainAxisRow ? minInnerWidth : minInnerHeight;
				var maxInnerMainDim = isMainAxisRow ? maxInnerWidth : maxInnerHeight;

				if ( Num.IsDefined( minInnerMainDim ) && flexLine.SizeConsumed < minInnerMainDim )
				{
					availableInnerMainDim = minInnerMainDim;
				}
				else if ( Num.IsDefined( maxInnerMainDim ) && flexLine.SizeConsumed > maxInnerMainDim )
				{
					availableInnerMainDim = maxInnerMainDim;
				}
				else
				{
					if ( (Num.IsDefined( flexLine.Layout.TotalFlexGrowFactors ) && flexLine.Layout.TotalFlexGrowFactors == 0)
						|| (Num.IsDefined( node.ResolveFlexGrow() ) && node.ResolveFlexGrow() == 0) )
					{
						// If we don't have any children to flex or we can't flex the node itself, space we've
						// used is all space we need. Root node also should be shrunk to minimum
						availableInnerMainDim = flexLine.SizeConsumed;
					}

					sizeBasedOnContent = true;
				}
			}

			if ( !sizeBasedOnContent && Num.IsDefined( availableInnerMainDim ) )
			{
				flexLine.Layout.RemainingFreeSpace = availableInnerMainDim - flexLine.SizeConsumed;
			}
			else if ( flexLine.SizeConsumed < 0 )
			{
				// The available inner main-axis size is indefinite, so the node is sized from its content.
				// A negative SizeConsumed means the node allocates zero points for content, making
				// RemainingFreeSpace equal to the negated SizeConsumed value.
				flexLine.Layout.RemainingFreeSpace = -flexLine.SizeConsumed;
			}

			if ( !canSkipFlex )
			{
				ResolveFlexibleLength(
					node,
					flexLine,
					mainAxis,
					crossAxis,
					direction,
					ownerWidth,
					mainAxisOwnerSize,
					availableInnerMainDim,
					availableInnerCrossDim,
					availableInnerWidth,
					availableInnerHeight,
					mainAxisOverflows,
					sizingModeCrossDim,
					performLayout,
					depth,
					generationCount,
					isMainAxisRow ? minContentHeight : minContentWidth,
					childScope
				);
			}

			node.Layout.HadOverflow = node.Layout.HadOverflow || (flexLine.Layout.RemainingFreeSpace < 0);

			// STEP 6: MAIN-AXIS JUSTIFICATION & CROSS-AXIS SIZE DETERMINATION

			// At this point, all the children have their dimensions set in the main axis. Their dimensions
			// are also set in the cross axis with the exception of items that are aligned "stretch". We need
			// to compute these stretch values and set the final positions.

			JustifyMainAxis(
				node,
				flexLine,
				mainAxis,
				crossAxis,
				direction,
				sizingModeMainDim,
				sizingModeCrossDim,
				mainAxisOwnerSize,
				ownerWidth,
				availableInnerMainDim,
				availableInnerCrossDim,
				availableInnerWidth,
				performLayout,
				mainAxisOnly
			);

			var containerCrossAxis = availableInnerCrossDim;
			if ( sizingModeCrossDim == SizingMode.MaxContent || sizingModeCrossDim == SizingMode.FitContent )
			{
				// Compute the cross axis from the max cross dimension of the children.
				containerCrossAxis = BoundAxis( node, crossAxis, direction, flexLine.Layout.CrossDimension + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth ) - paddingAndBorderAxisCross;
			}

			// If there's no flex wrap, the cross dimension is defined by the container.
			if ( !isNodeFlexWrap && sizingModeCrossDim == SizingMode.StretchFit )
			{
				flexLine.Layout.CrossDimension = availableInnerCrossDim;
			}

			// As-per https://www.w3.org/TR/css-flexbox-1/#cross-sizing, the cross-size of the line within a
			// single-line container should be bound to min/max constraints before alignment within the
			// line. In a multi-line container, affecting alignment between the lines.
			if ( !isNodeFlexWrap )
			{
				flexLine.Layout.CrossDimension = BoundAxis( node, crossAxis, direction, flexLine.Layout.CrossDimension + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth ) - paddingAndBorderAxisCross;
			}

			// STEP 7: CROSS-AXIS ALIGNMENT
			// We can skip child alignment if we're just measuring the container.
			if ( performLayout )
			{
				foreach ( var child in flexLine.ItemsInFlow )
				{
					var leadingCrossDim = leadingPaddingAndBorderCross;

					// For a relative children, we're either using alignItems (owner) or alignSelf (child) in
					// order to determine the position in the cross axis
					var alignItem = Axis.ResolveChildAlignment( node, child );

					// If the child uses align stretch, we need to lay it out one more time, this time forcing
					// the cross-axis size to be the computed cross size for the current line.
					if ( alignItem == Align.Stretch && !child.Style.FlexStartMarginIsAuto( crossAxis, direction ) && !child.Style.FlexEndMarginIsAuto( crossAxis, direction ) )
					{
						// If the child defines a definite size for its cross axis, there's no need to stretch.
						if ( !child.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim ) )
						{
							var childMainSize = child.Layout.MeasuredDimension( Axis.DimensionOf( mainAxis ) );
							var childStyle = child.Style;
							var childCrossSize = Num.IsDefined( childStyle.AspectRatio )
								? child.Style.ComputeMarginForAxis( crossAxis, availableInnerWidth ) + (isMainAxisRow ? childMainSize / childStyle.AspectRatio : childMainSize * childStyle.AspectRatio)
								: flexLine.Layout.CrossDimension;

							childMainSize += child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );

							var childMainSizingMode = SizingMode.StretchFit;
							var childCrossSizingMode = SizingMode.StretchFit;
							ConstrainMaxSizeForMode( child, direction, mainAxis, availableInnerMainDim, availableInnerWidth, ref childMainSizingMode, ref childMainSize );
							ConstrainMaxSizeForMode( child, direction, crossAxis, availableInnerCrossDim, availableInnerWidth, ref childCrossSizingMode, ref childCrossSize );

							var childWidth = isMainAxisRow ? childMainSize : childCrossSize;
							var childHeight = !isMainAxisRow ? childMainSize : childCrossSize;

							var alignContent = node.Style.AlignContent;
							var crossAxisDoesNotGrow = alignContent != Align.Stretch && isNodeFlexWrap;
							var childWidthSizingMode = Num.IsUndefined( childWidth ) || (!isMainAxisRow && crossAxisDoesNotGrow) ? SizingMode.MaxContent : SizingMode.StretchFit;
							var childHeightSizingMode = Num.IsUndefined( childHeight ) || (isMainAxisRow && crossAxisDoesNotGrow) ? SizingMode.MaxContent : SizingMode.StretchFit;

							CalculateLayoutInternal( child, childWidth, childHeight, direction, childWidthSizingMode, childHeightSizingMode, availableInnerWidth, availableInnerHeight, true, depth, generationCount );
						}
					}
					else
					{
						var remainingCrossDim = containerCrossAxis - child.DimensionWithMargin( crossAxis, availableInnerWidth );

						if ( child.Style.FlexStartMarginIsAuto( crossAxis, direction ) && child.Style.FlexEndMarginIsAuto( crossAxis, direction ) )
						{
							leadingCrossDim += Num.MaxOrDefined( 0.0f, remainingCrossDim / 2 );
						}
						else if ( child.Style.FlexEndMarginIsAuto( crossAxis, direction ) )
						{
							// No-Op
						}
						else if ( child.Style.FlexStartMarginIsAuto( crossAxis, direction ) )
						{
							leadingCrossDim += Num.MaxOrDefined( 0.0f, remainingCrossDim );
						}
						else if ( alignItem == Align.FlexStart )
						{
							// No-Op
						}
						else if ( alignItem == Align.Center )
						{
							leadingCrossDim += remainingCrossDim / 2;
						}
						else
						{
							leadingCrossDim += remainingCrossDim;
						}
					}

					// And we apply the position
					child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ), child.Layout.Position( Axis.FlexStartEdge( crossAxis ) ) + totalLineCrossDim + leadingCrossDim );
				}
			}

			var appliedCrossGap = lineCount != 0 ? crossAxisGap : 0.0f;
			totalLineCrossDim += flexLine.Layout.CrossDimension + appliedCrossGap;
			maxLineMainDim = Num.MaxOrDefined( maxLineMainDim, flexLine.Layout.MainDimension );
		}

		ReturnFlexLine( flexLine );

		// STEP 8: MULTI-LINE CONTENT ALIGNMENT
		// currentLead stores the size of the cross dim
		if ( performLayout && (isNodeFlexWrap || IsBaselineLayout( node )) )
		{
			float leadPerLine = 0;
			var currentLead = leadingPaddingAndBorderCross;
			float extraSpacePerLine = 0;

			var unclampedCrossDim = sizingModeCrossDim == SizingMode.StretchFit
				? availableInnerCrossDim + paddingAndBorderAxisCross
				: node.HasDefiniteLength( Axis.DimensionOf( crossAxis ), crossAxisOwnerSize )
					? node.GetResolvedDimension( direction, Axis.DimensionOf( crossAxis ), crossAxisOwnerSize, ownerWidth )
					: totalLineCrossDim + paddingAndBorderAxisCross;

			var innerCrossDim = BoundAxis( node, crossAxis, direction, unclampedCrossDim, ownerHeight, ownerWidth ) - paddingAndBorderAxisCross;

			var remainingAlignContentDim = innerCrossDim - totalLineCrossDim;

			var alignContent = remainingAlignContentDim >= 0 ? node.Style.AlignContent : Axis.FallbackAlignment( node.Style.AlignContent );

			switch ( alignContent )
			{
				case Align.FlexEnd:
					currentLead += remainingAlignContentDim;
					break;
				case Align.Center:
					currentLead += remainingAlignContentDim / 2;
					break;
				case Align.Stretch:
					extraSpacePerLine = remainingAlignContentDim / lineCount;
					break;
				case Align.SpaceAround:
					currentLead += remainingAlignContentDim / (2 * lineCount);
					leadPerLine = remainingAlignContentDim / lineCount;
					break;
				case Align.SpaceEvenly:
					currentLead += remainingAlignContentDim / (lineCount + 1);
					leadPerLine = remainingAlignContentDim / (lineCount + 1);
					break;
				case Align.SpaceBetween:
					if ( lineCount > 1 )
					{
						leadPerLine = remainingAlignContentDim / (lineCount - 1);
					}
					break;
				case Align.Auto:
				case Align.FlexStart:
				case Align.Baseline:
					break;
			}

			int endIndex = 0;
			for ( int i = 0; i < lineCount; i++ )
			{
				var startIndex = endIndex;
				var ii = startIndex;

				// compute the line's height and find the endIndex
				float lineHeight = 0;
				float maxAscentForCurrentLine = 0;
				float maxDescentForCurrentLine = 0;
				for ( ; ii < children.Count; ii++ )
				{
					var child = children[ii];
					if ( child.Style.Display == Display.None )
					{
						continue;
					}

					if ( !child.Style.IsOutOfFlow )
					{
						if ( child.LineIndex != i )
						{
							break;
						}

						if ( child.IsLayoutDimensionDefined( crossAxis ) )
						{
							lineHeight = Num.MaxOrDefined(
								lineHeight,
								child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) )
									+ child.Style.ComputeMarginForAxis( crossAxis, availableInnerWidth )
							);
						}

						if ( Axis.ResolveChildAlignment( node, child ) == Align.Baseline )
						{
							var ascent = CalculateBaseline( child ) + child.Style.ComputeFlexStartMargin( FlexDirection.Column, direction, availableInnerWidth );
							var descent = child.Layout.MeasuredDimension( Dimension.Height ) + child.Style.ComputeMarginForAxis( FlexDirection.Column, availableInnerWidth ) - ascent;
							maxAscentForCurrentLine = Num.MaxOrDefined( maxAscentForCurrentLine, ascent );
							maxDescentForCurrentLine = Num.MaxOrDefined( maxDescentForCurrentLine, descent );
							lineHeight = Num.MaxOrDefined( lineHeight, maxAscentForCurrentLine + maxDescentForCurrentLine );
						}
					}
				}
				endIndex = ii;
				currentLead += i != 0 ? crossAxisGap : 0;
				lineHeight += extraSpacePerLine;

				for ( ii = startIndex; ii < endIndex; ii++ )
				{
					var child = children[ii];
					if ( child.Style.Display == Display.None )
					{
						continue;
					}

					if ( !child.Style.IsOutOfFlow )
					{
						switch ( Axis.ResolveChildAlignment( node, child ) )
						{
							case Align.FlexStart:
								child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ), currentLead + child.Style.ComputeFlexStartPosition( crossAxis, direction, availableInnerWidth ) );
								break;

							case Align.FlexEnd:
								child.Layout.SetPosition(
									Axis.FlexStartEdge( crossAxis ),
									currentLead
										+ lineHeight
										- child.Style.ComputeFlexEndMargin( crossAxis, direction, availableInnerWidth )
										- child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) )
								);
								break;

							case Align.Center:
								{
									var childHeight = child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) );
									child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ), currentLead + (lineHeight - childHeight) / 2 );
									break;
								}

							case Align.Stretch:
								{
									child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ), currentLead + child.Style.ComputeFlexStartMargin( crossAxis, direction, availableInnerWidth ) );

									// Remeasure child with the line height as it as been only measured with the owners height yet.
									if ( !child.HasDefiniteLength( Axis.DimensionOf( crossAxis ), availableInnerCrossDim ) )
									{
										var childWidth = isMainAxisRow
											? (child.Layout.MeasuredDimension( Dimension.Width ) + child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth ))
											: leadPerLine + lineHeight;

										var childHeight = !isMainAxisRow
											? (child.Layout.MeasuredDimension( Dimension.Height ) + child.Style.ComputeMarginForAxis( crossAxis, availableInnerWidth ))
											: leadPerLine + lineHeight;

										if ( !(Num.InexactEquals( childWidth, child.Layout.MeasuredDimension( Dimension.Width ) )
											&& Num.InexactEquals( childHeight, child.Layout.MeasuredDimension( Dimension.Height ) )) )
										{
											CalculateLayoutInternal(
												child,
												childWidth,
												childHeight,
												direction,
												SizingMode.StretchFit,
												SizingMode.StretchFit,
												availableInnerWidth,
												availableInnerHeight,
												true,
												depth,
												generationCount
											);
										}
									}
									break;
								}

							case Align.Baseline:
								child.Layout.SetPosition( PhysicalEdge.Top,
									currentLead + maxAscentForCurrentLine - CalculateBaseline( child ) + child.Style.ComputeFlexStartPosition( FlexDirection.Column, direction, availableInnerCrossDim ) );
								break;

							case Align.Auto:
							case Align.SpaceBetween:
							case Align.SpaceAround:
							case Align.SpaceEvenly:
								break;
						}
					}
				}

				currentLead = currentLead + leadPerLine + lineHeight;
			}
		}

		// STEP 9: COMPUTING FINAL DIMENSIONS

		node.Layout.SetMeasuredDimension( Dimension.Width, BoundAxis( node, FlexDirection.Row, direction, availableWidth - marginAxisRow, ownerWidth, ownerWidth ) );
		node.Layout.SetMeasuredDimension( Dimension.Height, BoundAxis( node, FlexDirection.Column, direction, availableHeight - marginAxisColumn, ownerHeight, ownerWidth ) );

		// If the user didn't specify a width or height for the node, set the dimensions based on the children.
		if ( sizingModeMainDim == SizingMode.MaxContent || (node.Style.Overflow != Overflow.Scroll && sizingModeMainDim == SizingMode.FitContent) )
		{
			// Clamp the size to the min/max size, if specified, and make sure it doesn't go below the padding and border amount.
			node.Layout.SetMeasuredDimension( Axis.DimensionOf( mainAxis ), BoundAxis( node, mainAxis, direction, maxLineMainDim, mainAxisOwnerSize, ownerWidth ) );
		}
		else if ( sizingModeMainDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll )
		{
			node.Layout.SetMeasuredDimension( Axis.DimensionOf( mainAxis ), Num.MaxOrDefined(
				Num.MinOrDefined( availableInnerMainDim + paddingAndBorderAxisMain, BoundAxisWithinMinAndMax( node, direction, mainAxis, maxLineMainDim, mainAxisOwnerSize, ownerWidth ) ),
				paddingAndBorderAxisMain ) );
		}

		if ( computedScope != MeasureScope.Both )
		{
			// Main-axis-only pass: the children were not sized on the cross axis, so the content-based cross
			// size below would be meaningless. Leave it undefined; the caller does not read it.
			node.Layout.SetMeasuredDimension( Axis.DimensionOf( crossAxis ), Num.Undefined );
		}
		else if ( sizingModeCrossDim == SizingMode.MaxContent || (node.Style.Overflow != Overflow.Scroll && sizingModeCrossDim == SizingMode.FitContent) )
		{
			// Clamp the size to the min/max size, if specified, and make sure it doesn't go below the padding and border amount.
			node.Layout.SetMeasuredDimension( Axis.DimensionOf( crossAxis ), BoundAxis( node, crossAxis, direction, totalLineCrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth ) );
		}
		else if ( sizingModeCrossDim == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll )
		{
			node.Layout.SetMeasuredDimension( Axis.DimensionOf( crossAxis ), Num.MaxOrDefined(
				Num.MinOrDefined(
					availableInnerCrossDim + paddingAndBorderAxisCross,
					BoundAxisWithinMinAndMax(
						node,
						direction,
						crossAxis,
						totalLineCrossDim + paddingAndBorderAxisCross,
						crossAxisOwnerSize,
						ownerWidth
					)
				),
				paddingAndBorderAxisCross ) );
		}

		// As we only wrapped in normal direction yet, we need to reverse the positions on wrap-reverse.
		if ( performLayout && node.Style.FlexWrap == Wrap.WrapReverse )
		{
			foreach ( var child in children )
			{
				if ( !child.Style.IsOutOfFlow )
				{
					child.Layout.SetPosition( Axis.FlexStartEdge( crossAxis ),
						node.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) ) - child.Layout.Position( Axis.FlexStartEdge( crossAxis ) ) - child.Layout.MeasuredDimension( Axis.DimensionOf( crossAxis ) ) );
				}
			}
		}

		if ( performLayout )
		{
			// STEP 10: SETTING TRAILING POSITIONS FOR CHILDREN
			var needsMainTrailingPos = Axis.NeedsTrailingPosition( mainAxis );
			var needsCrossTrailingPos = Axis.NeedsTrailingPosition( crossAxis );

			if ( needsMainTrailingPos || needsCrossTrailingPos )
			{
				foreach ( var child in children )
				{
					// Absolute children will be handled by their containing block since we cannot guarantee
					// that their positions are set when their parents are done with layout.
					if ( child.Style.Display == Display.None || child.Style.IsOutOfFlow )
					{
						continue;
					}

					if ( needsMainTrailingPos )
					{
						Axis.SetChildTrailingPosition( node, child, mainAxis );
					}

					if ( needsCrossTrailingPos )
					{
						Axis.SetChildTrailingPosition( node, child, crossAxis );
					}
				}
			}

			// STEP 11: SIZING AND POSITIONING ABSOLUTE CHILDREN
			// Let the containing block layout its absolute descendants.
			if ( node.Style.PositionType != PositionType.Static || node.AlwaysFormsContainingBlock || depth == 1 )
			{
				LayoutAbsoluteDescendants(
					node,
					node,
					isMainAxisRow ? sizingModeMainDim : sizingModeCrossDim,
					direction,
					depth,
					generationCount,
					0.0f,
					0.0f,
					availableInnerWidth,
					availableInnerHeight
				);
			}
		}

		ReturnList( childBuffer );
		return computedScope;
	}
}
/*
 * Portions of the flex layout implementation and converted flex conformance tests derive from
 * Yoga 3.2.1: https://github.com/facebook/yoga/tree/v3.2.1
 * This source file is embedded in Sandbox.Layout.dll to retain the notice in binary distributions.
 *
 * MIT License
 * Copyright (c) Facebook, Inc. and its affiliates.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
