using Sandbox.Layout;
using Topten.RichTextKit;

namespace Sandbox.UI;

internal sealed partial class TextBlock
{
	internal bool IsInlineParagraph;
	private float? _inlineWidth;
	private float? _inlineLayoutWidth;
	private InlineContentLayout _inlineLayout;
	internal float InlineFinalWidth { get; private set; }
	internal void FinalizeInlineWidth( float width )
	{
		if ( InlineFinalWidth != width ) ReleaseTexture();
		InlineFinalWidth = width;
	}
	internal Topten.RichTextKit.Style InlineStyle => Style.Copy();
	internal int InlineStyleHash => FontHash;
	internal void InvalidateInlineSelection() => ReleaseTexture();
	internal int InlineCaretCount => Block.CaretIndicies.Count - 1;
	internal string GetInlineSelectedText( int start, int end )
	{
		// Hit testing and painting both use shaped carets, not StringInfo graphemes (e.g. ffi).
		var from = Block.CodePointToCharacterIndex( CaretToCodePointIndex( start ) );
		var to = Block.CodePointToCharacterIndex( CaretToCodePointIndex( end ) );
		return Text[from..to];
	}

	internal void SetInlineRuns( Styles style, IReadOnlyList<InlineParagraph.Run> runs )
	{
		_inlineLayout = null;
		IsInlineParagraph = true;
		SetText( string.Concat( runs.Select( r => r.Text ) ) );
		Dirty();
		UpdateStyles( style );
		Block.Clear();
		// Whitespace and source mapping have already been resolved across owners.
		Block.Alignment = TextAlignment.Left;
		Block.Overflow = Topten.RichTextKit.TextOverflow.None;
		TextOverflow = UI.TextOverflow.None;
		Block.NoWrap = style.WhiteSpace == UI.WhiteSpace.NoWrap;
		// Owner boundaries must not split shaping when the actual text style is identical (kerning and
		// ligatures can cross spans). Source/owner ranges remain separate from these shaping runs.
		for ( int i = 0; i < runs.Count; )
		{
			var first = runs[i++];
			var text = new System.Text.StringBuilder( first.Text );
			while ( i < runs.Count && InlineStyleKey( runs[i].Style ).Equals( InlineStyleKey( first.Style ) ) )
				text.Append( runs[i++].Text );
			Block.AddText( text.ToString(), first.Style );
		}
		IsHdr |= runs.Any( r => r.Owner.ComputedStyle.FontColor?.IsHdr == true
			|| r.Owner.ComputedStyle.TextDecorationColor?.IsHdr == true );
	}

	private static (string, float, int, bool, FontVariantNumeric, SkiaSharp.SKColorF, UnderlineStyle,
		SkiaSharp.SKColorF?, StrikeThroughStyle, float, float, float, bool, float?, UnderlineType, float, float, float)
		InlineStyleKey( Topten.RichTextKit.Style style ) =>
		(style.FontFamily, style.FontSize, style.FontWeight, style.FontItalic, style.FontVariantNumeric,
		style.TextColor, style.Underline, style.UnderlineColor, style.StrikeThrough, style.LineHeight,
		style.LetterSpacing, style.WordSpacing, style.StrokeInkSkip, style.StrokeThickness, style.UnderlineStrokeType,
		style.UnderlineOffset, style.OverlineOffset, style.StrikeThroughOffset);

	internal LayoutSize MeasureInline( float width )
	{
		_inlineWidth = float.IsFinite( width ) ? MathF.Max( 0, width ) : null;
		Block.MaxWidth = _inlineWidth;
		Block.MaxHeight = null;
		return new LayoutSize( Block.MeasuredWidth, Block.MeasuredHeight );
	}

	internal InlineContentLayout LayoutInline( float width, IReadOnlyList<InlineParagraph.Run> runs )
	{
		var size = MeasureInline( width );
		if ( _inlineLayout is not null && _inlineLayoutWidth == _inlineWidth ) return _inlineLayout;
		var fragments = new List<InlineFragment>();
		foreach ( var line in Block.Lines )
		{
			foreach ( var fontRun in line.Runs )
			{
				if ( fontRun.RunKind == FontRunKind.TrailingWhitespace ) continue;
				foreach ( var run in runs )
				{
					var start = Math.Max( fontRun.Start, run.Start );
					var end = Math.Min( fontRun.End, run.Start + run.Sources.Count );
					if ( end <= start ) continue;
					var a = fontRun.GetXCoordOfCodePointIndex( start );
					var b = fontRun.GetXCoordOfCodePointIndex( end );
					var sourceStart = run.Sources[start - run.Start].Start;
					var sourceEnd = run.Sources[end - run.Start - 1].End;
					fragments.Add( new InlineFragment( run.Owner.LayoutTree.Node, sourceStart, sourceEnd - sourceStart,
						MathF.Min( a, b ), line.YCoord, MathF.Abs( b - a ), line.Height ) );
				}
			}
		}
		_inlineLayoutWidth = _inlineWidth;
		return _inlineLayout = new InlineContentLayout( size,
			Block.Lines.Count == 0 ? 0 : Block.Lines[0].BaseLine, fragments );
	}
}
