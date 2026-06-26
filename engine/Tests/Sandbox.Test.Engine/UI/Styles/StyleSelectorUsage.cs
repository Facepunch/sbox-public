using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public class StyleSelectorUsageTest
{
	[TestMethod]
	public void SingleClass()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { background-color: red; }" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.RemoveClass( "one" );
		r.Layout();

		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );
	}

	[TestMethod]
	public void SingleId()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( "#mypanel { background-color: red; }" );
		var p = new Panel { Parent = r };

		p.Id = "mypanel";
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.Id = null;
		r.Layout();

		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );
	}

	[TestMethod]
	public void SingleIdAndClass()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( "#mypanel.classname { background-color: red; }" );
		var p = new Panel { Parent = r };

		p.Id = "MyPanel";
		p.SetClass( "classname", true );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.SetClass( "classname", false );
		r.Layout();
		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );

		p.SetClass( "classname", true );
		p.Id = "changed";
		r.Layout();
		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );
	}

	[TestMethod]
	public void HoverFlag()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { background-color: red; } .one:hover { background-color: yellow; }" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.PseudoClass |= PseudoClass.Hover;
		r.Layout();

		Assert.AreEqual( new Color( 1, 1, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void HoverFlagNested()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { &:hover { background-color: yellow; } }" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();

		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );

		p.PseudoClass |= PseudoClass.Hover;
		r.Layout();

		Assert.AreEqual( new Color( 1, 1, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void ActiveFlag()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { background-color: red; } .one:active { background-color: yellow; }" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.PseudoClass |= PseudoClass.Active;
		r.Layout();

		Assert.AreEqual( new Color( 1, 1, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void ChildSelector()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { .two { background-color: red; } }" );
		var p = new Panel { Parent = r };

		p.AddClass( "two" );
		r.Layout();

		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );

		r.AddClass( "one" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void ElementName()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( "panel { background-color: red; }" );
		var p = new Panel { Parent = r };

		Assert.AreEqual( "panel", p.ElementName );

		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void ElementNameCustom()
	{
		{
			var r = new RootPanel();
			r.StyleSheet.Parse( "MySpecialPanel { background-color: red; }" );
			var p = new MySpecialPanel { Parent = r };

			Assert.AreEqual( "myspecialpanel", p.ElementName );

			r.Layout();

			Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
		}

		{
			var r = new RootPanel();
			r.StyleSheet.Parse( "myspecialpanel { background-color: red; }" );

			var p = new MySpecialPanel { Parent = r };

			Assert.AreEqual( "myspecialpanel", p.ElementName );

			r.Layout();

			Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
		}
	}

	[TestMethod]
	public void ElementNameInverse()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( "elementname { background-color: red; }" );
		var p = new Panel { Parent = r };

		Assert.AreEqual( "panel", p.ElementName );

		r.Layout();

		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );
	}


	[TestMethod]
	public void ChildElementSelector()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( "MySpecialPanel { .two { background-color: red; } }" );

		var p = new MySpecialPanel { Parent = r };

		r.Layout();

		var q = p.Add.Panel( "Poopy" );

		r.Layout();

		Assert.IsTrue( q.ComputedStyle.IsDefault( "background-color" ) );

		q.AddClass( "two" );

		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), q.ComputedStyle.BackgroundColor.Value );
	}


	[TestMethod]
	public void ChildStyleSheet()
	{
		var r = new RootPanel();

		var a = r.Add.Panel();
		a.AddClass( "one" );

		var b = a.Add.Panel();
		b.AddClass( "two" );
		b.StyleSheet.Add( StyleParser.ParseSheet( ".two { opacity: 0; } .active{ .two { opacity: 1; } }" ) );

		r.Layout();

		Assert.IsFalse( b.IsVisible );
		Assert.AreEqual( 0.0f, b.ComputedStyle.Opacity.Value );

		r.AddClass( "active" );
		r.Layout();

		Assert.IsTrue( b.IsVisible );
		Assert.AreEqual( 1.0f, b.ComputedStyle.Opacity.Value );
	}

	[TestMethod]
	public void NotNested()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { background-color: yellow; &:not( .red ){ background-color: red; } }" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.AddClass( "red" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 1, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void NotRegular()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { background-color: yellow;} .one:not( .red ){ background-color: red; }" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.AddClass( "red" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 1, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void NotReverseOrder()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one:not( .red ){ background-color: red; } .one { background-color: yellow;}" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.AddClass( "red" );
		r.Layout();

		Assert.AreEqual( new Color( 1, 1, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void ImmediateChild()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one > .two { background-color: yellow;}" );
		var one = new Panel { Parent = r };

		one.AddClass( "one" );
		r.Layout();

		Assert.IsTrue( one.ComputedStyle.IsDefault( "background-color" ) );

		var two = one.Add.Panel( "two" );
		r.Layout();

		Assert.IsTrue( one.ComputedStyle.IsDefault( "background-color" ) );
		Assert.AreEqual( new Color( 1, 1, 0, 1 ), two.ComputedStyle.BackgroundColor.Value );

		var three = two.Add.Panel( "two" );
		r.Layout();

		Assert.IsTrue( one.ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( three.ComputedStyle.IsDefault( "background-color" ) );
		Assert.AreEqual( new Color( 1, 1, 0, 1 ), two.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void ImmediateChildNested()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one {  > .two { background-color: yellow;} }" );
		var one = new Panel { Parent = r };

		one.AddClass( "one" );
		r.Layout();

		Assert.IsTrue( one.ComputedStyle.IsDefault( "background-color" ) );

		var two = one.Add.Panel( "two" );
		r.Layout();

		Assert.IsTrue( one.ComputedStyle.IsDefault( "background-color" ) );
		Assert.AreEqual( new Color( 1, 1, 0, 1 ), two.ComputedStyle.BackgroundColor.Value );

		var three = two.Add.Panel( "two" );
		r.Layout();

		Assert.IsTrue( one.ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( three.ComputedStyle.IsDefault( "background-color" ) );
		Assert.AreEqual( new Color( 1, 1, 0, 1 ), two.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void NthChildSpecific()
	{
		{
			var r = new RootPanel();
			r.StyleSheet.Parse( ".one:nth-child( 2 ) { background-color: yellow; }" );

			var panels = new Panel[10];

			for ( int i = 0; i < panels.Length; i++ )
				panels[i] = r.Add.Panel( "one" );

			r.Layout();

			Assert.IsTrue( panels[0].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsFalse( panels[1].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[2].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[3].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[4].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[5].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[6].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[7].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[8].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[9].ComputedStyle.IsDefault( "background-color" ) );
		}

		{
			var r = new RootPanel();
			r.StyleSheet.Parse( ".one:nth-child( 7 ) { background-color: yellow; }" );

			var panels = new Panel[10];

			for ( int i = 0; i < panels.Length; i++ )
				panels[i] = r.Add.Panel( "one" );

			r.Layout();

			Assert.IsTrue( panels[0].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[1].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[2].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[3].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[4].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[5].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsFalse( panels[6].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[7].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[8].ComputedStyle.IsDefault( "background-color" ) );
			Assert.IsTrue( panels[9].ComputedStyle.IsDefault( "background-color" ) );
		}
	}

	[TestMethod]
	public void NthChildOdd()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one:nth-child( odd ) { background-color: yellow; }" );

		var panels = new Panel[10];

		for ( int i = 0; i < panels.Length; i++ )
			panels[i] = r.Add.Panel( "one" );

		r.Layout();

		Assert.IsFalse( panels[0].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[1].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[2].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[3].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[4].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[5].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[6].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[7].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[8].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[9].ComputedStyle.IsDefault( "background-color" ) );
	}

	[TestMethod]
	public void NthChildEven()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one:nth-child( even ) { background-color: yellow; }" );

		var panels = new Panel[10];

		for ( int i = 0; i < panels.Length; i++ )
			panels[i] = r.Add.Panel( "one" );

		r.Layout();

		Assert.IsTrue( panels[0].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[1].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[2].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[3].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[4].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[5].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[6].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[7].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsTrue( panels[8].ComputedStyle.IsDefault( "background-color" ) );
		Assert.IsFalse( panels[9].ComputedStyle.IsDefault( "background-color" ) );
	}

	[TestMethod]
	public void LastChildOnAppend()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one:last-child { background-color: yellow; }" );

		var p1 = r.Add.Panel( "one" );
		var p2 = r.Add.Panel( "one" );

		r.Layout();

		Assert.IsTrue( p1.ComputedStyle.IsDefault( "background-color" ) );   // p1 is NOT :last-child
		Assert.IsFalse( p2.ComputedStyle.IsDefault( "background-color" ) );  // p2 IS :last-child
	}

	[TestMethod]
	public void LastChildOnRemove()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one:last-child { background-color: yellow; }" );

		var p1 = r.Add.Panel( "one" );
		var p2 = r.Add.Panel( "one" );

		r.Layout();

		// Detach p2... p1 should become :last-child immediately
		p2.Parent = null;

		r.Layout();

		Assert.IsFalse( p1.ComputedStyle.IsDefault( "background-color" ) );  // p1 is now :last-child
	}

	[TestMethod]
	public void LastChildOnReorder()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one:last-child { background-color: yellow; }" );

		var p1 = r.Add.Panel( "one" );
		var p2 = r.Add.Panel( "one" );

		r.Layout();

		// Move p1 to the end: order becomes p2, p1
		r.SetChildIndex( p1, 1 );

		r.Layout();

		Assert.IsTrue( p2.ComputedStyle.IsDefault( "background-color" ) );   // p2 is no longer :last-child
		Assert.IsFalse( p1.ComputedStyle.IsDefault( "background-color" ) );  // p1 is now :last-child
	}

	/// <summary>
	/// "a + b" should match an adjacent sibling.
	/// </summary>
	[TestMethod]
	public void AdjacentSiblingCombinator()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".a + .b { background-color: red; }" );

		r.Add.Panel( "a" );
		var b = r.Add.Panel( "b" );
		r.Layout();

		Assert.IsFalse( b.ComputedStyle.IsDefault( "background-color" ) );
	}

	/// <summary>
	/// "a ~ b" should match a following sibling.
	/// </summary>
	[TestMethod]
	public void GeneralSiblingCombinator()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".a ~ .b { background-color: red; }" );

		r.Add.Panel( "a" );
		r.Add.Panel( "x" );
		var b = r.Add.Panel( "b" );
		r.Layout();

		Assert.IsFalse( b.ComputedStyle.IsDefault( "background-color" ) );
	}

	/// <summary>
	/// Classes = "" should clear styles and the cached string.
	/// </summary>
	[TestMethod]
	public void ClearingClassesViaSetterRestyles()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".one { background-color: red; }" );
		var p = new Panel { Parent = r };

		p.AddClass( "one" );
		r.Layout();
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		p.Classes = "";
		r.Layout();

		Assert.AreEqual( "", p.Classes );
		Assert.IsTrue( p.ComputedStyle.IsDefault( "background-color" ) );
	}

	/// <summary>
	/// Reparenting should drop the old parent's styles.
	/// </summary>
	[TestMethod]
	public void ReparentRefreshesStyleSheets()
	{
		var r = new RootPanel();

		var a = r.Add.Panel();
		a.StyleSheet.Parse( ".item { background-color: red; }" );
		var b = r.Add.Panel();

		var item = a.Add.Panel( "item" );
		r.Layout();
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), item.ComputedStyle.BackgroundColor.Value );

		item.Parent = b;
		r.Layout();

		Assert.IsTrue( item.ComputedStyle.IsDefault( "background-color" ) );
	}

	/// <summary>
	/// ":has(.a .b)" should match a descendant chain.
	/// </summary>
	[TestMethod]
	public void HasWithDescendantSelector()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".container:has(.a .b) { background-color: red; }" );

		var container = r.Add.Panel( "container" );
		var a = container.Add.Panel( "a" );
		a.Add.Panel( "b" );
		r.Layout();

		Assert.IsFalse( container.ComputedStyle.IsDefault( "background-color" ) );
	}

	/// <summary>
	/// :nth-child should re-evaluate when a sibling is removed and indexes shift.
	/// </summary>
	[TestMethod]
	public void NthChildUpdatesWhenSiblingRemoved()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".item:nth-child( even ) { background-color: red; }" );

		var p0 = r.Add.Panel( "item" );  // index 0 (odd)
		var p1 = r.Add.Panel( "item" );  // index 1 (even)
		var p2 = r.Add.Panel( "item" );  // index 2 (odd)
		var p3 = r.Add.Panel( "item" );  // index 3 (even)
		r.Layout();

		Assert.IsTrue( p2.ComputedStyle.IsDefault( "background-color" ) );   // index 2 (odd)
		Assert.IsFalse( p3.ComputedStyle.IsDefault( "background-color" ) );  // index 3 (even)

		// Remove index 1: p2 shifts 2->1 (even), p3 shifts 3->2 (odd), neither flips first/last
		p1.Parent = null;
		r.Layout();

		Assert.IsFalse( p2.ComputedStyle.IsDefault( "background-color" ) );  // now index 1 (even)
		Assert.IsTrue( p3.ComputedStyle.IsDefault( "background-color" ) );   // now index 2 (odd)
	}

	/// <summary>
	/// :has() should stop matching when the matching descendant is removed.
	/// </summary>
	[TestMethod]
	public void HasUpdatesWhenDescendantRemoved()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".panel:has(.error) { background-color: red; }" );

		var panel = r.Add.Panel( "panel" );
		panel.Add.Panel( "first" );              // stays first
		var error = panel.Add.Panel( "error" );  // middle: removing it flips no sibling pseudo-class
		panel.Add.Panel( "last" );               // stays last
		r.Layout();

		Assert.IsFalse( panel.ComputedStyle.IsDefault( "background-color" ) );  // has .error

		error.Parent = null;
		r.Layout();

		Assert.IsTrue( panel.ComputedStyle.IsDefault( "background-color" ) );   // no longer has .error
	}

	/// <summary>
	/// Matching stays correct with a large stylesheet (class, id, element, multi-class, no-match).
	/// </summary>
	[TestMethod]
	public void ManyRulesMatchCorrectly()
	{
		var r = new RootPanel();

		var sb = new System.Text.StringBuilder();
		for ( int i = 0; i < 600; i++ )
			sb.Append( $".noise{i} {{ width: {i % 100}px; }} " );

		sb.Append( ".target { background-color: red; } " );
		sb.Append( ".target.flagged { background-color: yellow; } " );
		sb.Append( "#special { background-color: red; } " );
		sb.Append( "myspecialpanel { background-color: red; } " );
		r.StyleSheet.Parse( sb.ToString() );

		// class match
		var p = new Panel { Parent = r };
		p.AddClass( "target" );
		r.Layout();
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		// higher-specificity 2-class rule wins
		p.AddClass( "flagged" );
		r.Layout();
		Assert.AreEqual( new Color( 1, 1, 0, 1 ), p.ComputedStyle.BackgroundColor.Value );

		// a panel matching no rule stays default
		var q = new Panel { Parent = r };
		q.AddClass( "nomatch" );
		r.Layout();
		Assert.IsTrue( q.ComputedStyle.IsDefault( "background-color" ) );

		// id match
		var s = new Panel { Parent = r };
		s.Id = "special";
		r.Layout();
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.ComputedStyle.BackgroundColor.Value );

		// element match
		var e = new MySpecialPanel { Parent = r };
		r.Layout();
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), e.ComputedStyle.BackgroundColor.Value );
	}

	/// <summary>
	/// ::before / ::after are still detected after the per-block probe was gated on HasBefore/HasAfter.
	/// </summary>
	[TestMethod]
	public void BeforeAfterElementsDetected()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( ".withbefore::before { background-color: red; } .withafter::after { background-color: red; } .plain { background-color: red; }" );

		var before = r.Add.Panel( "withbefore" );
		var after = r.Add.Panel( "withafter" );
		var plain = r.Add.Panel( "plain" );
		r.Layout();

		Assert.IsTrue( before.Style.HasBeforeElement );
		Assert.IsFalse( before.Style.HasAfterElement );

		Assert.IsTrue( after.Style.HasAfterElement );
		Assert.IsFalse( after.Style.HasBeforeElement );

		Assert.IsFalse( plain.Style.HasBeforeElement );
		Assert.IsFalse( plain.Style.HasAfterElement );
	}

}

public class MySpecialPanel : Panel
{
}
