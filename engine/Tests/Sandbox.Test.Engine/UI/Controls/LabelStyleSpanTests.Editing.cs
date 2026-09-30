using Sandbox.UI;
using System;
using System.Globalization;

namespace UITests.Controls;

public partial class LabelStyleSpanTests
{
	class ScaledRoot : RootPanel
	{
		internal void SetScale( float scale ) => Scale = scale;
	}

	class ImeEntry : TextEntry
	{
		internal Label Content => Label;
		internal void Ime( string name, string text = null ) => OnEvent( new PanelEvent( name ) { Value = text } );
	}

	ImeEntry CreateEntry( string text )
	{
		root.Delete( true );
		root = new ScaledRoot { PanelBounds = new Rect( 0, 0, 1400, 1000 ) };
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		var entry = root.AddChild<ImeEntry>();
		entry.Multiline = true;
		entry.Style.Set( "font-family: Arial; font-size: 16px; color: white; width: 180px; height: 65px; overflow: scroll; align-items: flex-start; justify-content: flex-start;" );
		entry.Text = text;
		label = entry.Content;
		label.Tokenize = false;
		label.Style.WhiteSpace = WhiteSpace.Pre;
		root.Layout();
		label.ShouldDrawSelection = true;
		return entry;
	}

	/// <summary>
	/// Splits styles at text-element boundaries, including around tabs and combining sequences.
	/// Compares every caret and the measured size against the same text without span styles.
	/// </summary>
	void AssertColorsPreserveGeometry( ImeEntry entry )
	{
		label.ClearStyleSpans();
		root.Layout();
		var text = entry.Text;
		var size = label._textBlock.MeasuredSize;
		var caret = entry.CaretPosition;
		var selection = (label.SelectionStart, label.SelectionEnd);
		var scroll = entry.ScrollOffset;
		var imeRect = entry.ImeCaretRect;
		var carets = Enumerable.Range( 0, label.TextLength + 1 ).Select( label.GetCaretRect ).ToArray();
		var starts = StringInfo.ParseCombiningCharacters( text );
		var red = new Styles { FontColor = Color.Red };
		var blue = new Styles { FontColor = Color.Blue };
		for ( int i = 0; i < starts.Length; i++ )
			label.SetStyleSpan( starts[i], i + 1 < starts.Length ? starts[i + 1] : text.Length, i % 2 == 0 ? red : blue );
		root.Layout();
		Assert.AreEqual( text, entry.Text );
		Assert.AreEqual( size, label._textBlock.MeasuredSize );
		Assert.AreEqual( caret, entry.CaretPosition );
		Assert.AreEqual( selection, (label.SelectionStart, label.SelectionEnd) );
		Assert.AreEqual( scroll, entry.ScrollOffset );
		Assert.AreEqual( imeRect, entry.ImeCaretRect );
		CollectionAssert.AreEqual( carets, Enumerable.Range( 0, label.TextLength + 1 ).Select( label.GetCaretRect ).ToArray() );
		Assert.AreEqual( Color.Red.ToSkF(), StyleAt( 0 ).TextColor );
	}

	[TestMethod]
	[DataRow( 1f )]
	[DataRow( 1.25f )]
	[DataRow( 2f )]
	[DataRow( 2.5f )]
	public void TabsUnicodeAndScrollingKeepTheirGeometryAtDifferentScales( float scale )
	{
		var entry = CreateEntry( "// 😀 e\u0301\tAV office " + new string( 'x', 80 ) + "\n\tsecond line\n\tthird line\n\tfourth line\nlast" );
		((ScaledRoot)root).SetScale( scale );
		root.Layout();
		AssertColorsPreserveGeometry( entry );
		entry.ScrollOffset = new Vector2( 40, 20 );
		entry.SetNeedsFinalLayout();
		root.Layout();
		Assert.IsTrue( entry.ScrollOffset.x > 0 && entry.ScrollOffset.y > 0 );
		AssertColorsPreserveGeometry( entry );

		// Also check changing scale with spans already on the label.
		((ScaledRoot)root).SetScale( scale + 0.25f );
		root.Layout();
		AssertColorsPreserveGeometry( entry );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void ImePreviewCommitAndCancellationPreserveStyledGeometry( bool commit, bool replaceSelection )
	{
		var entry = CreateEntry( "ab" );
		entry.CaretPosition = 1;
		if ( replaceSelection ) label.SetSelection( 0, 2 );
		AssertColorsPreserveGeometry( entry );
		entry.Ime( "onimestart" );
		entry.Ime( "onime", "か" );
		Assert.AreEqual( replaceSelection ? "か" : "aかb", entry.Text );
		AssertColorsPreserveGeometry( entry );
		entry.Ime( "onime", "かな" );
		Assert.AreEqual( replaceSelection ? "かな" : "aかなb", entry.Text );
		AssertColorsPreserveGeometry( entry );
		if ( commit )
		{
			entry.OnKeyTyped( 'か' );
			entry.OnKeyTyped( 'な' );
		}
		entry.Ime( "onime", "" );
		entry.Ime( "onimeend" );
		Assert.AreEqual( commit ? (replaceSelection ? "かな" : "aかなb") : "ab", entry.Text );
		AssertColorsPreserveGeometry( entry );
		if ( commit )
		{
			entry.Undo();
			Assert.AreEqual( "ab", entry.Text );
			AssertColorsPreserveGeometry( entry );
			entry.Redo();
			Assert.AreEqual( replaceSelection ? "かな" : "aかなb", entry.Text );
			AssertColorsPreserveGeometry( entry );
		}
	}
}
