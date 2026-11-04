namespace Sandbox.Layout;

internal static partial class LayoutAlgorithm
{
	internal static void ConstrainMaxSizeForMode(
		LayoutNode node,
		Direction direction,
		FlexDirection axis,
		float ownerAxisSize,
		float ownerWidth,
		ref SizingMode mode,
		ref float size )
	{
		var maxSize = node.Style.ResolvedMaxDimension( direction, Axis.DimensionOf( axis ), ownerAxisSize, ownerWidth ) + node.Style.ComputeMarginForAxis( axis, ownerWidth );
		switch ( mode )
		{
			case SizingMode.StretchFit:
			case SizingMode.FitContent:
				size = (Num.IsUndefined( maxSize ) || size < maxSize) ? size : maxSize;
				break;
			case SizingMode.MaxContent:
				if ( Num.IsDefined( maxSize ) )
				{
					mode = SizingMode.FitContent;
					size = maxSize;
				}
				break;
			case SizingMode.MinContent:
				// A max size caps the result (BoundAxis) but must not turn a min-content request into fit-content.
				break;
		}
	}

	internal static float CalculateAvailableInnerDimension(
		LayoutNode node,
		Direction direction,
		Dimension dimension,
		float availableDim,
		float paddingAndBorder,
		float ownerDim,
		float ownerWidth )
	{
		var availableInnerDim = availableDim - paddingAndBorder;

		// Max dimension overrides predefined dimension value; Min dimension in turn overrides both of the above
		if ( Num.IsDefined( availableInnerDim ) )
		{
			// We want to make sure our available height does not violate min and max constraints
			var minDimensionOptional = node.Style.ResolvedMinDimension( direction, dimension, ownerDim, ownerWidth );
			var minInnerDim = Num.IsUndefined( minDimensionOptional ) ? 0.0f : minDimensionOptional - paddingAndBorder;

			var maxDimensionOptional = node.Style.ResolvedMaxDimension( direction, dimension, ownerDim, ownerWidth );
			var maxInnerDim = Num.IsUndefined( maxDimensionOptional ) ? float.MaxValue : maxDimensionOptional - paddingAndBorder;

			availableInnerDim = Num.MaxOrDefined( Num.MinOrDefined( availableInnerDim, maxInnerDim ), minInnerDim );
		}

		return availableInnerDim;
	}

	internal static float PaddingAndBorderForAxis( LayoutNode node, FlexDirection axis, Direction direction, float widthSize )
	{
		var style = node.Style;
		if ( !style.HasPaddingOrBorder )
		{
			return 0.0f;
		}

		return style.ComputeInlineStartPaddingAndBorder( axis, direction, widthSize )
			+ style.ComputeInlineEndPaddingAndBorder( axis, direction, widthSize );
	}

	internal static float BoundAxisWithinMinAndMax(
		LayoutNode node,
		Direction direction,
		FlexDirection axis,
		float value,
		float axisSize,
		float widthSize )
	{
		var style = node.Style;
		var dimension = Axis.IsColumn( axis ) ? Dimension.Height : Dimension.Width;

		// This runs several times per node per pass; with no min/max on the axis both resolve to undefined
		// and the clamps below are no-ops, so skip the resolution entirely.
		if ( !style.HasMinOrMaxDimension( dimension ) )
		{
			return value;
		}

		var min = style.ResolvedMinDimension( direction, dimension, axisSize, widthSize );
		var max = style.ResolvedMaxDimension( direction, dimension, axisSize, widthSize );

		// Legacy flex boxes use max-wins only at the root or inside another flex box.
		// Block/grid boxes and their items use CSS min-wins, including absolute items.
		// Contents wrappers don't establish a formatting context. Only walk them on a conflict.
		if ( min > max && max >= 0 )
		{
			var owner = node.Owner;
			while ( owner?.Style.Display == Display.Contents ) owner = owner.Owner;
			if ( style.Display != Display.Flex || (owner is not null && owner.Style.Display != Display.Flex) )
			{
				max = min;
			}
		}

		if ( min >= 0 && value < min )
		{
			value = min;
		}

		if ( max >= 0 && value > max )
		{
			value = max;
		}

		return value;
	}

	/// <summary>
	/// Like <see cref="BoundAxisWithinMinAndMax"/> but also ensures that the value doesn't go below the
	/// padding and border amount.
	/// </summary>
	internal static float BoundAxis( LayoutNode node, FlexDirection axis, Direction direction, float value, float axisSize, float widthSize )
	{
		return Num.MaxOrDefined(
			BoundAxisWithinMinAndMax( node, direction, axis, value, axisSize, widthSize ),
			PaddingAndBorderForAxis( node, axis, direction, widthSize )
		);
	}
}
