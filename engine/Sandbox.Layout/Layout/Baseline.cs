namespace Sandbox.Layout;

internal static partial class LayoutAlgorithm
{
	/// <summary>
	/// Marks the layout children of a container whose subtree a baseline computation may walk (the container
	/// or an ancestor aligns items by baseline), so their passes stay complete. Absolutely positioned
	/// children are skipped by <see cref="CalculateBaseline"/> and reset the flag.
	/// </summary>
	internal static void PropagateBaselineSensitivity( LayoutNode node, List<LayoutNode> children )
	{
		var sensitive = node.Layout.BaselineSensitive || node.IsBaselineContainer;
		foreach ( var child in children )
		{
			child.Layout.BaselineSensitive = sensitive && !child.Style.IsOutOfFlow;
		}
	}

	internal static float CalculateBaseline( LayoutNode node )
	{
		if ( node.Style.Display == Display.Block && node.InlineContent is not null )
			return node.Layout.InlineBaseline;

		if ( node.HasBaselineFunc )
		{
			var baseline = node.Baseline( node.Layout.MeasuredDimension( Dimension.Width ), node.Layout.MeasuredDimension( Dimension.Height ) );
			if ( float.IsNaN( baseline ) )
			{
				throw new InvalidOperationException( "Expect custom baseline function to not return NaN" );
			}

			return baseline;
		}

		if ( node.Style.Display == Display.Block && !node.HasMeasureFunc )
		{
			// CSS: a block's first baseline is its first in-flow child's; boxes without any text have none, so
			// the block synthesizes one from its bottom edge.
			return TryGetBlockBaseline( node, out var blockBaseline ) ? blockBaseline : node.Layout.MeasuredDimension( Dimension.Height );
		}

		LayoutNode baselineChild = null;
		var buffer = RentList();
		var children = node.GetLayoutChildren( buffer );
		foreach ( var child in children )
		{
			if ( child.LineIndex > 0 )
			{
				break;
			}

			if ( child.Style.IsOutOfFlow )
			{
				continue;
			}

			if ( Axis.ResolveChildAlignment( node, child ) == Align.Baseline || child.IsReferenceBaseline )
			{
				baselineChild = child;
				break;
			}

			baselineChild ??= child;
		}
		ReturnList( buffer );

		if ( baselineChild is null )
		{
			return node.Layout.MeasuredDimension( Dimension.Height );
		}

		var childBaseline = CalculateBaseline( baselineChild );
		return childBaseline + baselineChild.Layout.Position( PhysicalEdge.Top );
	}

	private static bool TryGetBlockBaseline( LayoutNode node, out float baseline )
	{
		baseline = 0;

		if ( node.HasBaselineFunc || node.HasMeasureFunc || node.InlineContent is not null )
		{
			baseline = CalculateBaseline( node );
			return true;
		}

		if ( node.Style.Display != Display.Block )
		{
			// Flex and grid containers synthesize a baseline from their items; an empty one has none.
			if ( node.LayoutChildCount == 0 )
			{
				return false;
			}

			baseline = CalculateBaseline( node );
			return true;
		}

		var buffer = RentList();
		var children = node.GetLayoutChildren( buffer );
		var found = false;
		foreach ( var child in children )
		{
			if ( child.Style.Display == Display.None || child.Style.IsOutOfFlow )
			{
				continue;
			}

			if ( TryGetBlockBaseline( child, out var childBaseline ) )
			{
				baseline = childBaseline + child.Layout.Position( PhysicalEdge.Top );
				found = true;
				break;
			}
		}
		ReturnList( buffer );
		return found;
	}

	internal static bool IsBaselineLayout( LayoutNode node )
	{
		if ( Axis.IsColumn( node.Style.FlexDirection ) )
		{
			return false;
		}

		// The node counts the children that align themselves by baseline, so a container without any (the
		// common case) is answered without walking the children; this runs once per line per pass.
		if ( !node.IsBaselineContainer )
		{
			return false;
		}

		if ( node.Style.AlignItems == Align.Baseline )
		{
			return true;
		}

		var buffer = RentList();
		var children = node.GetLayoutChildren( buffer );
		var result = false;
		foreach ( var child in children )
		{
			if ( !child.Style.IsOutOfFlow && child.Style.AlignSelf == Align.Baseline )
			{
				result = true;
				break;
			}
		}
		ReturnList( buffer );
		return result;
	}
}
