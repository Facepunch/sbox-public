namespace Sandbox.Layout;

/// <summary>
/// Sizing and positioning of absolutely positioned children.
/// </summary>
internal static partial class LayoutAlgorithm
{
	private static void LayoutFixedDescendants( LayoutNode node, ref LayoutNode viewport, float width, float height, Direction direction, int depth, uint generationCount )
	{
		if ( !node.SubtreeHasFixed || node.Style.Display == Display.None ) return;

		foreach ( var child in node.ChildList )
		{
			if ( !child.SubtreeHasFixed || child.Style.Display == Display.None ) continue;
			var childDirection = child.ResolveDirection( direction );
			if ( child.Style.PositionType == PositionType.Fixed && child.Style.Display != Display.Contents )
			{
				// A neutral solver context, never attached to the ownership tree. Root decoration and
				// flex/grid alignment must not inset or align the viewport's floaters.
				viewport ??= new LayoutNode();
				child.ProcessDimensions();
				child.Layout.StaticPositionX = 0;
				child.Layout.StaticPositionY = 0;
				LayoutAbsoluteChildCss( viewport, viewport, child, width, height, childDirection, depth, generationCount );
				child.HasNewLayout = true;
			}

			LayoutFixedDescendants( child, ref viewport, width, height, childDirection, depth + 1, generationCount );
		}
	}

	private static void SetFlexStartLayoutPosition(
		LayoutNode parent,
		LayoutNode child,
		Direction direction,
		FlexDirection axis,
		float containingBlockWidth )
	{
		var position = child.Style.ComputeFlexStartMargin( axis, direction, containingBlockWidth )
			+ parent.Layout.Border( Axis.FlexStartEdge( axis ) );
		position += parent.Layout.Padding( Axis.FlexStartEdge( axis ) );

		child.Layout.SetPosition( Axis.FlexStartEdge( axis ), position );
	}

	private static void SetFlexEndLayoutPosition(
		LayoutNode parent,
		LayoutNode child,
		Direction direction,
		FlexDirection axis,
		float containingBlockWidth )
	{
		var flexEndPosition = parent.Layout.Border( Axis.FlexEndEdge( axis ) )
			+ child.Style.ComputeFlexEndMargin( axis, direction, containingBlockWidth );
		flexEndPosition += parent.Layout.Padding( Axis.FlexEndEdge( axis ) );

		child.Layout.SetPosition(
			Axis.FlexStartEdge( axis ),
			Axis.GetPositionOfOppositeEdge( flexEndPosition, axis, parent, child )
		);
	}

	private static void SetCenterLayoutPosition(
		LayoutNode parent,
		LayoutNode child,
		Direction direction,
		FlexDirection axis,
		float containingBlockWidth )
	{
		var parentContentBoxSize = parent.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
			- parent.Layout.Border( Axis.FlexStartEdge( axis ) )
			- parent.Layout.Border( Axis.FlexEndEdge( axis ) );
		parentContentBoxSize -= parent.Layout.Padding( Axis.FlexStartEdge( axis ) );
		parentContentBoxSize -= parent.Layout.Padding( Axis.FlexEndEdge( axis ) );

		var childOuterSize = child.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
			+ child.Style.ComputeMarginForAxis( axis, containingBlockWidth );

		var position = (parentContentBoxSize - childOuterSize) / 2.0f
			+ parent.Layout.Border( Axis.FlexStartEdge( axis ) )
			+ child.Style.ComputeFlexStartMargin( axis, direction, containingBlockWidth );
		position += parent.Layout.Padding( Axis.FlexStartEdge( axis ) );

		child.Layout.SetPosition( Axis.FlexStartEdge( axis ), position );
	}

	private static void JustifyAbsoluteChild(
		LayoutNode parent,
		LayoutNode child,
		Direction direction,
		FlexDirection mainAxis,
		float containingBlockWidth )
	{
		switch ( parent.Style.JustifyContent )
		{
			case Justify.FlexStart:
			case Justify.SpaceBetween:
			case Justify.Stretch:
				SetFlexStartLayoutPosition( parent, child, direction, mainAxis, containingBlockWidth );
				break;
			case Justify.FlexEnd:
				SetFlexEndLayoutPosition( parent, child, direction, mainAxis, containingBlockWidth );
				break;
			case Justify.Center:
			case Justify.SpaceAround:
			case Justify.SpaceEvenly:
				SetCenterLayoutPosition( parent, child, direction, mainAxis, containingBlockWidth );
				break;
		}
	}

	private static void AlignAbsoluteChild(
		LayoutNode parent,
		LayoutNode child,
		Direction direction,
		FlexDirection crossAxis,
		float containingBlockWidth )
	{
		var itemAlign = Axis.ResolveChildAlignment( parent, child );
		var parentWrap = parent.Style.FlexWrap;
		if ( parentWrap == Wrap.WrapReverse )
		{
			if ( itemAlign == Align.FlexEnd )
			{
				itemAlign = Align.FlexStart;
			}
			else if ( itemAlign != Align.Center )
			{
				itemAlign = Align.FlexEnd;
			}
		}

		switch ( itemAlign )
		{
			case Align.Auto:
			case Align.FlexStart:
			case Align.Baseline:
			case Align.SpaceAround:
			case Align.SpaceBetween:
			case Align.Stretch:
			case Align.SpaceEvenly:
				SetFlexStartLayoutPosition( parent, child, direction, crossAxis, containingBlockWidth );
				break;
			case Align.FlexEnd:
				SetFlexEndLayoutPosition( parent, child, direction, crossAxis, containingBlockWidth );
				break;
			case Align.Center:
				SetCenterLayoutPosition( parent, child, direction, crossAxis, containingBlockWidth );
				break;
		}
	}

	/// <summary>
	/// Positions an absolute child on one flex axis using its insets, or the parent's alignment when unset.
	/// </summary>
	private static void PositionAbsoluteChild(
		LayoutNode containingNode,
		LayoutNode parent,
		LayoutNode child,
		Direction direction,
		FlexDirection axis,
		bool isMainAxis,
		float containingBlockWidth,
		float containingBlockHeight )
	{
		var isAxisRow = Axis.IsRow( axis );
		var containingBlockSize = isAxisRow ? containingBlockWidth : containingBlockHeight;

		// The inline-start position takes priority over the end position in the case that they are both
		// set and the node has a fixed width. Thus we only have 2 cases here: if inline-start is defined
		// and if inline-end is defined.
		//
		// Despite checking inline-start to honor prioritization of insets, we write to the flex-start edge
		// because this algorithm works by positioning on the flex-start edge and then filling in the
		// flex-end direction at the end if necessary.
		if ( child.Style.IsInlineStartPositionDefined( axis, direction )
			&& !child.Style.IsInlineStartPositionAuto( axis, direction ) )
		{
			var positionRelativeToInlineStart = child.Style.ComputeInlineStartPosition( axis, direction, containingBlockSize )
				+ containingNode.Style.ComputeInlineStartBorder( axis, direction )
				+ child.Style.ComputeInlineStartMargin( axis, direction, containingBlockSize );
			var positionRelativeToFlexStart = Axis.InlineStartEdge( axis, direction ) != Axis.FlexStartEdge( axis )
				? Axis.GetPositionOfOppositeEdge( positionRelativeToInlineStart, axis, containingNode, child )
				: positionRelativeToInlineStart;

			child.Layout.SetPosition( Axis.FlexStartEdge( axis ), positionRelativeToFlexStart );
		}
		else if ( child.Style.IsInlineEndPositionDefined( axis, direction )
			&& !child.Style.IsInlineEndPositionAuto( axis, direction ) )
		{
			var positionRelativeToInlineStart = containingNode.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
				- child.Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
				- containingNode.Style.ComputeInlineEndBorder( axis, direction )
				- child.Style.ComputeInlineEndMargin( axis, direction, containingBlockSize )
				- child.Style.ComputeInlineEndPosition( axis, direction, containingBlockSize );
			var positionRelativeToFlexStart = Axis.InlineStartEdge( axis, direction ) != Axis.FlexStartEdge( axis )
				? Axis.GetPositionOfOppositeEdge( positionRelativeToInlineStart, axis, containingNode, child )
				: positionRelativeToInlineStart;

			child.Layout.SetPosition( Axis.FlexStartEdge( axis ), positionRelativeToFlexStart );
		}
		else
		{
			if ( isMainAxis )
			{
				JustifyAbsoluteChild( parent, child, direction, axis, containingBlockWidth );
			}
			else
			{
				AlignAbsoluteChild( parent, child, direction, axis, containingBlockWidth );
			}
		}
	}

	internal static void LayoutAbsoluteChild(
		LayoutNode containingNode,
		LayoutNode node,
		LayoutNode child,
		float containingBlockWidth,
		float containingBlockHeight,
		SizingMode widthMode,
		Direction direction,
		int depth,
		uint generationCount )
	{
		var mainAxis = Axis.ResolveDirection( node.Style.FlexDirection, direction );
		var crossAxis = Axis.ResolveCrossDirection( mainAxis, direction );
		var isMainAxisRow = Axis.IsRow( mainAxis );

		var childWidth = Num.Undefined;
		var childHeight = Num.Undefined;
		var childWidthSizingMode = SizingMode.MaxContent;
		var childHeightSizingMode = SizingMode.MaxContent;

		var marginRow = child.Style.ComputeMarginForAxis( FlexDirection.Row, containingBlockWidth );
		var marginColumn = child.Style.ComputeMarginForAxis( FlexDirection.Column, containingBlockWidth );

		if ( child.HasDefiniteLength( Dimension.Width, containingBlockWidth ) )
		{
			childWidth = child.GetResolvedDimension( direction, Dimension.Width, containingBlockWidth, containingBlockWidth ) + marginRow;
		}
		else
		{
			// If the child doesn't have a specified width, compute the width based on the left/right
			// offsets if they're defined.
			if ( child.Style.IsFlexStartPositionDefined( FlexDirection.Row, direction )
				&& child.Style.IsFlexEndPositionDefined( FlexDirection.Row, direction )
				&& !child.Style.IsFlexStartPositionAuto( FlexDirection.Row, direction )
				&& !child.Style.IsFlexEndPositionAuto( FlexDirection.Row, direction ) )
			{
				childWidth = containingNode.Layout.MeasuredDimension( Dimension.Width )
					- (containingNode.Style.ComputeFlexStartBorder( FlexDirection.Row, direction )
						+ containingNode.Style.ComputeFlexEndBorder( FlexDirection.Row, direction ))
					- (child.Style.ComputeFlexStartPosition( FlexDirection.Row, direction, containingBlockWidth )
						+ child.Style.ComputeFlexEndPosition( FlexDirection.Row, direction, containingBlockWidth ));
				childWidth = BoundAxis( child, FlexDirection.Row, direction, childWidth, containingBlockWidth, containingBlockWidth );
			}
		}

		if ( child.HasDefiniteLength( Dimension.Height, containingBlockHeight ) )
		{
			childHeight = child.GetResolvedDimension( direction, Dimension.Height, containingBlockHeight, containingBlockWidth ) + marginColumn;
		}
		else
		{
			// If the child doesn't have a specified height, compute the height based on the top/bottom
			// offsets if they're defined.
			if ( child.Style.IsFlexStartPositionDefined( FlexDirection.Column, direction )
				&& child.Style.IsFlexEndPositionDefined( FlexDirection.Column, direction )
				&& !child.Style.IsFlexStartPositionAuto( FlexDirection.Column, direction )
				&& !child.Style.IsFlexEndPositionAuto( FlexDirection.Column, direction ) )
			{
				childHeight = containingNode.Layout.MeasuredDimension( Dimension.Height )
					- (containingNode.Style.ComputeFlexStartBorder( FlexDirection.Column, direction )
						+ containingNode.Style.ComputeFlexEndBorder( FlexDirection.Column, direction ))
					- (child.Style.ComputeFlexStartPosition( FlexDirection.Column, direction, containingBlockHeight )
						+ child.Style.ComputeFlexEndPosition( FlexDirection.Column, direction, containingBlockHeight ));
				childHeight = BoundAxis( child, FlexDirection.Column, direction, childHeight, containingBlockHeight, containingBlockWidth );
			}
		}

		// Exactly one dimension needs to be defined for us to be able to do aspect ratio calculation. One
		// dimension being the anchor and the other being flexible.
		var childStyle = child.Style;
		if ( Num.IsUndefined( childWidth ) ^ Num.IsUndefined( childHeight ) )
		{
			if ( Num.IsDefined( childStyle.AspectRatio ) )
			{
				if ( Num.IsUndefined( childWidth ) )
				{
					childWidth = marginRow + (childHeight - marginColumn) * childStyle.AspectRatio;
				}
				else if ( Num.IsUndefined( childHeight ) )
				{
					childHeight = marginColumn + (childWidth - marginRow) / childStyle.AspectRatio;
				}
			}
		}

		// If we're still missing one or the other dimension, measure the content.
		if ( Num.IsUndefined( childWidth ) || Num.IsUndefined( childHeight ) )
		{
			childWidthSizingMode = Num.IsUndefined( childWidth ) ? SizingMode.MaxContent : SizingMode.StretchFit;
			childHeightSizingMode = Num.IsUndefined( childHeight ) ? SizingMode.MaxContent : SizingMode.StretchFit;

			// If the size of the owner is defined then try to constrain the absolute child to that size as
			// well. This allows text within the absolute child to wrap to the size of its owner. This is
			// the same behavior as many browsers implement.
			if ( !isMainAxisRow
				&& Num.IsUndefined( childWidth )
				&& widthMode != SizingMode.MaxContent
				&& Num.IsDefined( containingBlockWidth )
				&& containingBlockWidth > 0 )
			{
				childWidth = containingBlockWidth;
				childWidthSizingMode = SizingMode.FitContent;
			}

			CalculateLayoutInternal(
				child,
				childWidth,
				childHeight,
				direction,
				childWidthSizingMode,
				childHeightSizingMode,
				containingBlockWidth,
				containingBlockHeight,
				false,
				depth,
				generationCount
			);

			childWidth = child.Layout.MeasuredDimension( Dimension.Width )
				+ child.Style.ComputeMarginForAxis( FlexDirection.Row, containingBlockWidth );
			childHeight = child.Layout.MeasuredDimension( Dimension.Height )
				+ child.Style.ComputeMarginForAxis( FlexDirection.Column, containingBlockWidth );
		}

		CalculateLayoutInternal(
			child,
			childWidth,
			childHeight,
			direction,
			SizingMode.StretchFit,
			SizingMode.StretchFit,
			containingBlockWidth,
			containingBlockHeight,
			true,
			depth,
			generationCount
		);

		PositionAbsoluteChild( containingNode, node, child, direction, mainAxis, true, containingBlockWidth, containingBlockHeight );
		PositionAbsoluteChild( containingNode, node, child, direction, crossAxis, false, containingBlockWidth, containingBlockHeight );
	}

	/// <summary>
	/// Absolute positioning per CSS 2.1 §10.3.7 / §10.6.4 for children of block and grid containers: insets,
	/// auto margins, aspect ratio and shrink-to-fit sizing, falling back to the static position the parent
	/// recorded while laying out its in-flow children.
	/// </summary>
	internal static void LayoutAbsoluteChildCss(
		LayoutNode containingNode,
		LayoutNode parent,
		LayoutNode child,
		float containingBlockWidth,
		float containingBlockHeight,
		Direction direction,
		int depth,
		uint generationCount )
	{
		var style = child.Style;
		var isRtl = direction == Direction.RTL;

		var left = style.ComputePosition( PhysicalEdge.Left, direction );
		var right = style.ComputePosition( PhysicalEdge.Right, direction );
		var top = style.ComputePosition( PhysicalEdge.Top, direction );
		var bottom = style.ComputePosition( PhysicalEdge.Bottom, direction );
		var leftValue = left.IsAuto ? Num.Undefined : left.Resolve( containingBlockWidth );
		var rightValue = right.IsAuto ? Num.Undefined : right.Resolve( containingBlockWidth );
		var topValue = top.IsAuto ? Num.Undefined : top.Resolve( containingBlockHeight );
		var bottomValue = bottom.IsAuto ? Num.Undefined : bottom.Resolve( containingBlockHeight );

		var marginLeftStyle = style.ComputeMargin( PhysicalEdge.Left, direction );
		var marginRightStyle = style.ComputeMargin( PhysicalEdge.Right, direction );
		var marginTopStyle = style.ComputeMargin( PhysicalEdge.Top, direction );
		var marginBottomStyle = style.ComputeMargin( PhysicalEdge.Bottom, direction );
		var marginLeft = marginLeftStyle.IsAuto ? 0 : Num.UnwrapOrDefault( marginLeftStyle.Resolve( containingBlockWidth ), 0 );
		var marginRight = marginRightStyle.IsAuto ? 0 : Num.UnwrapOrDefault( marginRightStyle.Resolve( containingBlockWidth ), 0 );
		var marginTop = marginTopStyle.IsAuto ? 0 : Num.UnwrapOrDefault( marginTopStyle.Resolve( containingBlockWidth ), 0 );
		var marginBottom = marginBottomStyle.IsAuto ? 0 : Num.UnwrapOrDefault( marginBottomStyle.Resolve( containingBlockWidth ), 0 );
		var xMargins = marginLeft + marginRight;
		var yMargins = marginTop + marginBottom;

		var aspectRatio = style.AspectRatio;
		var paddingAndBorderRow = PaddingAndBorderForAxis( child, FlexDirection.Row, direction, containingBlockWidth );
		var paddingAndBorderColumn = PaddingAndBorderForAxis( child, FlexDirection.Column, direction, containingBlockWidth );
		var contentBox = style.BoxSizing == BoxSizing.ContentBox;

		var width = child.HasDefiniteLength( Dimension.Width, containingBlockWidth )
			? child.GetResolvedDimension( direction, Dimension.Width, containingBlockWidth, containingBlockWidth )
			: Num.Undefined;
		var height = child.HasDefiniteLength( Dimension.Height, containingBlockHeight )
			? child.GetResolvedDimension( direction, Dimension.Height, containingBlockHeight, containingBlockWidth )
			: Num.Undefined;

		// Aspect ratio transfers a definite size to the other axis.
		if ( Num.IsDefined( aspectRatio ) )
		{
			if ( Num.IsUndefined( width ) && Num.IsDefined( height ) )
			{
				width = HeightToWidth( height, aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn );
			}
			else if ( Num.IsDefined( width ) && Num.IsUndefined( height ) )
			{
				height = WidthToHeight( width, aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn );
			}
		}

		// Both insets set: the box fills what's left.
		if ( Num.IsUndefined( width ) && Num.IsDefined( leftValue ) && Num.IsDefined( rightValue ) )
		{
			width = MathF.Max( 0, containingBlockWidth - leftValue - rightValue - xMargins );
			if ( Num.IsDefined( aspectRatio ) && Num.IsUndefined( height ) )
			{
				height = WidthToHeight(
					BoundAxis( child, FlexDirection.Row, direction, width, containingBlockWidth, containingBlockWidth ),
					aspectRatio,
					contentBox,
					paddingAndBorderRow,
					paddingAndBorderColumn
				);
			}
		}

		if ( Num.IsUndefined( height ) && Num.IsDefined( topValue ) && Num.IsDefined( bottomValue ) )
		{
			height = MathF.Max( 0, containingBlockHeight - topValue - bottomValue - yMargins );
			if ( Num.IsDefined( aspectRatio ) && Num.IsUndefined( width ) )
			{
				width = HeightToWidth(
					BoundAxis( child, FlexDirection.Column, direction, height, containingBlockHeight, containingBlockWidth ),
					aspectRatio,
					contentBox,
					paddingAndBorderRow,
					paddingAndBorderColumn
				);
			}
		}

		ApplyAspectRatioConstraints(
			child,
			direction,
			aspectRatio,
			contentBox,
			paddingAndBorderRow,
			paddingAndBorderColumn,
			containingBlockWidth,
			containingBlockHeight,
			ref width,
			ref height
		);

		// Still missing a size: shrink-to-fit the content into the space the insets leave.
		if ( Num.IsUndefined( width ) || Num.IsUndefined( height ) )
		{
			var availableWidth = Num.IsDefined( width )
				? width
				: MathF.Max( 0, containingBlockWidth - Num.UnwrapOrDefault( leftValue, 0 ) - Num.UnwrapOrDefault( rightValue, 0 ) - xMargins );
			var widthMode = Num.IsDefined( width )
				? SizingMode.StretchFit
				: Num.IsDefined( containingBlockWidth ) ? SizingMode.FitContent : SizingMode.MaxContent;
			if ( widthMode == SizingMode.MaxContent )
			{
				availableWidth = Num.Undefined;
			}

			var heightMode = Num.IsDefined( height ) ? SizingMode.StretchFit : SizingMode.MaxContent;

			CalculateLayoutInternal(
				child,
				Num.IsDefined( availableWidth ) ? availableWidth + xMargins : Num.Undefined,
				Num.IsDefined( height ) ? height + yMargins : Num.Undefined,
				direction,
				widthMode,
				heightMode,
				containingBlockWidth,
				containingBlockHeight,
				false,
				depth,
				generationCount
			);

			if ( Num.IsUndefined( width ) )
			{
				width = child.Layout.MeasuredDimension( Dimension.Width );
				if ( Num.IsDefined( aspectRatio ) && Num.IsUndefined( height ) )
				{
					height = WidthToHeight( width, aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn );
				}
			}

			if ( Num.IsUndefined( height ) )
			{
				height = child.Layout.MeasuredDimension( Dimension.Height );
			}

			ApplyAspectRatioConstraints(
				child,
				direction,
				aspectRatio,
				contentBox,
				paddingAndBorderRow,
				paddingAndBorderColumn,
				containingBlockWidth,
				containingBlockHeight,
				ref width,
				ref height
			);
		}

		CalculateLayoutInternal(
			child,
			width + xMargins,
			height + yMargins,
			direction,
			SizingMode.StretchFit,
			SizingMode.StretchFit,
			containingBlockWidth,
			containingBlockHeight,
			true,
			depth,
			generationCount
		);
		width = child.Layout.MeasuredDimension( Dimension.Width );
		height = child.Layout.MeasuredDimension( Dimension.Height );

		// Auto margins absorb free space when both insets are set (§10.3.7 / §10.6.4).
		if ( Num.IsDefined( leftValue ) && Num.IsDefined( rightValue ) && (marginLeftStyle.IsAuto || marginRightStyle.IsAuto) )
		{
			var freeSpace = containingBlockWidth - leftValue - rightValue - width - xMargins;
			if ( marginLeftStyle.IsAuto && marginRightStyle.IsAuto )
			{
				if ( freeSpace >= 0 )
				{
					marginLeft = marginRight = freeSpace / 2;
				}
				else if ( isRtl )
				{
					marginRight = freeSpace;
				}
				else
				{
					marginLeft = freeSpace;
				}
			}
			else if ( marginLeftStyle.IsAuto )
			{
				marginLeft = freeSpace;
			}
			else
			{
				marginRight = freeSpace;
			}
		}

		if ( Num.IsDefined( topValue ) && Num.IsDefined( bottomValue ) && (marginTopStyle.IsAuto || marginBottomStyle.IsAuto) )
		{
			var freeSpace = containingBlockHeight - topValue - bottomValue - height - yMargins;
			if ( marginTopStyle.IsAuto && marginBottomStyle.IsAuto )
			{
				if ( freeSpace >= 0 )
				{
					marginTop = marginBottom = freeSpace / 2;
				}
				else
				{
					marginTop = freeSpace;
				}
			}
			else if ( marginTopStyle.IsAuto )
			{
				marginTop = freeSpace;
			}
			else
			{
				marginBottom = freeSpace;
			}
		}

		child.Layout.SetMargin( PhysicalEdge.Left, marginLeft );
		child.Layout.SetMargin( PhysicalEdge.Right, marginRight );
		child.Layout.SetMargin( PhysicalEdge.Top, marginTop );
		child.Layout.SetMargin( PhysicalEdge.Bottom, marginBottom );

		// Positions: relative to the containing block when insets are set, otherwise relative to the parent
		// at the static position (the caller relies on that distinction, see LayoutAbsoluteDescendants).
		var containingBlockBorderLeft = containingNode.Style.ComputeInlineStartBorder( FlexDirection.Row, Direction.LTR );
		var containingBlockBorderTop = containingNode.Style.ComputeInlineStartBorder( FlexDirection.Column, direction );

		float x;
		if ( Num.IsDefined( leftValue ) && (!isRtl || Num.IsUndefined( rightValue )) )
		{
			x = containingBlockBorderLeft + leftValue + marginLeft;
		}
		else if ( Num.IsDefined( rightValue ) )
		{
			x = containingBlockBorderLeft + containingBlockWidth - rightValue - marginRight - width;
		}
		else if ( Num.IsDefined( leftValue ) )
		{
			x = containingBlockBorderLeft + leftValue + marginLeft;
		}
		else
		{
			x = style.PositionType == PositionType.Fixed ? marginLeft
				: isRtl ? child.Layout.StaticPositionX - marginRight - width : child.Layout.StaticPositionX + marginLeft;
		}

		float y;
		if ( Num.IsDefined( topValue ) )
		{
			y = containingBlockBorderTop + topValue + marginTop;
		}
		else if ( Num.IsDefined( bottomValue ) )
		{
			y = containingBlockBorderTop + containingBlockHeight - bottomValue - marginBottom - height;
		}
		else
		{
			y = child.Layout.StaticPositionY + marginTop;
		}

		child.Layout.SetPosition( PhysicalEdge.Left, x );
		child.Layout.SetPosition( PhysicalEdge.Top, y );
		child.Layout.SetPosition( PhysicalEdge.Right, 0 );
		child.Layout.SetPosition( PhysicalEdge.Bottom, 0 );
	}

	/// <summary>
	/// Clamp a size to its min/max constraints, transferring a clamp through the aspect ratio to the other
	/// axis (css-sizing-4 §5.2 "transferred size"). Undefined sizes are left alone.
	/// </summary>
	internal static void ApplyAspectRatioConstraints(
		LayoutNode node,
		Direction direction,
		float aspectRatio,
		bool contentBox,
		float paddingAndBorderRow,
		float paddingAndBorderColumn,
		float containingBlockWidth,
		float containingBlockHeight,
		ref float width,
		ref float height )
	{
		if ( Num.IsDefined( width ) )
		{
			var bound = BoundAxis( node, FlexDirection.Row, direction, width, containingBlockWidth, containingBlockWidth );
			if ( bound != width )
			{
				width = bound;
				if ( Num.IsDefined( aspectRatio )
					&& Num.IsDefined( height )
					&& !node.HasDefiniteLength( Dimension.Height, containingBlockHeight ) )
				{
					height = WidthToHeight( width, aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn );
				}
			}
		}

		if ( Num.IsDefined( height ) )
		{
			var bound = BoundAxis( node, FlexDirection.Column, direction, height, containingBlockHeight, containingBlockWidth );
			if ( bound != height )
			{
				height = bound;
				if ( Num.IsDefined( aspectRatio )
					&& Num.IsDefined( width )
					&& !node.HasDefiniteLength( Dimension.Width, containingBlockWidth ) )
				{
					width = BoundAxis(
						node,
						FlexDirection.Row,
						direction,
						HeightToWidth( height, aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn ),
						containingBlockWidth,
						containingBlockWidth
					);
				}
			}
		}
	}

	internal static float WidthToHeight(
		float width,
		float aspectRatio,
		bool contentBox,
		float paddingAndBorderRow,
		float paddingAndBorderColumn )
	{
		return contentBox ? (width - paddingAndBorderRow) / aspectRatio + paddingAndBorderColumn : width / aspectRatio;
	}

	internal static float HeightToWidth(
		float height,
		float aspectRatio,
		bool contentBox,
		float paddingAndBorderRow,
		float paddingAndBorderColumn )
	{
		return contentBox ? (height - paddingAndBorderColumn) * aspectRatio + paddingAndBorderRow : height * aspectRatio;
	}

	internal static bool LayoutAbsoluteDescendants(
		LayoutNode containingNode,
		LayoutNode currentNode,
		SizingMode widthSizingMode,
		Direction currentNodeDirection,
		int currentDepth,
		uint generationCount,
		float currentNodeLeftOffsetFromContainingBlock,
		float currentNodeTopOffsetFromContainingBlock,
		float containingNodeAvailableInnerWidth,
		float containingNodeAvailableInnerHeight )
	{
		var hasNewLayout = false;
		var buffer = RentList();
		var children = currentNode.GetLayoutChildren( buffer );

		foreach ( var child in children )
		{
			if ( child.Style.Display == Display.None )
			{
				continue;
			}
			else if ( child.Style.PositionType == PositionType.Absolute )
			{
				var containingBlockWidth = containingNode.Layout.MeasuredDimension( Dimension.Width )
					- containingNode.Style.ComputeBorderForAxis( FlexDirection.Row );
				var containingBlockHeight = containingNode.Layout.MeasuredDimension( Dimension.Height )
					- containingNode.Style.ComputeBorderForAxis( FlexDirection.Column );

				if ( currentNode.Style.Display is Display.Block or Display.Grid )
				{
					LayoutAbsoluteChildCss( containingNode, currentNode, child, containingBlockWidth, containingBlockHeight, currentNodeDirection, currentDepth, generationCount );
				}
				else
				{
					LayoutAbsoluteChild( containingNode, currentNode, child, containingBlockWidth, containingBlockHeight, widthSizingMode, currentNodeDirection, currentDepth, generationCount );
				}

				hasNewLayout = hasNewLayout || child.HasNewLayout;

				// The child is positioned only on the parent's flex-start edge at this point.
				// Additionally, this position should be interpreted relative to the containing block of the
				// child if it had insets defined. Adjust the position by subtracting the parent's offset from
				// the containing block. Getting that offset is complicated
				// since the two nodes can have different main/cross axes.
				var parentMainAxis = Axis.ResolveDirection( currentNode.Style.FlexDirection, currentNodeDirection );
				var parentCrossAxis = Axis.ResolveCrossDirection( parentMainAxis, currentNodeDirection );
				var parentIsFlex = currentNode.Style.Display is not (Display.Block or Display.Grid);

				if ( parentIsFlex && Axis.NeedsTrailingPosition( parentMainAxis ) )
				{
					var mainInsetsDefined = Axis.IsRow( parentMainAxis ) ? child.Style.HorizontalInsetsDefined : child.Style.VerticalInsetsDefined;
					Axis.SetChildTrailingPosition( mainInsetsDefined ? containingNode : currentNode, child, parentMainAxis );
				}
				if ( parentIsFlex && Axis.NeedsTrailingPosition( parentCrossAxis ) )
				{
					var crossInsetsDefined = Axis.IsRow( parentCrossAxis ) ? child.Style.HorizontalInsetsDefined : child.Style.VerticalInsetsDefined;
					Axis.SetChildTrailingPosition( crossInsetsDefined ? containingNode : currentNode, child, parentCrossAxis );
				}

				// At this point we know the left and top physical edges of the child are set with positions
				// that are relative to the containing block if insets are defined
				var childLeftPosition = child.Layout.Position( PhysicalEdge.Left );
				var childTopPosition = child.Layout.Position( PhysicalEdge.Top );

				var childLeftOffsetFromParent = child.Style.HorizontalInsetsDefined
					? childLeftPosition - currentNodeLeftOffsetFromContainingBlock
					: childLeftPosition;
				var childTopOffsetFromParent = child.Style.VerticalInsetsDefined
					? childTopPosition - currentNodeTopOffsetFromContainingBlock
					: childTopPosition;

				child.Layout.SetPosition( PhysicalEdge.Left, childLeftOffsetFromParent );
				child.Layout.SetPosition( PhysicalEdge.Top, childTopOffsetFromParent );
			}
			else if ( child.Style.PositionType == PositionType.Static && !child.AlwaysFormsContainingBlock && child.Style.Display != Display.Grid )
			{
				// Grid containers position their own absolute children against their grid areas.
				var childDirection = child.ResolveDirection( currentNodeDirection );

				// By now all descendants of the containing block that are not absolute will have their
				// positions set for left and top.
				var childLeftOffsetFromContainingBlock = currentNodeLeftOffsetFromContainingBlock + child.Layout.Position( PhysicalEdge.Left );
				var childTopOffsetFromContainingBlock = currentNodeTopOffsetFromContainingBlock + child.Layout.Position( PhysicalEdge.Top );

				hasNewLayout = LayoutAbsoluteDescendants(
					containingNode,
					child,
					widthSizingMode,
					childDirection,
					currentDepth + 1,
					generationCount,
					childLeftOffsetFromContainingBlock,
					childTopOffsetFromContainingBlock,
					containingNodeAvailableInnerWidth,
					containingNodeAvailableInnerHeight
				) || hasNewLayout;

				if ( hasNewLayout )
				{
					child.HasNewLayout = hasNewLayout;
				}
			}
		}

		ReturnList( buffer );
		return hasNewLayout;
	}
}
