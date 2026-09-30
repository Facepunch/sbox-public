namespace Sandbox.Layout;

/// <summary>
/// CSS block layout (<c>display: block</c>): in-flow children stack vertically and fill the container's
/// content width; adjoining vertical margins collapse (CSS 2.1 §8.3.1, §9.4.1, §10.3.3, §10.6.3).
/// </summary>
internal static class BlockLayout
{
	internal static void Compute(
		LayoutNode node,
		float availableWidth,
		float availableHeight,
		Direction direction,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight,
		bool performLayout,
		int depth,
		uint generationCount,
		float marginAxisRow,
		float marginAxisColumn )
	{
		if ( node.InlineContent is not null )
		{
			InlineLayout.Compute( node, availableWidth - marginAxisRow, availableHeight - marginAxisColumn,
				direction, widthSizingMode, heightSizingMode, ownerWidth, ownerHeight, performLayout );
			return;
		}

		ref var layout = ref node.Layout;
		var style = node.Style;
		var isRtl = direction == Direction.RTL;

		var paddingAndBorderLeft = layout.Padding( PhysicalEdge.Left ) + layout.Border( PhysicalEdge.Left );
		var paddingAndBorderRight = layout.Padding( PhysicalEdge.Right ) + layout.Border( PhysicalEdge.Right );
		var paddingAndBorderTop = layout.Padding( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Top );
		var paddingAndBorderBottom = layout.Padding( PhysicalEdge.Bottom ) + layout.Border( PhysicalEdge.Bottom );
		var paddingAndBorderRow = paddingAndBorderLeft + paddingAndBorderRight;
		var paddingAndBorderColumn = paddingAndBorderTop + paddingAndBorderBottom;

		var childBuffer = LayoutAlgorithm.RentList();
		var children = node.GetLayoutChildren( childBuffer );
		LayoutAlgorithm.PropagateBaselineSensitivity( node, children );

		// ---------------------------------------------------------------------------------------------
		// Container width. Stretch-fit fills the available space; otherwise it's content based.
		// ---------------------------------------------------------------------------------------------

		var availableBorderBoxWidth = availableWidth - marginAxisRow;
		float outerWidth;
		if ( widthSizingMode == SizingMode.StretchFit )
		{
			outerWidth = LayoutAlgorithm.BoundAxis( node, FlexDirection.Row, direction, availableBorderBoxWidth, ownerWidth, ownerWidth );
		}
		else
		{
			var intrinsicMode = widthSizingMode == SizingMode.MinContent ? SizingMode.MinContent : SizingMode.MaxContent;
			var contentWidth = DetermineContentBasedWidth( node, children, direction, intrinsicMode, depth, generationCount );
			var width = contentWidth + paddingAndBorderRow;
			if ( widthSizingMode == SizingMode.FitContent && Num.IsDefined( availableBorderBoxWidth ) )
			{
				width = MathF.Min( width, availableBorderBoxWidth );
			}
			outerWidth = LayoutAlgorithm.BoundAxis( node, FlexDirection.Row, direction, width, ownerWidth, ownerWidth );
		}

		var innerWidth = MathF.Max( 0, outerWidth - paddingAndBorderRow );

		// Percentage heights of children resolve against our content height only when it is definite.
		float childOwnerHeight;
		if ( heightSizingMode == SizingMode.StretchFit )
		{
			childOwnerHeight = LayoutAlgorithm.CalculateAvailableInnerDimension(
				node,
				direction,
				Dimension.Height,
				availableHeight - marginAxisColumn,
				paddingAndBorderColumn,
				ownerHeight,
				ownerWidth
			);
		}
		else
		{
			childOwnerHeight = node.HasDefiniteLength( Dimension.Height, ownerHeight )
				? MathF.Max( 0, node.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) - paddingAndBorderColumn )
				: Num.Undefined;
		}

		// ---------------------------------------------------------------------------------------------
		// Margin collapsing eligibility
		// ---------------------------------------------------------------------------------------------

		var isRoot = depth <= 1;
		var parentIsBlock = node.Owner is not null && node.Owner.Style.Display == Display.Block;
		var establishesNewBlockFormattingContext = style.Overflow != Overflow.Visible;
		var hasDefiniteHeight = node.HasDefiniteLength( Dimension.Height, ownerHeight );
		var resolvedHeight = hasDefiniteHeight
			? node.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth )
			: Num.Undefined;
		var resolvedMinHeight = style.ResolvedMinDimension( direction, Dimension.Height, ownerHeight, ownerWidth );

		var collapsible = !isRoot && parentIsBlock && !establishesNewBlockFormattingContext && !style.IsOutOfFlow;
		var ownTopCollapsesWithChildren = collapsible && paddingAndBorderTop == 0;
		var ownBottomCollapsesWithChildren = collapsible && paddingAndBorderBottom == 0 && !hasDefiniteHeight;

		var hasStylesPreventingCollapseThrough = isRoot
			|| establishesNewBlockFormattingContext
			|| style.IsOutOfFlow
			|| paddingAndBorderTop > 0
			|| paddingAndBorderBottom > 0
			|| (hasDefiniteHeight && resolvedHeight > 0)
			|| (Num.IsDefined( resolvedMinHeight ) && resolvedMinHeight > 0);

		// ---------------------------------------------------------------------------------------------
		// In-flow children
		// ---------------------------------------------------------------------------------------------

		var committedY = paddingAndBorderTop;
		var yOffsetForAbsolute = paddingAndBorderTop;
		var firstChildTopMarginSet = default( CollapsibleMargin );
		var activeMarginSet = default( CollapsibleMargin );
		var isCollapsingWithFirstMarginSet = true;
		var allChildrenCollapseThrough = true;
		var inFlowCount = 0;

		foreach ( var child in children )
		{
			child.ProcessDimensions();

			if ( child.Style.Display == Display.None )
			{
				LayoutAlgorithm.ZeroOutLayoutRecursively( child );
				child.HasNewLayout = true;
				child.SetDirty( false );
				continue;
			}

			var childStyle = child.Style;

			if ( childStyle.IsOutOfFlow )
			{
				child.Layout.StaticPositionX = isRtl ? outerWidth - paddingAndBorderRight : paddingAndBorderLeft;
				child.Layout.StaticPositionY = yOffsetForAbsolute;
				continue;
			}

			inFlowCount++;

			// Margins. Vertical percentages resolve against the containing block width, like horizontal ones.
			var marginLeftStyle = childStyle.ComputeMargin( PhysicalEdge.Left, direction );
			var marginRightStyle = childStyle.ComputeMargin( PhysicalEdge.Right, direction );
			var marginLeftAuto = marginLeftStyle.IsAuto;
			var marginRightAuto = marginRightStyle.IsAuto;
			var marginLeft = marginLeftAuto ? 0 : Num.UnwrapOrDefault( marginLeftStyle.Resolve( innerWidth ), 0 );
			var marginRight = marginRightAuto ? 0 : Num.UnwrapOrDefault( marginRightStyle.Resolve( innerWidth ), 0 );
			var marginTop = Num.UnwrapOrDefault( childStyle.ComputeMargin( PhysicalEdge.Top, direction ).Resolve( innerWidth ), 0 );
			var marginBottom = Num.UnwrapOrDefault( childStyle.ComputeMargin( PhysicalEdge.Bottom, direction ).Resolve( innerWidth ), 0 );
			var nonAutoXMargins = marginLeft + marginRight;
			var yMargins = marginTop + marginBottom;

			var contentBox = childStyle.BoxSizing == BoxSizing.ContentBox;
			var childPaddingAndBorderRow = LayoutAlgorithm.PaddingAndBorderForAxis( child, FlexDirection.Row, direction, innerWidth );
			var childPaddingAndBorderColumn = LayoutAlgorithm.PaddingAndBorderForAxis( child, FlexDirection.Column, direction, innerWidth );

			// Height: definite style height, else content sized (or from the aspect ratio below).
			var childHeight = Num.Undefined;
			var childHeightMode = SizingMode.MaxContent;
			if ( child.HasDefiniteLength( Dimension.Height, childOwnerHeight ) )
			{
				childHeight = child.GetResolvedDimension( direction, Dimension.Height, childOwnerHeight, innerWidth );
				childHeightMode = SizingMode.StretchFit;
			}

			// Width: definite style width, from the aspect ratio if the height is definite, otherwise stretch.
			var stretchWidth = MathF.Max( 0, innerWidth - nonAutoXMargins );
			float childWidth;
			if ( child.HasDefiniteLength( Dimension.Width, innerWidth ) )
			{
				childWidth = child.GetResolvedDimension( direction, Dimension.Width, innerWidth, innerWidth );
			}
			else if ( Num.IsDefined( childStyle.AspectRatio ) && childHeightMode == SizingMode.StretchFit )
			{
				childWidth = LayoutAlgorithm.HeightToWidth(
					LayoutAlgorithm.BoundAxis( child, FlexDirection.Column, direction, childHeight, childOwnerHeight, innerWidth ),
					childStyle.AspectRatio,
					contentBox,
					childPaddingAndBorderRow,
					childPaddingAndBorderColumn
				);
			}
			else
			{
				childWidth = stretchWidth;
			}

			if ( childHeightMode != SizingMode.StretchFit && Num.IsDefined( childStyle.AspectRatio ) )
			{
				childWidth = LayoutAlgorithm.BoundAxis( child, FlexDirection.Row, direction, childWidth, innerWidth, innerWidth );
				childHeight = LayoutAlgorithm.WidthToHeight(
					childWidth,
					childStyle.AspectRatio,
					contentBox,
					childPaddingAndBorderRow,
					childPaddingAndBorderColumn
				);
				childHeightMode = SizingMode.StretchFit;

				// A min/max-height clamp transfers back to the width through the ratio.
				LayoutAlgorithm.ApplyAspectRatioConstraints(
					child,
					direction,
					childStyle.AspectRatio,
					contentBox,
					childPaddingAndBorderRow,
					childPaddingAndBorderColumn,
					innerWidth,
					childOwnerHeight,
					ref childWidth,
					ref childHeight
				);
			}

			LayoutAlgorithm.CalculateLayoutInternal(
				child,
				childWidth + nonAutoXMargins,
				childHeightMode == SizingMode.StretchFit ? childHeight + yMargins : Num.Undefined,
				direction,
				SizingMode.StretchFit,
				childHeightMode,
				innerWidth,
				childOwnerHeight,
				performLayout,
				depth,
				generationCount
			);

			var finalWidth = child.Layout.MeasuredDimension( Dimension.Width );
			var finalHeight = child.Layout.MeasuredDimension( Dimension.Height );

			// Margins the child lets escape (block containers) plus its own.
			GetChildMarginInfo(
				child,
				direction,
				innerWidth,
				childOwnerHeight,
				finalHeight,
				out var childTopSet,
				out var childBottomSet,
				out var canCollapseThrough
			);
			var topMarginSet = childTopSet.CollapseWith( marginTop );
			var bottomMarginSet = childBottomSet.CollapseWith( marginBottom );

			// Auto horizontal margins share the free space.
			var freeX = MathF.Max( 0, stretchWidth - finalWidth );
			var autoCount = (marginLeftAuto ? 1 : 0) + (marginRightAuto ? 1 : 0);
			var autoMargin = autoCount > 0 ? freeX / autoCount : 0;
			var resolvedMarginLeft = marginLeftAuto ? autoMargin : marginLeft;
			var resolvedMarginRight = marginRightAuto ? autoMargin : marginRight;

			// Relative offsets.
			float insetX = 0;
			float insetY = 0;
			if ( childStyle.PositionType == PositionType.Relative )
			{
				var left = childStyle.ComputePosition( PhysicalEdge.Left, direction ).Resolve( innerWidth );
				var right = childStyle.ComputePosition( PhysicalEdge.Right, direction ).Resolve( innerWidth );
				var top = childStyle.ComputePosition( PhysicalEdge.Top, direction ).Resolve( childOwnerHeight );
				var bottom = childStyle.ComputePosition( PhysicalEdge.Bottom, direction ).Resolve( childOwnerHeight );
				insetX = isRtl
					? (Num.IsDefined( right ) ? -right : Num.IsDefined( left ) ? left : 0)
					: (Num.IsDefined( left ) ? left : Num.IsDefined( right ) ? -right : 0);
				insetY = Num.IsDefined( top ) ? top : Num.IsDefined( bottom ) ? -bottom : 0;
			}

			float yMarginOffset = 0;
			if ( !isCollapsingWithFirstMarginSet || !ownTopCollapsesWithChildren )
			{
				yMarginOffset = activeMarginSet.CollapseWith( topMarginSet ).Resolve();
			}

			var x = isRtl
				? outerWidth - paddingAndBorderRight - finalWidth - resolvedMarginRight + insetX
				: paddingAndBorderLeft + insetX + resolvedMarginLeft;
			var y = committedY + yMarginOffset + insetY;

			if ( performLayout )
			{
				child.Layout.SetPosition( PhysicalEdge.Left, x );
				child.Layout.SetPosition( PhysicalEdge.Top, y );
				child.Layout.SetPosition( PhysicalEdge.Right, outerWidth - x - finalWidth );
				child.Layout.SetPosition( PhysicalEdge.Bottom, 0 );
				child.Layout.SetMargin( PhysicalEdge.Left, resolvedMarginLeft );
				child.Layout.SetMargin( PhysicalEdge.Right, resolvedMarginRight );
			}

			if ( isCollapsingWithFirstMarginSet )
			{
				if ( canCollapseThrough )
				{
					firstChildTopMarginSet = firstChildTopMarginSet.CollapseWith( topMarginSet ).CollapseWith( bottomMarginSet );
				}
				else
				{
					firstChildTopMarginSet = firstChildTopMarginSet.CollapseWith( topMarginSet );
					isCollapsingWithFirstMarginSet = false;
				}
			}

			if ( canCollapseThrough )
			{
				activeMarginSet = activeMarginSet.CollapseWith( topMarginSet ).CollapseWith( bottomMarginSet );
				yOffsetForAbsolute = committedY + finalHeight + yMarginOffset;
			}
			else
			{
				allChildrenCollapseThrough = false;
				committedY = y - insetY + finalHeight;
				activeMarginSet = bottomMarginSet;
				yOffsetForAbsolute = committedY + activeMarginSet.Resolve();
			}
		}

		var lastChildBottomMarginSet = activeMarginSet;
		var bottomYMarginOffset = ownBottomCollapsesWithChildren ? 0 : lastChildBottomMarginSet.Resolve();
		committedY += paddingAndBorderBottom + bottomYMarginOffset;
		var contentHeight = MathF.Max( 0, committedY );

		// ---------------------------------------------------------------------------------------------
		// Container height
		// ---------------------------------------------------------------------------------------------

		float outerHeight;
		if ( heightSizingMode == SizingMode.StretchFit )
		{
			outerHeight = LayoutAlgorithm.BoundAxis(
				node,
				FlexDirection.Column,
				direction,
				availableHeight - marginAxisColumn,
				ownerHeight,
				ownerWidth
			);
		}
		else
		{
			var height = contentHeight;
			if ( heightSizingMode == SizingMode.FitContent && style.Overflow == Overflow.Scroll && Num.IsDefined( availableHeight ) )
			{
				height = MathF.Min( height, availableHeight - marginAxisColumn );
			}
			outerHeight = LayoutAlgorithm.BoundAxis( node, FlexDirection.Column, direction, height, ownerHeight, ownerWidth );
		}

		// align-content distributes leftover block-axis space (css-align-3 §6 applied to block containers).
		if ( performLayout && inFlowCount > 0 )
		{
			var freeSpace = outerHeight - contentHeight;
			var alignContent = freeSpace >= 0 ? style.AlignContent : Axis.FallbackAlignment( style.AlignContent );
			float offset = alignContent switch
			{
				Align.Center => freeSpace / 2,
				Align.FlexEnd => freeSpace,
				Align.SpaceAround => freeSpace / 2,
				Align.SpaceEvenly => freeSpace / 2,
				_ => 0,
			};

			if ( offset != 0 )
			{
				foreach ( var child in children )
				{
					if ( child.Style.Display == Display.None || child.Style.IsOutOfFlow )
					{
						continue;
					}

					child.Layout.SetPosition( PhysicalEdge.Top, child.Layout.Position( PhysicalEdge.Top ) + offset );
				}
			}
		}

		var heightConstrainedByMinHeight = Num.IsDefined( resolvedMinHeight ) && resolvedMinHeight > 0 && resolvedMinHeight >= outerHeight;

		layout.MarginTopSet = ownTopCollapsesWithChildren
			? firstChildTopMarginSet
			: CollapsibleMargin.FromMargin( Num.UnwrapOrDefault( style.ComputeMargin( PhysicalEdge.Top, direction ).Resolve( ownerWidth ), 0 ) );
		layout.MarginBottomSet = ownBottomCollapsesWithChildren && !heightConstrainedByMinHeight
			? lastChildBottomMarginSet
			: CollapsibleMargin.FromMargin( Num.UnwrapOrDefault( style.ComputeMargin( PhysicalEdge.Bottom, direction ).Resolve( ownerWidth ), 0 ) );
		layout.MarginsCanCollapseThrough = !hasStylesPreventingCollapseThrough && allChildrenCollapseThrough;

		layout.SetMeasuredDimension( Dimension.Width, outerWidth );
		layout.SetMeasuredDimension( Dimension.Height, outerHeight );
		layout.HadOverflow = contentHeight > outerHeight + 0.0001f;

		// ---------------------------------------------------------------------------------------------
		// Absolutely positioned descendants
		// ---------------------------------------------------------------------------------------------

		if ( performLayout && (style.PositionType != PositionType.Static || node.AlwaysFormsContainingBlock || depth == 1) )
		{
			LayoutAlgorithm.LayoutAbsoluteDescendants(
				node,
				node,
				widthSizingMode,
				direction,
				depth,
				generationCount,
				0.0f,
				0.0f,
				innerWidth,
				Num.IsDefined( childOwnerHeight ) ? childOwnerHeight : outerHeight - paddingAndBorderColumn
			);
		}

		LayoutAlgorithm.ReturnList( childBuffer );
	}

	/// <summary>
	/// The max-content (or min-content) width of the widest in-flow child, including its margins.
	/// </summary>
	private static float DetermineContentBasedWidth(
		LayoutNode node,
		List<LayoutNode> children,
		Direction direction,
		SizingMode mode,
		int depth,
		uint generationCount )
	{
		float maxChildWidth = 0;

		foreach ( var child in children )
		{
			if ( child.Style.Display == Display.None || child.Style.IsOutOfFlow )
			{
				continue;
			}

			child.ProcessDimensions();

			// Percentages against an indefinite width resolve to auto here.
			var marginLeft = child.Style.ComputeMargin( PhysicalEdge.Left, direction );
			var marginRight = child.Style.ComputeMargin( PhysicalEdge.Right, direction );
			var xMargins = (marginLeft.IsPoints ? marginLeft.Value : 0) + (marginRight.IsPoints ? marginRight.Value : 0);

			float width;
			var styleWidth = child.GetProcessedDimension( Dimension.Width );
			if ( styleWidth.IsPoints )
			{
				width = LayoutAlgorithm.BoundAxis(
					child,
					FlexDirection.Row,
					direction,
					child.GetResolvedDimension( direction, Dimension.Width, Num.Undefined, Num.Undefined ),
					Num.Undefined,
					Num.Undefined
				);
			}
			else
			{
				LayoutAlgorithm.CalculateLayoutInternal(
					child,
					Num.Undefined,
					Num.Undefined,
					direction,
					mode,
					SizingMode.MaxContent,
					Num.Undefined,
					Num.Undefined,
					false,
					depth,
					generationCount
				);
				width = child.Layout.MeasuredDimension( Dimension.Width );
			}

			maxChildWidth = MathF.Max( maxChildWidth, width + xMargins );
		}

		return maxChildWidth;
	}

	/// <summary>
	/// Collapsible margins a child contributes: block containers report the margins that escaped from their
	/// own children (computed by <see cref="Compute"/>); everything else contributes only its own margins,
	/// which the caller adds.
	/// </summary>
	private static void GetChildMarginInfo(
		LayoutNode child,
		Direction direction,
		float containerInnerWidth,
		float containerInnerHeight,
		float finalHeight,
		out CollapsibleMargin topSet,
		out CollapsibleMargin bottomSet,
		out bool canCollapseThrough )
	{
		var style = child.Style;
		var isBlockContainer = style.Display == Display.Block && !child.HasMeasureFunc && child.LayoutChildCount > 0;

		if ( isBlockContainer )
		{
			var paddingAndBorderTop = child.Layout.Padding( PhysicalEdge.Top ) + child.Layout.Border( PhysicalEdge.Top );
			var paddingAndBorderBottom = child.Layout.Padding( PhysicalEdge.Bottom ) + child.Layout.Border( PhysicalEdge.Bottom );
			var collapsible = style.Overflow == Overflow.Visible && !style.IsOutOfFlow;

			// Only the escaped children's margins; the child's own margins are added by the caller.
			topSet = collapsible && paddingAndBorderTop == 0 ? child.Layout.MarginTopSet : default;
			bottomSet = collapsible && paddingAndBorderBottom == 0 && !child.HasDefiniteLength( Dimension.Height, containerInnerHeight )
				? child.Layout.MarginBottomSet
				: default;
			canCollapseThrough = child.Layout.MarginsCanCollapseThrough;
			return;
		}

		topSet = default;
		bottomSet = default;

		var leafPaddingAndBorderTop = child.Layout.Padding( PhysicalEdge.Top ) + child.Layout.Border( PhysicalEdge.Top );
		var leafPaddingAndBorderBottom = child.Layout.Padding( PhysicalEdge.Bottom ) + child.Layout.Border( PhysicalEdge.Bottom );
		var minHeight = style.ResolvedMinDimension( direction, Dimension.Height, containerInnerHeight, containerInnerWidth );

		canCollapseThrough = style.Display == Display.Block
			&& !child.HasMeasureFunc
			&& style.Overflow == Overflow.Visible
			&& !style.IsOutOfFlow
			&& leafPaddingAndBorderTop == 0
			&& leafPaddingAndBorderBottom == 0
			&& finalHeight == 0
			&& !(Num.IsDefined( minHeight ) && minHeight > 0);
	}
}
