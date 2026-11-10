using System;
using System.Collections.Generic;
using Sandbox.Layout;

namespace LayoutTests.Core;

[TestClass]
public class InlineContentTests
{
	private sealed class Paragraph( LayoutNode first, LayoutNode second ) : IInlineContent
	{
		public float WordWidth = 40;
		public int Calls;
		public LayoutSize Measure( float width, bool minContent ) => new( minContent ? WordWidth : WordWidth * 2, 20 );
		public InlineContentLayout Layout( float width )
		{
			Calls++;
			var wraps = width < WordWidth * 2;
			return new( new LayoutSize( wraps ? WordWidth : WordWidth * 2, wraps ? 40 : 20 ), 15,
				new List<InlineFragment>
				{
					new( first, 0, 4, 0, 0, WordWidth, 20 ),
					new( second, 0, 4, wraps ? 0 : WordWidth, wraps ? 20 : 0, WordWidth, 20 )
				} );
		}
	}

	[TestMethod]
	public void NonLeafParagraphWrapsAndPublishesNestedOwners()
	{
		var root = new LayoutNode();
		root.Style.Display = Display.Block;
		root.Style.Width = StyleLength.Points( 70 );
		root.Style.SetPadding( Edge.All, StyleLength.Points( 5 ) );
		var span = new LayoutNode();
		var first = new LayoutNode();
		var second = new LayoutNode();
		root.AddChild( span );
		span.AddChild( first );
		span.AddChild( second );
		first.MeasureFunc = ( _, _, _, _, _ ) => throw new Exception( "Must not measure participants independently" );
		second.MeasureFunc = first.MeasureFunc;
		var paragraph = new Paragraph( first, second );
		root.InlineContent = paragraph;
		root.CalculateLayout();
		Assert.AreEqual( 50f, root.LayoutHeight );
		Assert.AreEqual( 5f, span.LayoutLeft );
		Assert.AreEqual( 5f, span.LayoutTop );
		Assert.AreEqual( 20f, second.LayoutTop );
		Assert.AreEqual( 2, span.InlineFragments.Count );
		Assert.AreSame( second, span.InlineFragments[1].Owner );
		Assert.IsFalse( first.IsDirty );
		Assert.IsTrue( first.HasMeasureFunc );
		var calls = paragraph.Calls;
		root.CalculateLayout();
		Assert.AreEqual( calls, paragraph.Calls, "Clean layout is cached" );

		root.Style.Width = StyleLength.Points( 120 );
		root.CalculateLayout();
		Assert.AreEqual( 30f, root.LayoutHeight );
		Assert.AreEqual( 40f, second.LayoutLeft );
		Assert.AreEqual( 0f, second.LayoutTop );

		paragraph.WordWidth = 70;
		second.MarkDirty();
		Assert.IsTrue( root.IsDirty );
		root.CalculateLayout();
		Assert.AreEqual( 50f, root.LayoutHeight );
		Assert.AreEqual( 70f, first.LayoutWidth );
	}

	[TestMethod]
	public void InlineCapabilityDoesNotRelaxMeasuredLeafInvariant()
	{
		var leaf = new LayoutNode { MeasureFunc = ( _, _, _, _, _ ) => new( 10, 10 ) };
		var content = new Paragraph( leaf, leaf );
		Assert.ThrowsException<InvalidOperationException>( () => leaf.AddChild( new LayoutNode() ) );
		Assert.ThrowsException<InvalidOperationException>( () => leaf.InlineContent = content );
		var paragraph = new LayoutNode { InlineContent = content };
		Assert.ThrowsException<InvalidOperationException>( () => paragraph.MeasureFunc = leaf.MeasureFunc );
		paragraph.Reset();
		Assert.IsNull( paragraph.InlineContent );
	}

	[TestMethod]
	public void InlineCallbackIsOnlyUsedForBlockDispatch()
	{
		var root = new LayoutNode();
		var child = new LayoutNode();
		root.AddChild( child );
		child.Style.Width = StyleLength.Points( 10 );
		child.Style.Height = StyleLength.Points( 20 );
		var paragraph = new Paragraph( child, child );
		root.InlineContent = paragraph;
		root.CalculateLayout();
		Assert.AreEqual( 0, paragraph.Calls );
		Assert.AreEqual( 20f, root.LayoutHeight );
	}

	[TestMethod]
	public void HidingParagraphClearsDescendantFragments()
	{
		var root = new LayoutNode();
		var paragraph = new LayoutNode();
		paragraph.Style.Display = Display.Block;
		var text = new LayoutNode();
		paragraph.AddChild( text );
		paragraph.InlineContent = new Paragraph( text, text );
		root.AddChild( paragraph );
		root.CalculateLayout();
		Assert.IsTrue( text.InlineFragments.Count > 0 );
		paragraph.Style.Display = Display.None;
		root.CalculateLayout();
		Assert.AreEqual( 0, text.InlineFragments.Count );
		Assert.AreEqual( 0f, text.LayoutWidth );
	}

	[TestMethod]
	public void ParagraphBaselineIncludesPaddingAndDoesNotUseItsUnionBox()
	{
		var root = new LayoutNode();
		root.Style.AlignItems = Align.Baseline;
		var p = new LayoutNode();
		p.Style.Display = Display.Block;
		p.Style.Width = StyleLength.Points( 50 );
		p.Style.SetPadding( Edge.Top, StyleLength.Points( 5 ) );
		var text = new LayoutNode();
		p.AddChild( text );
		p.InlineContent = new Paragraph( text, text );
		var sibling = new LayoutNode { MeasureFunc = ( _, _, _, _, _ ) => new( 10, 10 ), BaselineFunc = ( _, _, _ ) => 8 };
		root.AddChild( p );
		root.AddChild( sibling );
		root.CalculateLayout();
		Assert.AreEqual( 45f, p.LayoutHeight );
		Assert.AreEqual( 12f, sibling.LayoutTop );
	}
}
