namespace Sandbox.Layout;

/// <summary>
/// Text formatting adapter for a block whose entire content is inline. Unlike a leaf measure function,
/// this owns the layout of the node's descendants. Neither callback may mutate node geometry. Layout
/// can be called speculatively; block dispatch publishes its fragments only in the final layout pass.
/// All coordinates exclude the block's padding/border.
/// </summary>
internal interface IInlineContent
{
	LayoutSize Measure( float width, bool minContent );
	InlineContentLayout Layout( float width );
}

/// <summary>A shaped fragment owned by a descendant. Text indices are adapter-defined source indices.</summary>
internal readonly record struct InlineFragment( LayoutNode Owner, int TextStart, int TextLength,
	float X, float Y, float Width, float Height );

/// <summary>The final paragraph size, first baseline, and fragments in logical text order.</summary>
internal sealed record InlineContentLayout( LayoutSize Size, float Baseline, IReadOnlyList<InlineFragment> Fragments );
