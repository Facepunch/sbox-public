using System.Linq;
using Sandbox.Engine;
using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class InlineParagraphTests
{
	private bool _renderText;
	[TestInitialize]
	public void Initialize()
	{
		_renderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
		TextBlock.ui_rendertext = _renderText;
	}

	private static RootPanel Root() => new() { PanelBounds = new Rect( 0, 0, 600, 600 ) };
	private static Panel Paragraph( Panel root, int width = 180 )
	{
		var p = root.AddChild<Panel>();
		p.Style.Set( $"display: block; align-self: flex-start; width: {width}px; font-family: Arial; font-size: 20px;" );
		p.AllowChildSelection = true;
		return p;
	}
	private static Label Text( Panel parent, string text )
	{
		var label = parent.AddChild<Label>();
		label.Style.Set( "display: inline;" );
		label.Text = text;
		return label;
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void DeletionBetweenPreLayoutAndMeasureDropsDisposedOwners( bool deferred, bool nested )
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "before " );
		var span = nested ? p.AddChild<Panel>() : p;
		if ( nested ) span.Style.Display = DisplayMode.Inline;
		var deleted = Text( span, "removed " );
		var tail = Text( p, "after" );
		root.Layout();
		p.SelectAllInChildren();
		var oldLayout = p.InlineParagraph.Layout( 180 );
		var removedNode = deleted.LayoutTree.Node;
		var target = nested ? span : deleted;
		if ( deferred ) target.Delete();
		root.PreLayout();
		// The live UI loop completes deferred deletion after PreLayout, unlike RootPanel.Layout().
		if ( deferred ) GlobalContext.Current.UISystem.RunDeferredDeletion( true );
		else target.Delete( true );
		Assert.IsNull( deleted.LayoutTree );
		root.CalculateLayout();
		root.PostLayout();
		Assert.AreEqual( "before after", p.InlineParagraph.Text.Text );
		Assert.IsNull( deleted.InlineOwner );
		Assert.IsFalse( p.InlineParagraph.Text.ShouldDrawSelection );
		var layout = p.InlineParagraph.Layout( 180 );
		Assert.AreNotSame( oldLayout, layout );
		Assert.IsFalse( layout.Fragments.Any( f => f.Owner == removedNode ) );
		Assert.IsTrue( tail.LayoutTree.Node.InlineFragments.Count > 0 );
		p.SelectAllInChildren();
		Assert.AreEqual( "before after", p.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void DeletingLastSpanBetweenPassesLeavesAnEmptyParagraphUntilPreLayout()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "last" );
		root.Layout();
		text.Delete();
		root.PreLayout();
		GlobalContext.Current.UISystem.RunDeferredDeletion( true );
		root.CalculateLayout();
		root.PostLayout();
		Assert.AreEqual( "", p.InlineParagraph.Text.Text );
		Assert.AreEqual( 0, p.InlineParagraph.Layout( 180 ).Fragments.Count );
		p.InlineParagraph.Draw();
		root.Layout();
		Assert.IsNull( p.InlineParagraph );
		Assert.IsNull( p.LayoutTree.Node.InlineContent );
	}

	private sealed class ReplacingParagraph : Panel
	{
		internal System.Action ReplaceChild;

		protected override void OnChildRemoved( Panel child )
		{
			var replace = ReplaceChild;
			ReplaceChild = null;
			replace?.Invoke();
		}
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void ReplacementDuringRemovalWaitsForPreLayout( bool nested, bool measureFirst )
	{
		var root = Root();
		var p = root.AddChild<ReplacingParagraph>();
		p.Style.Set( "display: block; width: 180px; font-family: Arial; font-size: 20px;" );
		Text( p, "before " );
		var removed = Text( p, "removed" );
		root.Layout();
		Label replacement = null;
		p.ReplaceChild = () =>
		{
			Panel parent = p;
			if ( nested )
			{
				parent = p.AddChild<Panel>();
				parent.Style.Display = DisplayMode.Inline;
			}
			replacement = Text( parent, "replacement" );
		};
		removed.Delete();
		root.PreLayout();
		GlobalContext.Current.UISystem.RunDeferredDeletion( true );
		Assert.IsNotNull( replacement );
		Assert.IsNull( replacement._textBlock );
		if ( measureFirst ) p.InlineParagraph.Measure( float.NaN, false );
		root.CalculateLayout();
		Assert.AreEqual( "before", p.InlineParagraph.Text.Text );
		Assert.IsNull( replacement.InlineOwner );
		Assert.IsFalse( p.InlineParagraph.Layout( 180 ).Fragments.Any( f => f.Owner == replacement.LayoutTree.Node ) );
		root.PostLayout();
		root.Layout();
		Assert.AreEqual( "before replacement", p.InlineParagraph.Text.Text );
		Assert.AreSame( p.InlineParagraph, replacement.InlineOwner );
		Assert.IsTrue( replacement.LayoutTree.Node.InlineFragments.Count > 0 );
	}

	[TestMethod]
	public void ReparentingAfterPreLayoutInvalidatesTheOldParagraphBeforeMeasure()
	{
		var root = Root();
		var source = Paragraph( root );
		Text( source, "stay " );
		var moved = Text( source, "move" );
		var destination = Paragraph( root );
		Text( destination, "new " );
		root.Layout();
		root.PreLayout();
		moved.Parent = destination;
		root.CalculateLayout();
		Assert.AreEqual( "stay", source.InlineParagraph.Text.Text );
		Assert.IsFalse( source.InlineParagraph.Layout( 180 ).Fragments.Any( f => f.Owner == moved.LayoutTree.Node ) );
		root.Layout();
		Assert.AreEqual( "new move", destination.InlineParagraph.Text.Text );
		Assert.AreSame( destination.InlineParagraph, moved.InlineOwner );
	}

	[TestMethod]
	public void UnchangedFormattingMeasurementAndPaintDoNotAllocate()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "before " );
		var span = p.AddChild<Panel>();
		span.Style.Display = DisplayMode.Inline;
		Text( span, "several words wrapping across multiple lines" );
		root.Layout();
		root.Layout();
		var paragraph = p.InlineParagraph;
		var layout = paragraph.Layout( 180 );
		for ( int i = 0; i < 100; i++ )
		{
			InlineParagraph.CanFormat( p );
			paragraph.Update();
			paragraph.Measure( 180, false );
			paragraph.FinalizeLayout();
			paragraph.Draw();
		}
		p.IsRenderDirty = false;
		var before = System.GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
		{
			InlineParagraph.CanFormat( p );
			paragraph.Update();
			paragraph.Measure( 180, false );
			paragraph.FinalizeLayout();
			paragraph.Draw();
		}
		var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( 0L, allocated, "Unchanged paragraph passes should reuse collections and shaping" );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		Assert.IsFalse( p.IsRenderDirty );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ScrollingReusesGeometryAndOnlyInvalidatesMovedDescriptors( bool scrollParagraph )
	{
		var root = Root();
		var host = root.AddChild<Panel>();
		host.Style.Set( "display: block; width: 200px; height: 60px; overflow: scroll;" );
		var p = Paragraph( host );
		if ( scrollParagraph ) p.Style.Set( "height: 60px; overflow: scroll;" );
		var text = Text( p, "several words wrapping across many lines to fill the scrolling paragraph with text" );
		root.Layout();
		root.Layout();
		var paragraph = p.InlineParagraph;
		var layout = paragraph.Layout( 180 );
		var fragments = text.LayoutTree.Node.InlineFragments;
		var origin = paragraph.Origin;
		paragraph.SetSelection( 0, 7 );
		var selection = paragraph.SelectedText;
		var scroller = scrollParagraph ? p : host;
		scroller.ScrollOffset = new Vector2( 0, 20 );
		scroller.SetNeedsFinalLayout();
		p.IsRenderDirty = false;
		root.PostLayout();
		Assert.AreEqual( origin - new Vector2( 0, 20 ), paragraph.Origin );
		Assert.IsTrue( p.IsRenderDirty, "Moved text descriptors must be rebuilt" );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		Assert.AreSame( fragments, text.LayoutTree.Node.InlineFragments );
		paragraph.Draw();
		Assert.AreEqual( selection, paragraph.SelectedText );
		p.IsRenderDirty = false;
		paragraph.FinalizeLayout();
		paragraph.Draw();
		Assert.IsFalse( p.IsRenderDirty, "An unchanged final pass must not dirty painting" );
	}

	[TestMethod]
	public void GeometryCacheInvalidatesForContentStylesAndWidthButNotSelectionOrIntrinsicMeasure()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "several words wrapping across multiple lines" );
		root.Layout();
		root.Layout();
		var paragraph = p.InlineParagraph;
		var layout = paragraph.Layout( 180 );
		paragraph.Measure( float.NaN, false );
		paragraph.Measure( 0, true );
		paragraph.Draw();
		Assert.AreSame( layout, paragraph.Layout( 180 ), "Intrinsic measurement must not replace final fragments" );
		Assert.AreEqual( layout.Size.Height, paragraph.Text.MeasuredSize.y );
		paragraph.SetSelection( 0, 7 );
		p.IsRenderDirty = false;
		paragraph.SetSelection( 0, 7 );
		Assert.IsFalse( p.IsRenderDirty, "Repeated selection must not invalidate the text texture" );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		paragraph.SetSelection( 0, 8 );
		Assert.IsTrue( p.IsRenderDirty );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		text.Text += " more words";
		root.Layout();
		var changed = paragraph.Layout( 180 );
		Assert.AreNotSame( layout, changed );
		text.Style.FontColor = Color.Red;
		root.Layout();
		layout = paragraph.Layout( 180 );
		Assert.AreNotSame( changed, layout );
		text.Style.FontSize = 30;
		root.Layout();
		changed = paragraph.Layout( 180 );
		Assert.AreNotSame( layout, changed );
		p.Style.Width = 400;
		root.Layout();
		layout = paragraph.Layout( 400 );
		Assert.AreNotSame( changed, layout );
		Assert.IsTrue( layout.Size.Height < changed.Size.Height );
	}

	[TestMethod]
	public void ParentPaintOnlyChangesPreserveShapingAndSelection()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "selected paragraph" );
		root.Layout();
		root.Layout();
		var paragraph = p.InlineParagraph;
		var layout = paragraph.Layout( 180 );
		p.SelectAllInChildren();
		p.Style.Opacity = 0.5f;
		p.Style.BackgroundColor = Color.Red;
		root.Layout();
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		Assert.AreEqual( "selected paragraph", paragraph.SelectedText );
		Assert.IsTrue( p.IsRenderDirty );
	}

	[TestMethod]
	public void InheritedBlendChangesDirtyDescriptorsWithoutReshapingOrClearingSelection()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "selected paragraph" );
		root.Layout();
		root.Layout();
		var paragraph = p.InlineParagraph;
		var layout = paragraph.Layout( 180 );
		p.SelectAllInChildren();
		p.IsRenderDirty = false;
		root.Style.MixBlendMode = "multiply";
		root.Layout();
		Assert.AreEqual( "multiply", p.ComputedStyle.MixBlendMode );
		Assert.IsTrue( p.IsRenderDirty, "Inherited blend changes must invalidate the paragraph's cached descriptors" );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		Assert.AreEqual( "selected paragraph", paragraph.SelectedText );
		p.IsRenderDirty = false;
		paragraph.Update();
		paragraph.FinalizeLayout();
		Assert.IsFalse( p.IsRenderDirty, "Unchanged blend state must not dirty painting" );
	}

	[TestMethod]
	public void SharedWrappingNestedFragmentsAndResize()
	{
		var root = Root();
		var p = Paragraph( root );
		var prefix = Text( p, "Before " );
		var link = p.AddChild<Panel>();
		link.Style.Set( "display: inline; color: red; cursor: pointer;" );
		var nested = link.AddChild<Panel>();
		nested.Style.Set( "display: inline; font-weight: 700;" );
		var text = Text( nested, "a long interactive link that wraps across several lines" );
		root.Layout();
		Assert.IsNotNull( p.InlineParagraph );
		Assert.AreSame( p.InlineParagraph, text.InlineOwner );
		var fragments = link.LayoutTree.Node.InlineFragments;
		Assert.IsTrue( fragments.Select( f => f.Y ).Distinct().Count() > 1 );
		Assert.IsTrue( fragments[0].X > 0, "The link shares the prefix's line" );
		Assert.IsTrue( link.IsInside( p.InlineParagraph.Origin + new Vector2( fragments[0].X + 1, fragments[0].Y + 1 ) ) );
		Assert.IsFalse( link.IsInside( p.InlineParagraph.Origin + new Vector2( 1, fragments[0].Y + 1 ) ), "Union-box gap must not hit the link" );
		Assert.AreEqual( fragments.Count, nested.LayoutTree.Node.InlineFragments.Count );
		Assert.IsTrue( prefix.LayoutTree.IsMeasureDefined );
		var clicks = 0;
		link.AddEventListener( "onclick", () => clicks++ );
		text.DispatchEventImmediate( new MousePanelEvent( "onclick", text, "mouseleft" ) );
		Assert.AreEqual( 1, clicks, "The real link owner receives bubbled clicks" );

		var oldHeight = p.Box.Rect.Height;
		p.Style.Width = 500;
		root.Layout();
		Assert.IsTrue( p.Box.Rect.Height < oldHeight );
		text.Text = "short";
		root.Layout();
		Assert.AreEqual( "Before short", p.InlineParagraph.Text.Text );
		var oldWidth = text.Box.Rect.Width;
		nested.Style.FontSize = 40;
		root.Layout();
		Assert.IsTrue( text.Box.Rect.Width > oldWidth );
	}

	[TestMethod]
	public void WhitespaceAndCopyUseOneLogicalParagraph()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "  hello \t" );
		var span = p.AddChild<Panel>();
		span.Style.Display = DisplayMode.Inline;
		Text( span, "\n  world" );
		Text( p, "\u00a0!  " );
		root.Layout();
		Assert.AreEqual( "hello world\u00a0!", p.InlineParagraph.Text.Text );
		p.SelectAllInChildren();
		Assert.AreEqual( "hello world\u00a0!", p.GetClipboardValue( false ) );
		p.UnselectAllInChildren();
		Assert.IsNull( p.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void RemovingInlineContextRestoresLegacyLabels()
	{
		var root = Root();
		var p = Paragraph( root );
		var label = Text( p, "hello world" );
		root.Layout();
		Assert.IsNotNull( label.InlineOwner );
		p.Style.Display = DisplayMode.Flex;
		root.Layout();
		Assert.IsNull( p.InlineParagraph );
		Assert.IsNull( label.InlineOwner );
		Assert.IsTrue( label.LayoutTree.IsMeasureDefined );
		Assert.IsTrue( label.Box.Rect.Width > 0 );
	}

	[TestMethod]
	public void RazorGeneratedTextParticipatesWithoutChangingDefaultDisplay()
	{
		var root = Root();
		var p = Paragraph( root );
		var builder = new PanelRenderTreeBuilder( p );
		builder.Start();
		builder.OpenElement( 0, "span" );
		builder.AddAttributeString( 1, "style", "display: inline;" );
		builder.AddContent( 2, "one " );
		builder.CloseElement();
		builder.OpenElement( 3, "span" );
		builder.AddAttributeString( 4, "style", "display: inline;" );
		builder.AddContent( 5, " two" );
		builder.CloseElement();
		builder.Finish();
		root.Layout();
		Assert.AreEqual( "one two", p.InlineParagraph.Text.Text );
		var label = p.Children.First().Children.OfType<Label>().Single();
		Assert.IsTrue( label.IsGeneratedText );
		Assert.AreEqual( DisplayMode.Flex, label.ComputedStyle.Display );
	}

	[TestMethod]
	public void SplitWordHasNoInsertedSpaceAndStyleInvalidates()
	{
		var root = Root();
		var p = Paragraph( root, 400 );
		Text( p, "inter" );
		var last = Text( p, "active" );
		root.Layout();
		Assert.AreEqual( "interactive", p.InlineParagraph.Text.Text );
		var oldHash = last._textBlock.InlineStyleHash;
		last.Style.FontColor = Color.Red;
		root.Layout();
		Assert.AreNotEqual( oldHash, last._textBlock.InlineStyleHash );
		Assert.IsTrue( p.IsRenderDirty );
		Assert.AreEqual( "interactive", p.InlineParagraph.Text.Text );
	}

	[TestMethod]
	public void EqualStylesShapeAcrossOwnerBoundaries()
	{
		var root = Root();
		var split = Paragraph( root, 250 );
		Text( split, "A" );
		Text( split, "V office" );
		var whole = Paragraph( root, 250 );
		Text( whole, "AV office" );
		root.Layout();
		Assert.AreEqual( whole.InlineParagraph.Text.MeasuredSize.x, split.InlineParagraph.Text.MeasuredSize.x, 0.001f );
	}

	[TestMethod]
	public void HiddenAndReparentedTextDoesNotKeepOldOwnership()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "first" );
		var other = Text( p, " second" );
		var destination = Paragraph( root );
		Text( destination, "new " );
		root.Layout();
		other.Style.Display = DisplayMode.None;
		root.Layout();
		Assert.AreEqual( "first", p.InlineParagraph.Text.Text );
		Assert.IsNull( other.InlineOwner );
		Assert.AreEqual( 0, other.LayoutTree.Node.InlineFragments.Count );
		text.Parent = destination;
		root.Layout();
		Assert.AreSame( destination.InlineParagraph, text.InlineOwner );
		Assert.AreEqual( "new first", destination.InlineParagraph.Text.Text );
		text.Text = "";
		root.Layout();
		Assert.AreEqual( "new", destination.InlineParagraph.Text.Text );
	}

	[TestMethod]
	public void DragSelectionCrossesSpanBoundaryWithoutCopySeparators()
	{
		var root = Root();
		var p = Paragraph( root, 400 );
		Text( p, "hello " );
		var last = Text( p, "world" );
		root.Layout();
		var origin = p.InlineParagraph.Origin;
		last.DispatchEventImmediate( new SelectionEvent( "ondragselect", last )
		{
			StartPoint = origin,
			EndPoint = origin + new Vector2( 350, 1 )
		} );
		Assert.AreEqual( "hello world", last.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void EmptyParagraphDoesNotCreatePlaceholderGlyphs()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, " \t\n" );
		root.Layout();
		Assert.AreEqual( "", p.InlineParagraph.Text.Text );
		Assert.AreEqual( 0, text.LayoutTree.Node.InlineFragments.Count );
		text.Text = "hello";
		root.Layout();
		Assert.IsTrue( text.LayoutTree.Node.InlineFragments.Count > 0 );
	}

	[TestMethod]
	public void SourceRangesRemainUtf16AfterWhitespaceCollapseAndSurrogates()
	{
		var root = Root();
		var p = Paragraph( root, 400 );
		var text = Text( p, "  A\U0001F600B" );
		root.Layout();
		var fragments = text.LayoutTree.Node.InlineFragments;
		Assert.AreEqual( 2, fragments.Min( f => f.TextStart ) );
		Assert.AreEqual( 6, fragments.Max( f => f.TextStart + f.TextLength ) );
		p.SelectAllInChildren();
		Assert.AreEqual( "A\U0001F600B", p.GetClipboardValue( false ) );
	}

	[TestMethod]
	[DataRow( "office tail", "Calibri" )]
	[DataRow( "\u0644\u0627 tail", "Arial" )]
	[DataRow( "A\U0001F600e\u0301 tail", "Arial" )]
	public void SelectionUsesShapedCaretsForCopyAndPaint( string value, string font )
	{
		var root = Root();
		var p = Paragraph( root, 400 );
		p.Style.FontFamily = font;
		var label = Text( p, value );
		root.Layout();
		var shaped = new Topten.RichTextKit.TextBlock { FontMapper = FontManager.Instance };
		shaped.AddText( value, label._textBlock.InlineStyle );
		var carets = shaped.CaretIndicies;
		if ( font == "Calibri" )
			Assert.IsTrue( carets.Count - 1 < value.Length, "Regression requires a shaped ligature" );
		for ( int i = 0; i < carets.Count - 1; i++ )
		{
			p.InlineParagraph.SetSelection( i + 1, i );
			var from = shaped.CodePointToCharacterIndex( carets[i] );
			var to = shaped.CodePointToCharacterIndex( carets[i + 1] );
			Assert.AreEqual( value[from..to], p.GetClipboardValue( false ) );
		}
		p.InlineParagraph.Select( p.InlineParagraph.Origin, p.InlineParagraph.Origin + new Vector2( 390, 40 ) );
		if ( font == "Calibri" ) Assert.AreEqual( value, p.GetClipboardValue( false ) );
		p.SelectAllInChildren();
		Assert.AreEqual( value, p.GetClipboardValue( false ) );
		Assert.AreEqual( carets.Count - 1, p.InlineParagraph.Text.SelectionEnd );
	}

	[TestMethod]
	public void GeneratedTextAloneDoesNotActivateInlineFormatting()
	{
		var root = Root();
		var p = Paragraph( root );
		var builder = new PanelRenderTreeBuilder( p );
		builder.Start();
		builder.AddMarkupContent( 0, "legacy text" );
		builder.Finish();
		root.Layout();
		Assert.IsTrue( p.Children.OfType<Label>().Single().IsGeneratedText );
		Assert.IsNull( p.InlineParagraph );
		var span = Text( p, " inline" );
		root.Layout();
		Assert.AreEqual( "legacy text inline", p.InlineParagraph.Text.Text );
		span.Style.Display = DisplayMode.None;
		root.Layout();
		Assert.IsNull( p.InlineParagraph );
		Assert.IsTrue( p.Children.All( c => c.InlineOwner is null && c.LayoutTree.Node.InlineFragments.Count == 0 ) );
	}

	[TestMethod]
	[DataRow( "text-align: center;" )]
	[DataRow( "white-space: pre;" )]
	[DataRow( "text-transform: uppercase;" )]
	[DataRow( "padding-left: 10px;" )]
	[DataRow( "background-color: red;" )]
	[DataRow( "opacity: 0;" )]
	[DataRow( "transform: translateX(10px);" )]
	[DataRow( "text-shadow: 1px 1px 2px black;" )]
	public void UnsupportedStylesRestoreLegacyLayout( string style )
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "hello world" );
		root.Layout();
		Assert.IsNotNull( p.InlineParagraph );
		text.Style.Set( style );
		root.Layout();
		Assert.IsNull( p.InlineParagraph, style );
		Assert.IsNull( text.InlineOwner );
		Assert.AreEqual( 0, text.LayoutTree.Node.InlineFragments.Count );
	}

	[TestMethod]
	public void HidingParagraphReleasesOwnersAndFragments()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "hello world" );
		root.Layout();
		p.Style.Display = DisplayMode.None;
		root.Layout();
		Assert.IsNull( p.InlineParagraph );
		Assert.IsNull( text.InlineOwner );
		Assert.AreEqual( 0, text.LayoutTree.Node.InlineFragments.Count );
		p.Style.Display = DisplayMode.Block;
		root.Layout();
		Assert.AreSame( p.InlineParagraph, text.InlineOwner );
		Assert.IsTrue( text.LayoutTree.Node.InlineFragments.Count > 0 );
	}

	[TestMethod]
	public void ParentStylesInvalidateWithExplicitChildFontAndPaintRestoresFinalWidth()
	{
		var root = Root();
		var p = Paragraph( root, 180 );
		var text = Text( p, "several words wrapping across multiple lines" );
		text.Style.Set( "font-size: 20px; font-family: Arial; line-height: 24px;" );
		root.Layout();
		p.SelectAllInChildren();
		p.Style.FontSize = 40;
		root.Layout();
		Assert.IsFalse( p.InlineParagraph.Text.ShouldDrawSelection, "Parent style changes rebuild the paragraph too" );
		var height = p.InlineParagraph.Text.MeasuredSize.y;
		p.InlineParagraph.Measure( float.NaN, false );
		Assert.IsTrue( p.InlineParagraph.Text.MeasuredSize.y < height );
		p.InlineParagraph.Draw();
		Assert.AreEqual( height, p.InlineParagraph.Text.MeasuredSize.y );
		p.SelectAllInChildren();
		text.Style.FontFamily = "Courier New";
		root.Layout();
		Assert.IsFalse( p.InlineParagraph.Text.ShouldDrawSelection, "Font changes invalidate shaped caret ordinals" );
	}

	[TestMethod]
	public void InlineStylePreservesDecorationSettings()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "decorated" );
		text.Style.Set( "text-decoration: underline; text-decoration-style: wavy; text-decoration-thickness: 3px; text-underline-offset: 4px;" );
		root.Layout();
		Assert.IsNotNull( p.InlineParagraph );
		var style = text._textBlock.InlineStyle;
		Assert.AreEqual( Topten.RichTextKit.UnderlineType.Wavy, style.UnderlineStrokeType );
		Assert.AreEqual( 3f, style.StrokeThickness );
		Assert.AreEqual( 4f, style.UnderlineOffset );
	}

	[TestMethod]
	public void InheritedFontChangesUpdateParagraphBaseline()
	{
		var root = Root();
		var p = Paragraph( root, 400 );
		Text( p, "hello" );
		root.Layout();
		var before = p.InlineParagraph.Layout( 400 );
		p.Style.FontSize = 40;
		root.Layout();
		var after = p.InlineParagraph.Layout( 400 );
		Assert.IsTrue( after.Baseline > before.Baseline );
		Assert.IsTrue( after.Size.Height > before.Size.Height );
	}

	private sealed class CustomInlinePanel : Panel;

	[TestMethod]
	public void ReplacedAndCustomInlineChildrenKeepLegacyLayout()
	{
		var root = Root();
		foreach ( var child in new Panel[] { new Image(), new TextEntry(), new CustomInlinePanel() } )
		{
			var p = Paragraph( root );
			var text = Text( p, "hello" );
			root.Layout();
			child.Parent = p;
			child.Style.Display = DisplayMode.Inline;
			root.Layout();
			Assert.IsNull( p.InlineParagraph, child.GetType().Name );
			Assert.IsNull( text.InlineOwner );
		}
	}

	[TestMethod]
	public void MixedBlockContentAndTextEntryKeepLegacyLayout()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "inline" );
		var block = p.AddChild<Panel>();
		block.Style.Set( "display: block; height: 20px;" );
		var entry = root.AddChild<TextEntry>();
		entry.Text = "editable";
		root.Layout();
		Assert.IsNull( p.InlineParagraph );
		var entryLabel = entry.Children.OfType<Label>().First();
		Assert.IsNull( entryLabel.InlineOwner );
		Assert.IsTrue( entryLabel.LayoutTree.IsMeasureDefined );
	}
}
