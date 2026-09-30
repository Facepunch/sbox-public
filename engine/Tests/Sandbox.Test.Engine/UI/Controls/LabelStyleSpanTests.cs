using Sandbox.UI;
using System;
using System.Reflection;

namespace UITests.Controls;

[TestClass, DoNotParallelize]
public partial class LabelStyleSpanTests
{
	RootPanel root;
	Label label;
	bool renderText;

	[TestInitialize]
	public void Initialize()
	{
		renderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
		root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		label = root.AddChild<Label>();
		label.Style.Set( "font-family: Arial; font-size: 16px; color: white; white-space: pre;" );
		label.Tokenize = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		root.Delete( true );
		TextBlock.ui_rendertext = renderText;
	}

	Topten.RichTextKit.TextBlock Block => (Topten.RichTextKit.TextBlock)typeof( TextBlock )
		.GetField( "Block", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( label._textBlock );

	Topten.RichTextKit.IStyle StyleAt( int offset )
	{
		var index = Block.CharacterToCodePointIndex( offset );
		return Block.StyleRuns.First( run => index >= run.Start && index < run.End ).Style;
	}

	[TestMethod]
	public void SpansKeepTextSelectionAndCaretGeometry()
	{
		label.Text = "office AV 😀 e\u0301\nnext line";
		root.Layout();
		label.SetSelection( 1, 5 );
		var carets = Enumerable.Range( 0, label.TextLength + 1 ).Select( label.GetCaretRect ).ToArray();
		var originalText = label.Text;
		var originalSize = label._textBlock.MeasuredSize;
		var red = new Styles { FontColor = Color.Red };
		label.SetStyleSpan( 1, 3, red );
		label.SetStyleSpan( 7, 9, red );
		root.Layout();
		Assert.AreEqual( originalText, label.Text );
		Assert.AreEqual( 1, label.SelectionStart );
		Assert.AreEqual( 5, label.SelectionEnd );
		Assert.AreEqual( originalSize, label._textBlock.MeasuredSize );
		CollectionAssert.AreEqual( carets, Enumerable.Range( 0, label.TextLength + 1 ).Select( label.GetCaretRect ).ToArray() );
		Assert.AreEqual( Color.Red.ToSkF(), StyleAt( 1 ).TextColor );
		Assert.AreEqual( Color.White.ToSkF(), StyleAt( 3 ).TextColor );
		Assert.AreSame( StyleAt( 1 ), StyleAt( 7 ), "Repeated styles should share one resolved style." );
	}

	[TestMethod]
	public void ClearAndTextReplacementRemoveStaleStyles()
	{
		label.Text = "hello";
		var red = new Styles { FontColor = Color.Red };
		label.SetStyleSpan( 0, 5, red );
		root.Layout();
		Assert.AreEqual( Color.Red.ToSkF(), StyleAt( 0 ).TextColor );
		label.ClearStyleSpans();
		root.Layout();
		Assert.AreEqual( Color.White.ToSkF(), StyleAt( 0 ).TextColor );
		label.SetStyleSpan( 0, 5, red );
		root.Layout();
		label.Text = "new";
		root.Layout();
		Assert.AreEqual( Color.White.ToSkF(), StyleAt( 0 ).TextColor );
	}

	[TestMethod]
	public void SpansHandleSurrogatesAndClipAtTheEnd()
	{
		label.Text = "😀abc";
		label.SetStyleSpan( 0, 2, new Styles { FontColor = Color.Red } );
		label.SetStyleSpan( 2, 100, new Styles { FontColor = Color.Blue } );
		root.Layout();
		Assert.AreEqual( Color.Red.ToSkF(), StyleAt( 0 ).TextColor );
		Assert.AreEqual( Color.Blue.ToSkF(), StyleAt( 2 ).TextColor );
		Assert.AreEqual( 4, Block.Length );
	}

	[TestMethod]
	public void ColorOnlyStyleInheritsFontAndCanBeReapplied()
	{
		label.Text = "word";
		label.Style.FontWeight = 700;
		label.Style.FontStyle = FontStyle.Italic;
		var span = new Styles { FontColor = Color.Red };
		label.SetStyleSpan( 0, 4, span );
		root.Layout();
		Assert.AreEqual( 700, StyleAt( 0 ).FontWeight );
		Assert.IsTrue( StyleAt( 0 ).FontItalic );
		label.ClearStyleSpans();
		span.FontColor = Color.Blue;
		span.FontWeight = 400;
		label.SetStyleSpan( 0, 4, span );
		root.Layout();
		Assert.AreEqual( 400, StyleAt( 0 ).FontWeight );
		Assert.AreEqual( Color.Blue.ToSkF(), StyleAt( 0 ).TextColor );
	}

	[TestMethod]
	public void OffsetsReferToNormalizedDisplayText()
	{
		label.Style.WhiteSpace = WhiteSpace.Normal;
		label.Text = "  first   second  ";
		label.SetStyleSpan( 6, 12, new Styles { FontColor = Color.Red } );
		root.Layout();
		Assert.AreEqual( Color.White.ToSkF(), StyleAt( 0 ).TextColor );
		Assert.AreEqual( Color.Red.ToSkF(), StyleAt( 6 ).TextColor );
		Assert.AreEqual( "  first   second  ", label.Text );
	}

	[TestMethod]
	public void RejectsOverlappingAndReversedRanges()
	{
		var style = new Styles();
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => label.SetStyleSpan( -1, 2, style ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => label.SetStyleSpan( 4, 2, style ) );
		Assert.ThrowsException<ArgumentNullException>( () => label.SetStyleSpan( 0, 2, null ) );
		label.SetStyleSpan( 0, 2, style );
		Assert.ThrowsException<ArgumentException>( () => label.SetStyleSpan( 1, 3, style ) );
		label.SetStyleSpan( 2, 4, style );
	}

	[TestMethod]
	public void FillingAWarmedSpanBufferDoesNotAllocate()
	{
		var style = new Styles { FontColor = Color.Red };
		void Fill()
		{
			label.ClearStyleSpans();
			for ( int i = 0; i < 1000; i++ ) label.SetStyleSpan( i * 2, i * 2 + 1, style );
		}
		for ( int i = 0; i < 10; i++ ) Fill();
		var before = GC.GetAllocatedBytesForCurrentThread();
		Fill();
		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
	}
}
