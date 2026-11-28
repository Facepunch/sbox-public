using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Topten.RichTextKit;

namespace TextTests;

/// <summary>
/// A block laid out without a width keeps its layout when given a width every line fits, and only then: the layout it
/// keeps is the one laying out again would make.
/// </summary>
[TestClass]
public class MaxWidthLayoutTests
{
	static readonly Style Font = new() { FontFamily = "Arial", FontSize = 16 };

	static TextBlock Block( string text, TextAlignment alignment = TextAlignment.Auto, int? maxLines = null )
	{
		var block = new TextBlock { Alignment = alignment, MaxLines = maxLines };
		block.AddText( text, Font );
		return block;
	}

	/// <summary>
	/// Every line's runs and glyph positions, and the measured size.
	/// </summary>
	static string Layout( TextBlock block )
	{
		var parts = new List<string> { $"{block.MeasuredWidth} x {block.MeasuredHeight}, {block.Lines.Count} lines" };
		foreach ( var line in block.Lines )
		{
			parts.Add( $"line {line.YCoord} {line.Width}" );
			foreach ( var run in line.Runs )
			{
				parts.Add( $" run {run.Start}+{run.Length} at {run.XCoord} w {run.Width}" );
				for ( int i = 0; i < run.GlyphPositions.Length; i++ ) parts.Add( $"  {run.GlyphPositions[i]}" );
			}
		}

		return string.Join( "\n", parts );
	}

	/// <summary>
	/// Lays <paramref name="text"/> out unbounded, then at <paramref name="width"/>, and checks it matches a fresh block
	/// laid out at that width; returns whether the first block laid out again.
	/// </summary>
	static bool RelaidOut( string text, float width, TextAlignment alignment = TextAlignment.Auto, int? maxLines = null )
	{
		var block = Block( text, alignment, maxLines );
		_ = block.MeasuredWidth;
		var version = block.LayoutVersion;
		block.MaxWidth = width;
		var kept = Layout( block );

		var fresh = Block( text, alignment, maxLines );
		fresh.MaxWidth = width;
		Assert.AreEqual( Layout( fresh ), kept, "the same layout as laying out at that width" );
		Assert.AreEqual( fresh.MeasuredPadding, block.MeasuredPadding );
		return block.LayoutVersion != version;
	}

	[TestMethod]
	[DataRow( "100" )]
	[DataRow( "30 / 120" )]
	[DataRow( "trailing  " )]
	public void ItsOwnWidthKeepsTheLayout( string text )
	{
		var block = Block( text );
		var width = MathF.Ceiling( block.MeasuredWidth ) + 1;
		// Trailing whitespace counts against the width when lines break, so it needs room of its own
		if ( text.EndsWith( ' ' ) ) width += 20;
		Assert.IsFalse( RelaidOut( text, width ) );
	}

	[TestMethod]
	[DataRow( "two\nlines", 0 )]
	[DataRow( "two\nlines", 30 )]
	[DataRow( "a long line\nshort", 1 )]
	public void HardBreaksGiveTheSameLayoutEitherWay( string text, float extra )
	{
		// A line ended by a break carries the break's width, which the check counts though the line breaker doesn't
		RelaidOut( text, MathF.Ceiling( Block( text ).MeasuredWidth ) + 1 + extra );
	}

	[TestMethod]
	public void ANarrowerWidthWrapsAgain()
	{
		var width = MathF.Ceiling( Block( "small words" ).MeasuredWidth ) / 2;
		Assert.IsTrue( RelaidOut( "small words", width ) );
	}

	[TestMethod]
	[DataRow( TextAlignment.Center )]
	[DataRow( TextAlignment.Right )]
	[DataRow( TextAlignment.Justify )]
	public void AlignedAgainstTheWidthLaysOutAgain( TextAlignment alignment )
	{
		var width = MathF.Ceiling( Block( "words here" ).MeasuredWidth ) + 40;
		Assert.IsTrue( RelaidOut( "words here", width, alignment ) );
	}

	[TestMethod]
	public void ALineLimitLaysOutAgain()
	{
		var width = MathF.Ceiling( Block( "one\ntwo" ).MeasuredWidth ) + 1;
		Assert.IsTrue( RelaidOut( "one\ntwo", width, maxLines: 1 ) );
	}

	[TestMethod]
	public void AWidthAfterAWidthLaysOutAgain()
	{
		var block = Block( "small words" );
		block.MaxWidth = 500;
		_ = block.MeasuredWidth;
		var version = block.LayoutVersion;
		block.MaxWidth = 400;
		_ = block.MeasuredWidth;
		Assert.AreNotEqual( version, block.LayoutVersion, "only a layout made without a width is kept at a new one" );
	}
}
