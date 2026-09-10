namespace Sandbox.Layout;

/// <summary>Block sizing around an externally shaped, text-only inline formatting context.</summary>
internal static class InlineLayout
{
	internal static void Compute( LayoutNode node, float width, float height, Direction direction,
		SizingMode widthMode, SizingMode heightMode, float ownerWidth, float ownerHeight, bool performLayout )
	{
		ref var layout = ref node.Layout;
		var left = layout.Padding( PhysicalEdge.Left ) + layout.Border( PhysicalEdge.Left );
		var top = layout.Padding( PhysicalEdge.Top ) + layout.Border( PhysicalEdge.Top );
		var horizontal = left + layout.Padding( PhysicalEdge.Right ) + layout.Border( PhysicalEdge.Right );
		var vertical = top + layout.Padding( PhysicalEdge.Bottom ) + layout.Border( PhysicalEdge.Bottom );
		if ( widthMode != SizingMode.StretchFit )
		{
			var intrinsic = node.InlineContent.Measure( float.NaN, widthMode == SizingMode.MinContent ).Width + horizontal;
			width = widthMode == SizingMode.FitContent ? MathF.Min( width, intrinsic ) : intrinsic;
		}
		width = LayoutAlgorithm.BoundAxis( node, FlexDirection.Row, direction, width, ownerWidth, ownerWidth );
		var innerWidth = MathF.Max( 0, width - horizontal );
		// Baseline is needed during measurement too. The adapter returns results, but only a final pass
		// publishes descendant boxes. A speculative flex/grid probe must not replace interactive geometry.
		var result = node.InlineContent.Layout( innerWidth );
		var contentHeight = result.Size.Height + vertical;
		if ( heightMode != SizingMode.StretchFit )
			height = heightMode == SizingMode.FitContent && node.Style.Overflow == Overflow.Scroll
				? MathF.Min( height, contentHeight ) : contentHeight;
		height = LayoutAlgorithm.BoundAxis( node, FlexDirection.Column, direction, height, ownerHeight, ownerWidth );
		layout.SetMeasuredDimension( Dimension.Width, width );
		layout.SetMeasuredDimension( Dimension.Height, height );
		layout.InlineBaseline = top + result.Baseline;
		layout.MarginsCanCollapseThrough = false;
		layout.MarginTopSet = default;
		layout.MarginBottomSet = default;
		layout.HadOverflow = result.Size.Width > innerWidth || contentHeight > height;
		if ( !performLayout ) return;

		foreach ( var child in node.Children )
			Publish( child, result.Fragments, 0, 0, left, top, direction );
	}

	private static void Publish( LayoutNode node,
		IReadOnlyList<InlineFragment> fragments, float parentX, float parentY, float left, float top, Direction direction )
	{
		var owned = new List<InlineFragment>();
		foreach ( var fragment in fragments )
		{
			for ( var owner = fragment.Owner; owner is not null; owner = owner.Owner )
			{
				if ( owner != node ) continue;
				owned.Add( fragment );
				break;
			}
		}
		float x = 0, y = 0, right = 0, bottom = 0;
		if ( owned.Count > 0 )
		{
			x = owned[0].X;
			y = owned[0].Y;
			right = x + owned[0].Width;
			bottom = y + owned[0].Height;
			foreach ( var fragment in owned )
			{
				x = MathF.Min( x, fragment.X );
				y = MathF.Min( y, fragment.Y );
				right = MathF.Max( right, fragment.X + fragment.Width );
				bottom = MathF.Max( bottom, fragment.Y + fragment.Height );
			}
		}
		node.Layout.Reset();
		node.Layout.Direction = direction;
		node.Layout.SetPosition( PhysicalEdge.Left, x - parentX + left );
		node.Layout.SetPosition( PhysicalEdge.Top, y - parentY + top );
		node.Layout.SetDimension( Dimension.Width, right - x );
		node.Layout.SetDimension( Dimension.Height, bottom - y );
		node.Layout.SetMeasuredDimension( Dimension.Width, right - x );
		node.Layout.SetMeasuredDimension( Dimension.Height, bottom - y );
		node.InlineFragments = owned;
		node.HasNewLayout = true;
		node.SetDirty( false );
		foreach ( var child in node.Children ) Publish( child, fragments, x, y, 0, 0, direction );
	}
}
