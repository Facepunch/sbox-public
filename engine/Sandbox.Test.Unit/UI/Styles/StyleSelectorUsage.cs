using Sandbox.UI;

namespace UITest;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public class StyleSelectorUsage
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
}

public class MySpecialPanel : Panel
{
}
