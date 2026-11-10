namespace Sandbox.Layout;

internal struct FlexLineRunningLayout
{
	/// <summary>Total flex grow factors of flex items which are to be laid in the current line.</summary>
	public float TotalFlexGrowFactors;

	/// <summary>Total flex shrink factors of flex items which are to be laid in the current line.</summary>
	public float TotalFlexShrinkScaledFactors;

	/// <summary>
	/// The amount of available space within inner dimensions of the line which may still be distributed.
	/// </summary>
	public float RemainingFreeSpace;

	/// <summary>The line's main-axis size after accounting for each item's size, padding, margin, and border.</summary>
	public float MainDimension;

	/// <summary>The line's cross-axis size after accounting for each item's size, padding, margin, and border.</summary>
	public float CrossDimension;
}

internal sealed class FlexLine
{
	/// <summary>List of items in flow within the line.</summary>
	public readonly List<LayoutNode> ItemsInFlow = new();

	/// <summary>
	/// Accumulation of the dimensions and margin of all the children on the current line. This will be
	/// used in order to either set the dimensions of the node if none already exist or to compute the
	/// remaining space left for the flexible children.
	/// </summary>
	public float SizeConsumed;

	/// <summary>Number of edges along the line flow with an auto margin.</summary>
	public int NumberOfAutoMargins;

	/// <summary>Layout information about the line computed in steps after line breaking.</summary>
	public FlexLineRunningLayout Layout;

	public void Reset()
	{
		ItemsInFlow.Clear();
		SizeConsumed = 0;
		NumberOfAutoMargins = 0;
		Layout = default;
	}
}

internal static partial class FlexLayout
{
	/// <summary>
	/// Calculates where a line starting at the given child index should end, and populates the line.
	/// <paramref name="childIndex"/> is advanced past the items consumed.
	/// </summary>
	private static void CalculateFlexLine(
		FlexLine flexLine,
		LayoutNode node,
		Direction ownerDirection,
		float ownerWidth,
		float mainAxisOwnerSize,
		float availableInnerWidth,
		float availableInnerMainDim,
		List<LayoutNode> children,
		ref int childIndex,
		int lineCount )
	{
		flexLine.Reset();

		float sizeConsumed = 0.0f;
		float totalFlexGrowFactors = 0.0f;
		float totalFlexShrinkScaledFactors = 0.0f;
		int numberOfAutoMargins = 0;
		LayoutNode firstElementInLine = null;

		float sizeConsumedIncludingMinConstraint = 0;
		var direction = node.ResolveDirection( ownerDirection );
		var mainAxis = Axis.ResolveDirection( node.Style.FlexDirection, direction );
		var isNodeFlexWrap = node.Style.FlexWrap != Wrap.NoWrap;
		var gap = node.Style.ComputeGapForAxis( mainAxis, availableInnerMainDim );

		// Add items to the current line until it's full or we run out of items.
		for ( ; childIndex < children.Count; childIndex++ )
		{
			var child = children[childIndex];
			if ( child.Style.Display == Display.None || child.Style.IsOutOfFlow )
			{
				continue;
			}

			firstElementInLine ??= child;

			if ( child.Style.FlexStartMarginIsAuto( mainAxis, ownerDirection ) )
			{
				numberOfAutoMargins++;
			}

			if ( child.Style.FlexEndMarginIsAuto( mainAxis, ownerDirection ) )
			{
				numberOfAutoMargins++;
			}

			child.LineIndex = lineCount;
			var childMarginMainAxis = child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );
			var childLeadingGapMainAxis = child == firstElementInLine ? 0.0f : gap;
			var flexBasisWithMinAndMaxConstraints = LayoutAlgorithm.BoundAxisWithinMinAndMax(
				child,
				direction,
				mainAxis,
				child.Layout.ComputedFlexBasis,
				mainAxisOwnerSize,
				ownerWidth
			);

			// If this is a multi-line flow and this item pushes us over the available size, we've hit the
			// end of the current line. Break out of the loop and lay out the current line.
			if ( sizeConsumedIncludingMinConstraint
				+ flexBasisWithMinAndMaxConstraints
				+ childMarginMainAxis
				+ childLeadingGapMainAxis > availableInnerMainDim
				&& isNodeFlexWrap
				&& flexLine.ItemsInFlow.Count > 0 )
			{
				break;
			}

			sizeConsumedIncludingMinConstraint += flexBasisWithMinAndMaxConstraints + childMarginMainAxis + childLeadingGapMainAxis;
			sizeConsumed += flexBasisWithMinAndMaxConstraints + childMarginMainAxis + childLeadingGapMainAxis;

			if ( child.IsNodeFlexible() )
			{
				totalFlexGrowFactors += child.ResolveFlexGrow();

				// Unlike the grow factor, the shrink factor is scaled relative to the child dimension.
				totalFlexShrinkScaledFactors += -child.ResolveFlexShrink() * child.Layout.ComputedFlexBasis;
			}

			flexLine.ItemsInFlow.Add( child );
		}

		// The total flex factor needs to be floored to 1.
		if ( totalFlexGrowFactors > 0 && totalFlexGrowFactors < 1 )
		{
			totalFlexGrowFactors = 1;
		}

		// The total flex shrink factor needs to be floored to 1.
		if ( totalFlexShrinkScaledFactors > 0 && totalFlexShrinkScaledFactors < 1 )
		{
			totalFlexShrinkScaledFactors = 1;
		}

		flexLine.SizeConsumed = sizeConsumed;
		flexLine.NumberOfAutoMargins = numberOfAutoMargins;
		flexLine.Layout.TotalFlexGrowFactors = totalFlexGrowFactors;
		flexLine.Layout.TotalFlexShrinkScaledFactors = totalFlexShrinkScaledFactors;
	}
}
