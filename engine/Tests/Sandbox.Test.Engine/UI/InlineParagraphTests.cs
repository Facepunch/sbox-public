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
		var oldLayout = p.LayoutTree.InlineContext.Layout( 180 );
		var removedLayout = deleted.LayoutTree;
		var removedNode = removedLayout.Node;
		var target = nested ? span : deleted;
		if ( deferred ) target.Delete();
		root.PreLayout();
		// The live UI loop completes deferred deletion after PreLayout, unlike RootPanel.Layout().
		if ( deferred ) GlobalContext.Current.UISystem.RunDeferredDeletion( true );
		else target.Delete( true );
		Assert.IsNull( deleted.LayoutTree );
		root.CalculateLayout();
		root.PostLayout();
		Assert.AreEqual( "before after", p.LayoutTree.InlineContext.Text.Text );
		Assert.IsNull( removedLayout.InlineContext );
		Assert.IsFalse( p.LayoutTree.InlineContext.Text.ShouldDrawSelection );
		var layout = p.LayoutTree.InlineContext.Layout( 180 );
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
		Assert.AreEqual( "", p.LayoutTree.InlineContext.Text.Text );
		Assert.AreEqual( 0, p.LayoutTree.InlineContext.Layout( 180 ).Fragments.Count );
		using ( var painter = Painter.Begin( new Sandbox.Rendering.CommandList(), root.PanelBounds ) )
		{
			p.LayoutTree.InlineContext.Draw( painter );
		}
		root.Layout();
		Assert.IsNull( p.LayoutTree.InlineContext );
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
		if ( measureFirst ) p.LayoutTree.InlineContext.Measure( float.NaN, false );
		root.CalculateLayout();
		Assert.AreEqual( "before", p.LayoutTree.InlineContext.Text.Text );
		Assert.IsNull( replacement.LayoutTree.InlineContext );
		Assert.IsFalse( p.LayoutTree.InlineContext.Layout( 180 ).Fragments.Any( f => f.Owner == replacement.LayoutTree.Node ) );
		root.PostLayout();
		root.Layout();
		Assert.AreEqual( "before replacement", p.LayoutTree.InlineContext.Text.Text );
		Assert.AreSame( p.LayoutTree.InlineContext, replacement.LayoutTree.InlineContext );
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
		Assert.AreEqual( "stay", source.LayoutTree.InlineContext.Text.Text );
		Assert.IsFalse( source.LayoutTree.InlineContext.Layout( 180 ).Fragments.Any( f => f.Owner == moved.LayoutTree.Node ) );
		root.Layout();
		Assert.AreEqual( "new move", destination.LayoutTree.InlineContext.Text.Text );
		Assert.AreSame( destination.LayoutTree.InlineContext, moved.LayoutTree.InlineContext );
	}

	[TestMethod]
	public void InlineParticipantCanBecomeAHostAndRejoinItsAncestor()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "before " );
		var span = p.AddChild<Panel>();
		span.Style.Display = DisplayMode.Inline;
		var text = Text( span, "nested" );
		root.Layout();
		Assert.AreSame( p.LayoutTree, span.LayoutTree.InlineContext.Root );
		Assert.IsTrue( span.LayoutTree.IsInlineParticipant );

		span.Style.Display = DisplayMode.Block;
		root.Layout();
		Assert.IsNull( p.LayoutTree.InlineContext );
		Assert.AreSame( span.LayoutTree, span.LayoutTree.InlineContext.Root );
		Assert.IsTrue( span.LayoutTree.HasInlineContent );
		Assert.IsFalse( span.LayoutTree.IsInlineParticipant );
		Assert.AreSame( span.LayoutTree.InlineContext, span.LayoutTree.Node.InlineContent );
		Assert.AreSame( span.LayoutTree.InlineContext, text.LayoutTree.InlineContext );
		span.SelectAllInChildren();
		Assert.AreEqual( "nested", text.GetClipboardValue( false ) );

		span.Style.Display = DisplayMode.Inline;
		root.Layout();
		Assert.AreSame( p.LayoutTree, span.LayoutTree.InlineContext.Root );
		Assert.IsFalse( span.LayoutTree.HasInlineContent );
		Assert.IsTrue( span.LayoutTree.IsInlineParticipant );
		Assert.IsNull( span.LayoutTree.Node.InlineContent );
		Assert.AreSame( p.LayoutTree.InlineContext, text.LayoutTree.InlineContext );
		p.SelectAllInChildren();
		Assert.AreEqual( "before nested", text.GetClipboardValue( false ) );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ReleasingOldHostPreservesReparentedParticipants( bool destinationFirst )
	{
		var root = Root();
		var first = Paragraph( root );
		var second = Paragraph( root );
		var source = destinationFirst ? second : first;
		var destination = destinationFirst ? first : second;
		Text( destination, "new " );
		var span = source.AddChild<Panel>();
		span.Style.Display = DisplayMode.Inline;
		var text = Text( span, "move" );
		root.Layout();
		var destinationContext = destination.LayoutTree.InlineContext;

		span.Parent = destination;
		root.Layout();
		Assert.IsNull( source.LayoutTree.InlineContext );
		Assert.IsNull( source.LayoutTree.Node.InlineContent );
		Assert.AreSame( destinationContext, span.LayoutTree.InlineContext );
		Assert.AreSame( destinationContext, text.LayoutTree.InlineContext );
		Assert.IsTrue( text.LayoutTree.Node.InlineFragments.Count > 0 );
		destination.SelectAllInChildren();
		Assert.AreEqual( "new move", text.GetClipboardValue( false ) );
	}

	[TestMethod]
	public void DeletingHostReleasesItsContextAndParticipants()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "delete" );
		root.Layout();
		var hostLayout = p.LayoutTree;
		var textLayout = text.LayoutTree;
		var hostNode = hostLayout.Node;

		p.Delete( true );
		Assert.IsNull( hostLayout.InlineContext );
		Assert.IsNull( textLayout.InlineContext );
		Assert.IsNull( hostNode.InlineContent );
	}

	[TestMethod]
	public void UnchangedFormattingMeasurementAndPaintReuseLayout()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "before " );
		var span = p.AddChild<Panel>();
		span.Style.Display = DisplayMode.Inline;
		Text( span, "several words wrapping across multiple lines" );
		root.Layout();
		root.Layout();
		var paragraph = p.LayoutTree.InlineContext;
		using var painter = Painter.Begin( new Sandbox.Rendering.CommandList(), root.PanelBounds );
		var layout = paragraph.Layout( 180 );
		InlineFormattingContext.CanFormat( p );
		paragraph.Update();
		paragraph.Measure( 180, false );
		paragraph.FinalizeLayout();
		paragraph.Draw( painter );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
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
		var paragraph = p.LayoutTree.InlineContext;
		using var painter = Painter.Begin( new Sandbox.Rendering.CommandList(), root.PanelBounds );
		var layout = paragraph.Layout( 180 );
		var fragments = text.LayoutTree.Node.InlineFragments;
		var origin = paragraph.Origin;
		paragraph.SetSelection( 0, 7 );
		var selection = paragraph.SelectedText;
		var scroller = scrollParagraph ? p : host;
		scroller.ScrollOffset = new Vector2( 0, 20 );
		scroller.SetNeedsFinalLayout();
		root.PostLayout();
		Assert.AreEqual( origin - new Vector2( 0, 20 ), paragraph.Origin );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		Assert.AreSame( fragments, text.LayoutTree.Node.InlineFragments );
		paragraph.Draw( painter );
		Assert.AreEqual( selection, paragraph.SelectedText );
		paragraph.FinalizeLayout();
		paragraph.Draw( painter );
	}

	[TestMethod]
	public void GeometryCacheInvalidatesForContentStylesAndWidthButNotSelectionOrIntrinsicMeasure()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "several words wrapping across multiple lines" );
		root.Layout();
		root.Layout();
		var paragraph = p.LayoutTree.InlineContext;
		using var painter = Painter.Begin( new Sandbox.Rendering.CommandList(), root.PanelBounds );
		var layout = paragraph.Layout( 180 );
		paragraph.Measure( float.NaN, false );
		paragraph.Measure( 0, true );
		paragraph.Draw( painter );
		Assert.AreSame( layout, paragraph.Layout( 180 ), "Intrinsic measurement must not replace final fragments" );
		Assert.AreEqual( layout.Size.Height, paragraph.Text.MeasuredSize.y );
		paragraph.SetSelection( 0, 7 );
		paragraph.SetSelection( 0, 7 );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		paragraph.SetSelection( 0, 8 );
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
		var paragraph = p.LayoutTree.InlineContext;
		using var painter = Painter.Begin( new Sandbox.Rendering.CommandList(), root.PanelBounds );
		var layout = paragraph.Layout( 180 );
		p.SelectAllInChildren();
		p.Style.Opacity = 0.5f;
		p.Style.BackgroundColor = Color.Red;
		root.Layout();
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		Assert.AreEqual( "selected paragraph", paragraph.SelectedText );
	}

	[TestMethod]
	public void InheritedBlendChangesDirtyDescriptorsWithoutReshapingOrClearingSelection()
	{
		var root = Root();
		var p = Paragraph( root );
		Text( p, "selected paragraph" );
		root.Layout();
		root.Layout();
		var paragraph = p.LayoutTree.InlineContext;
		using var painter = Painter.Begin( new Sandbox.Rendering.CommandList(), root.PanelBounds );
		var layout = paragraph.Layout( 180 );
		p.SelectAllInChildren();
		root.Style.MixBlendMode = "multiply";
		root.Layout();
		Assert.AreEqual( "multiply", p.ComputedStyle.MixBlendMode );
		Assert.AreSame( layout, paragraph.Layout( 180 ) );
		Assert.AreEqual( "selected paragraph", paragraph.SelectedText );
		paragraph.Update();
		paragraph.FinalizeLayout();
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
		Assert.IsNotNull( p.LayoutTree.InlineContext );
		Assert.AreSame( p.LayoutTree.InlineContext, text.LayoutTree.InlineContext );
		var fragments = link.LayoutTree.Node.InlineFragments;
		Assert.IsTrue( fragments.Select( f => f.Y ).Distinct().Count() > 1 );
		Assert.IsTrue( fragments[0].X > 0, "The link shares the prefix's line" );
		Assert.IsTrue( link.IsInside( p.LayoutTree.InlineContext.Origin + new Vector2( fragments[0].X + 1, fragments[0].Y + 1 ) ) );
		Assert.IsFalse( link.IsInside( p.LayoutTree.InlineContext.Origin + new Vector2( 1, fragments[0].Y + 1 ) ), "Union-box gap must not hit the link" );
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
		Assert.AreEqual( "Before short", p.LayoutTree.InlineContext.Text.Text );
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
		Assert.AreEqual( "hello world\u00a0!", p.LayoutTree.InlineContext.Text.Text );
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
		Assert.IsNotNull( label.LayoutTree.InlineContext );
		p.Style.Display = DisplayMode.Flex;
		root.Layout();
		Assert.IsNull( p.LayoutTree.InlineContext );
		Assert.IsNull( label.LayoutTree.InlineContext );
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
		Assert.AreEqual( "one two", p.LayoutTree.InlineContext.Text.Text );
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
		Assert.AreEqual( "interactive", p.LayoutTree.InlineContext.Text.Text );
		var oldHash = last._textBlock.InlineStyleHash;
		last.Style.FontColor = Color.Red;
		root.Layout();
		Assert.AreNotEqual( oldHash, last._textBlock.InlineStyleHash );
		Assert.AreEqual( "interactive", p.LayoutTree.InlineContext.Text.Text );
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
		Assert.AreEqual( whole.LayoutTree.InlineContext.Text.MeasuredSize.x, split.LayoutTree.InlineContext.Text.MeasuredSize.x, 0.001f );
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
		Assert.AreEqual( "first", p.LayoutTree.InlineContext.Text.Text );
		Assert.IsNull( other.LayoutTree.InlineContext );
		Assert.AreEqual( 0, other.LayoutTree.Node.InlineFragments.Count );
		text.Parent = destination;
		root.Layout();
		Assert.AreSame( destination.LayoutTree.InlineContext, text.LayoutTree.InlineContext );
		Assert.AreEqual( "new first", destination.LayoutTree.InlineContext.Text.Text );
		text.Text = "";
		root.Layout();
		Assert.AreEqual( "new", destination.LayoutTree.InlineContext.Text.Text );
	}

	[TestMethod]
	public void DragSelectionCrossesSpanBoundaryWithoutCopySeparators()
	{
		var root = Root();
		var p = Paragraph( root, 400 );
		Text( p, "hello " );
		var last = Text( p, "world" );
		root.Layout();
		var origin = p.LayoutTree.InlineContext.Origin;
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
		Assert.AreEqual( "", p.LayoutTree.InlineContext.Text.Text );
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
			p.LayoutTree.InlineContext.SetSelection( i + 1, i );
			var from = shaped.CodePointToCharacterIndex( carets[i] );
			var to = shaped.CodePointToCharacterIndex( carets[i + 1] );
			Assert.AreEqual( value[from..to], p.GetClipboardValue( false ) );
		}
		p.LayoutTree.InlineContext.Select( p.LayoutTree.InlineContext.Origin, p.LayoutTree.InlineContext.Origin + new Vector2( 390, 40 ) );
		if ( font == "Calibri" ) Assert.AreEqual( value, p.GetClipboardValue( false ) );
		p.SelectAllInChildren();
		Assert.AreEqual( value, p.GetClipboardValue( false ) );
		Assert.AreEqual( carets.Count - 1, p.LayoutTree.InlineContext.Text.SelectionEnd );
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
		Assert.IsNull( p.LayoutTree.InlineContext );
		var span = Text( p, " inline" );
		root.Layout();
		Assert.AreEqual( "legacy text inline", p.LayoutTree.InlineContext.Text.Text );
		span.Style.Display = DisplayMode.None;
		root.Layout();
		Assert.IsNull( p.LayoutTree.InlineContext );
		Assert.IsTrue( p.Children.All( c => c.LayoutTree.InlineContext is null && c.LayoutTree.Node.InlineFragments.Count == 0 ) );
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
		Assert.IsNotNull( p.LayoutTree.InlineContext );
		text.Style.Set( style );
		root.Layout();
		Assert.IsNull( p.LayoutTree.InlineContext, style );
		Assert.IsNull( text.LayoutTree.InlineContext );
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
		Assert.IsNull( p.LayoutTree.InlineContext );
		Assert.IsNull( text.LayoutTree.InlineContext );
		Assert.AreEqual( 0, text.LayoutTree.Node.InlineFragments.Count );
		p.Style.Display = DisplayMode.Block;
		root.Layout();
		Assert.AreSame( p.LayoutTree.InlineContext, text.LayoutTree.InlineContext );
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
		Assert.IsFalse( p.LayoutTree.InlineContext.Text.ShouldDrawSelection, "Parent style changes rebuild the paragraph too" );
		var height = p.LayoutTree.InlineContext.Text.MeasuredSize.y;
		p.LayoutTree.InlineContext.Measure( float.NaN, false );
		Assert.IsTrue( p.LayoutTree.InlineContext.Text.MeasuredSize.y < height );
		using ( var painter = Painter.Begin( new Sandbox.Rendering.CommandList(), root.PanelBounds ) )
		{
			p.LayoutTree.InlineContext.Draw( painter );
		}
		Assert.AreEqual( height, p.LayoutTree.InlineContext.Text.MeasuredSize.y );
		p.SelectAllInChildren();
		text.Style.FontFamily = "Courier New";
		root.Layout();
		Assert.IsFalse( p.LayoutTree.InlineContext.Text.ShouldDrawSelection, "Font changes invalidate shaped caret ordinals" );
	}

	[TestMethod]
	public void InlineStylePreservesDecorationSettings()
	{
		var root = Root();
		var p = Paragraph( root );
		var text = Text( p, "decorated" );
		text.Style.Set( "text-decoration: underline; text-decoration-style: wavy; text-decoration-thickness: 3px; text-underline-offset: 4px;" );
		root.Layout();
		Assert.IsNotNull( p.LayoutTree.InlineContext );
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
		var before = p.LayoutTree.InlineContext.Layout( 400 );
		p.Style.FontSize = 40;
		root.Layout();
		var after = p.LayoutTree.InlineContext.Layout( 400 );
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
			Assert.IsNull( p.LayoutTree.InlineContext, child.GetType().Name );
			Assert.IsNull( text.LayoutTree.InlineContext );
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
		Assert.IsNull( p.LayoutTree.InlineContext );
		var entryLabel = entry.Children.OfType<Label>().First();
		Assert.IsNull( entryLabel.LayoutTree.InlineContext );
		Assert.IsTrue( entryLabel.LayoutTree.IsMeasureDefined );
	}
}
