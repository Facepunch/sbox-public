namespace Sandbox.UI;

public partial class Label
{
	/// <summary>
	/// Ordered spans. Clearing retains the list's capacity for the next update.
	/// </summary>
	List<TextBlock.StyleSpan> styleSpans;

	/// <summary>
	/// Styles a range of displayed plain text. Start is inclusive and end is exclusive, in UTF-16 units
	/// after whitespace normalization and text-transform. Supply spans in order without overlaps.
	/// Supported properties match rich text: font, color, background, spacing and text decorations.
	/// Reuse styles between spans; after changing a style, clear and resubmit the spans.
	/// Spans are cleared when Text changes, and are not used for HTML or inline paragraphs.
	/// </summary>
	public void SetStyleSpan( int start, int end, Styles style )
	{
		ArgumentNullException.ThrowIfNull( style );
		if ( start < 0 || end < start ) throw new ArgumentOutOfRangeException( nameof( start ) );
		if ( start == end ) return;
		if ( styleSpans is { Count: > 0 } && start < styleSpans[^1].End )
			throw new ArgumentException( "Style spans must be ordered and must not overlap.", nameof( start ) );

		styleSpans ??= new();
		styleSpans.Add( new( start, end, style ) );
		_textBlock?.Dirty();
		SetNeedsPreLayout();
	}

	/// <summary>
	/// Removes all style spans, retaining the buffer for reuse. Does not change text or selection.
	/// </summary>
	public void ClearStyleSpans()
	{
		if ( styleSpans is not { Count: > 0 } ) return;
		styleSpans.Clear();
		_textBlock?.Dirty();
		SetNeedsPreLayout();
	}
}
