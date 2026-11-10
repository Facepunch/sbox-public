using Topten.RichTextKit;

namespace Sandbox.UI;

/// <summary>
/// Builds styled text using the same layout that handles measurement, drawing and caret positions.
/// </summary>
internal sealed partial class TextBlock
{
	/// <summary>
	/// Style overrides for a range of displayed text, after whitespace handling and text transformations.
	/// Start is inclusive and End is exclusive, measured in UTF-16 units. Holds a reference to the supplied style.
	/// </summary>
	internal readonly record struct StyleSpan( int Start, int End, Styles Style );

	/// <summary>
	/// Ordered, non-overlapping spans owned by the Label and shared here for layout.
	/// </summary>
	internal List<StyleSpan> StyleSpans;

	/// <summary>
	/// Converts font sizes and spacing supplied by Label from panel units to screen pixels.
	/// </summary>
	internal float StyleSpanScale = 1;

	/// <summary>
	/// Shares one resolved text style between spans using the same Styles object during a rebuild.
	/// Entries are cleared after building; the dictionary's capacity is reused.
	/// </summary>
	Dictionary<Styles, Style> spanStyles;

	/// <summary>
	/// Adds the already-normalized text to RichTextKit in one pass, using span styles and the normal style for gaps.
	/// Clamps ranges to the text and avoids splitting surrogate pairs. Text slices avoid allocating a string per span.
	/// RichTextKit then shapes these runs together where possible and handles their layout and drawing.
	/// </summary>
	void AddStyledText( string text )
	{
		if ( StyleSpans is not { Count: > 0 } )
		{
			Block.AddText( text, Style );
			return;
		}

		spanStyles ??= new();
		spanStyles.Clear();
		int position = 0;
		foreach ( var span in StyleSpans )
		{
			int start = Math.Min( span.Start, text.Length );
			int end = Math.Min( span.End, text.Length );
			// A UTF-16 boundary within a surrogate pair belongs to that whole code point.
			if ( start > 0 && start < text.Length && char.IsLowSurrogate( text[start] ) && char.IsHighSurrogate( text[start - 1] ) ) start--;
			if ( end > 0 && end < text.Length && char.IsLowSurrogate( text[end] ) && char.IsHighSurrogate( text[end - 1] ) ) end--;
			Block.AddText( text.AsSpan( position, start - position ), Style );
			if ( end > start )
			{
				if ( !spanStyles.TryGetValue( span.Style, out var resolved ) )
				{
					resolved = ResolveSpanStyle( span.Style, StyleSpanScale );
					spanStyles.Add( span.Style, resolved );
				}
				Block.AddText( text.AsSpan( start, end - start ), resolved );
			}
			position = end;
		}
		Block.AddText( text.AsSpan( position ), Style );
		spanStyles.Clear();
	}

	/// <summary>
	/// Copies the block's normal text style and applies the supplied font, color, spacing and decoration overrides.
	/// Unspecified settings are inherited. Used by both plain-text spans and HTML spans.
	/// </summary>
	/// <param name="source">CSS style settings to apply; this object is not modified.</param>
	/// <param name="scale">Panel-to-screen scale, or 1 for HTML styles that are already in screen units.</param>
	Style ResolveSpanStyle( Styles source, float scale )
	{
		var result = Style.Copy();
		if ( source.FontSize is { } size ) result.FontSize = MathF.Round( size.GetPixels( 100 ) * scale * 32 ) / 32;
		result.FontFamily = source.FontFamily ?? result.FontFamily;
		result.FontWeight = source.FontWeight ?? result.FontWeight;
		if ( source.FontStyle is { } italic ) result.FontItalic = italic == UI.FontStyle.Italic;
		result.FontVariantNumeric = source.FontVariantNumeric ?? result.FontVariantNumeric;
		result.TextColor = source.FontColor?.ToSkF() ?? result.TextColor;
		result.BackgroundColor = source.BackgroundColor?.ToSkF() ?? result.BackgroundColor;
		if ( source.LetterSpacing is { } letters ) result.LetterSpacing = letters.GetPixels( 1000 ) * scale;
		if ( source.WordSpacing is { } words ) result.WordSpacing = words.GetPixels( 1000 ) * scale;
		if ( source.TextDecorationLine is { } decoration )
		{
			result.Underline = decoration.Contains( UI.TextDecoration.Underline ) ? UnderlineStyle.Solid : UnderlineStyle.None;
			result.StrikeThrough = decoration.Contains( UI.TextDecoration.LineThrough ) ? StrikeThroughStyle.Solid : StrikeThroughStyle.None;
		}
		result.UnderlineColor = source.TextDecorationColor?.ToSkF() ?? result.TextColor;
		return result;
	}
}
