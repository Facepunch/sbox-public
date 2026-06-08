using System;
using Topten.RichTextKit;

namespace UITest.Text;

/// <summary>
/// Rendering-level coverage for text-align: justify, exercised directly through RichTextKit's
/// layout so we verify words are actually spread to fill the line (not just that the style parses).
/// </summary>
[TestClass]
public class Justification
{
	const float MaxWidth = 140;

	/// <summary>
	/// Lays out a short multi-word string that wraps to several lines under the given alignment.
	/// </summary>
	static TextBlock Build( TextAlignment alignment )
	{
		var tb = new TextBlock();
		tb.MaxWidth = MaxWidth;
		tb.Alignment = alignment;

		var style = new Style { FontFamily = "Arial", FontSize = 16 };
		tb.AddText( "aa bb cc dd ee ff gg hh ii jj kk ll mm nn oo pp qq rr ss tt uu vv", style );
		tb.Layout();

		return tb;
	}

	/// <summary>
	/// The right edge of a line's visible (non-trailing-whitespace) content.
	/// </summary>
	static float LineRight( TextLine line )
	{
		float right = 0;
		foreach ( var run in line.Runs )
		{
			if ( run.RunKind != FontRunKind.Normal )
				continue;

			right = MathF.Max( right, run.XCoord + run.Width );
		}
		return right;
	}

	/// <summary>
	/// A justified line (other than the last) should be stretched to fill the available width.
	/// </summary>
	[TestMethod]
	public void JustifyFillsFirstLine()
	{
		var tb = Build( TextAlignment.Justify );

		Assert.IsTrue( tb.Lines.Count >= 2, $"expected the text to wrap, got {tb.Lines.Count} line(s)" );
		Assert.IsTrue( LineRight( tb.Lines[0] ) >= MaxWidth - 1f, $"justified line only reached {LineRight( tb.Lines[0] )} of {MaxWidth}" );
	}

	/// <summary>
	/// The same line is wider when justified than when left-aligned (proving the spacing was added,
	/// not that the text just happened to fit).
	/// </summary>
	[TestMethod]
	public void JustifyWidensVersusLeft()
	{
		var justified = Build( TextAlignment.Justify );
		var left = Build( TextAlignment.Left );

		Assert.IsTrue( LineRight( justified.Lines[0] ) > LineRight( left.Lines[0] ) + 1f,
			$"justified {LineRight( justified.Lines[0] )} vs left {LineRight( left.Lines[0] )}" );
	}

	/// <summary>
	/// The last line of the block is not justified - it stays the same width as when left-aligned.
	/// </summary>
	[TestMethod]
	public void JustifyLeavesLastLineRagged()
	{
		var justified = Build( TextAlignment.Justify );
		var left = Build( TextAlignment.Left );

		int last = justified.Lines.Count - 1;
		Assert.AreEqual( LineRight( left.Lines[last] ), LineRight( justified.Lines[last] ), 0.5f );
		Assert.IsTrue( LineRight( justified.Lines[last] ) < MaxWidth - 1f, "last line should not fill the width" );
	}
}
