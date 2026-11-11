using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using Topten.RichTextKit;

namespace TextTests;

[TestClass]
public class MinContentTests
{
	[DataTestMethod]
	[DataRow( "small extraordinary words", new[] { "extraordinary" }, false, false )]
	[DataRow( "one\u00a0two three", new[] { "one\u00a0two" }, false, false )]
	[DataRow( "extraordinary\nsmall", new[] { "extraordinary" }, false, false )]
	[DataRow( "small extraordinary words", new[] { "small extraordinary words" }, true, false )]
	[DataRow( "small words  ", new[] { "small words  " }, true, false )]
	[DataRow( "a \u05d0\u05d1\u05d2\u05d3\u05d4 words", new[] { "a", "\u05d0\u05d1\u05d2\u05d3\u05d4", "words" }, false, false )]
	[DataRow( "a   bb", new[] { "bb" }, false, true )]
	[DataRow( "bb   a", new[] { "bb " }, false, true )]
	[DataRow( "   ", new[] { " " }, false, true )]
	[DataRow( "bb  \na", new[] { "bb " }, false, true )]
	[DataRow( "bb  \naa", new[] { "bb " }, false, true )]
	[DataRow( "\n", new[] { "" }, false, false )]
	[DataRow( "", new[] { "" }, false, false )]
	public void WidthAndHeightMatchIntrinsicConstraint( string text, string[] longestCandidates, bool noWrap, bool preserveSpaces )
	{
		var block = new TextBlock { NoWrap = noWrap, MaxWidth = 20, MaxHeight = 10, Overflow = TextOverflow.Ellipsis };
		var reference = new TextBlock();
		try
		{
			var style = new Style { FontFamily = "Arial", FontSize = 16 };
			block.AddText( text, style );
			float expectedWidth = 0;
			foreach ( string candidate in longestCandidates )
			{
				reference.Clear();
				reference.AddText( candidate, style );
				expectedWidth = MathF.Max( expectedWidth, reference.MeasuredWidth );
			}
			var before = (block.MeasuredWidth, block.MeasuredHeight, block.Lines.Count, block.Truncated);
			var intrinsic = block.MeasureMinContent( preserveSpaces: preserveSpaces );
			Assert.AreEqual( expectedWidth, intrinsic.Width, 0.01f );
			Assert.AreEqual( before, (block.MeasuredWidth, block.MeasuredHeight, block.Lines.Count, block.Truncated) );

			reference.Clear();
			reference.NoWrap = noWrap;
			reference.AddText( text, style );
			reference.MaxWidth = MathF.Ceiling( intrinsic.Width ) + 1;
			Assert.AreEqual( reference.MeasuredHeight, intrinsic.Height, 0.01f );
			Assert.AreEqual( intrinsic, block.MeasureMinContent( preserveSpaces: preserveSpaces ) );
		}
		finally
		{
			block.Clear();
			reference.Clear();
		}
	}

	[TestMethod]
	public void HeightUsesAvailableWidthAndReservesTrailingLine()
	{
		var block = new TextBlock();
		try
		{
			block.AddText( "words words words\n", new Style { FontFamily = "Arial", FontSize = 16 } );
			var intrinsic = block.MeasureMinContent( reserveTrailingLine: true );
			var wide = block.MeasureMinContent( 200, reserveTrailingLine: true );
			Assert.IsTrue( intrinsic.Height > wide.Height );
			Assert.AreEqual( 200f, wide.Width );
			block.MaxWidth = MathF.Ceiling( intrinsic.Width ) + 1;
			Assert.AreEqual( block.MeasuredHeight + block.Lines[^1].Height, intrinsic.Height, 0.01f );
			Assert.AreEqual( block.MeasuredHeight, block.MeasureMinContent().Height, 0.01f );
			block.MaxWidth = null;
			Assert.AreEqual( block.MeasuredHeight + block.Lines[^1].Height,
				block.MeasureMinContent( float.NaN, reserveTrailingLine: true ).Height, 0.01f );
		}
		finally
		{
			block.Clear();
		}
	}

	[TestMethod]
	public void StyledWordAndCharacterBreaksUseShaping()
	{
		var block = new TextBlock();
		try
		{
			var style = new Style { FontFamily = "Arial", FontSize = 16 };
			block.AddText( "extra", style );
			var bold = style.Copy();
			bold.FontWeight = 700;
			block.AddText( "ordinary", bold );
			var wordWidth = block.MeasuredWidth;
			block.AddText( " words", style );
			Assert.AreEqual( wordWidth, block.MeasureMinContent().Width, 0.01f );
			block.Clear();
			block.AddText( "W", style );
			var characterWidth = block.MeasuredWidth;
			block.AddText( "WWWW", style );
			block.WordBreak = WordBreakMode.Character;
			Assert.AreEqual( characterWidth, block.MeasureMinContent().Width, 0.01f );
		}
		finally
		{
			block.Clear();
		}
	}
}
