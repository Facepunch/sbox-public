using Sandbox.UI;

namespace UITests.Panels;

/// <summary>
/// Panel.StyleParent: a panel can be styled under a panel other than the one it's laid out in -
/// that panel's stylesheets apply, its selectors see the panel as a descendant, and its font and
/// colour are inherited. Layout still follows the real parent.
/// </summary>
[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class StyleParentTest
{
	/// <summary>
	/// A panel that can be told what to style itself under.
	/// </summary>
	sealed class Floater : Panel
	{
		public Panel StyledUnder;
		internal override Panel StyleParent => StyledUnder ?? Parent;
	}

	static RootPanel CreateRoot( string sheet = null )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		if ( sheet is not null ) root.StyleSheet.Parse( sheet );
		return root;
	}

	static void Layout( RootPanel root )
	{
		root.Layout();
		root.Layout();
	}

	[TestMethod]
	public void DefaultsToTheParent()
	{
		var root = CreateRoot();
		var panel = new Panel { Parent = root };

		Assert.AreEqual( root, panel.StyleParent );
	}

	[TestMethod]
	public void DescendantSelectorMatchesThroughTheStyleParent()
	{
		var root = CreateRoot( ".host .thing { width: 50px; height: 10px; }" );
		var host = root.Add.Panel( "host" );

		var plain = new Floater { Parent = root };
		plain.AddClass( "thing" );

		var hosted = new Floater { Parent = root, StyledUnder = host };
		hosted.AddClass( "thing" );

		Layout( root );

		Assert.AreEqual( 0, plain.Box.Rect.Width, 0.5f, "a sibling of the host doesn't match" );
		Assert.AreEqual( 50, hosted.Box.Rect.Width, 0.5f, "matched as a descendant of the host" );
	}

	[TestMethod]
	public void ChildCombinatorMatchesThroughTheStyleParent()
	{
		var root = CreateRoot( ".host > .thing { width: 50px; height: 10px; }" );
		var host = root.Add.Panel( "host" );
		var floater = new Floater { Parent = root, StyledUnder = host };
		floater.AddClass( "thing" );

		Layout( root );

		Assert.AreEqual( 50, floater.Box.Rect.Width, 0.5f );
	}

	[TestMethod]
	public void InheritsFontAndColorFromTheStyleParent()
	{
		var root = CreateRoot();
		var host = root.Add.Panel( "host" );
		host.Style.FontSize = 30;
		host.Style.FontColor = Color.Red;

		var floater = new Floater { Parent = root, StyledUnder = host };
		Layout( root );

		Assert.AreEqual( 30, floater.ComputedStyle.FontSize?.Value );
		Assert.AreEqual( Color.Red, floater.ComputedStyle.FontColor );
	}

	[TestMethod]
	public void StyleParentsOwnSheetsApply()
	{
		var root = CreateRoot();
		var host = root.Add.Panel( "host" );
		host.StyleSheet.Parse( ".thing { width: 33px; height: 10px; }" );

		var floater = new Floater { Parent = root, StyledUnder = host };
		floater.AddClass( "thing" );
		Layout( root );

		Assert.AreEqual( 33, floater.Box.Rect.Width, 0.5f );
		Assert.IsTrue( floater.AllStyleSheets.Contains( host.StyleSheet.List[0] ) );
	}

	[TestMethod]
	public void LayoutStaysWithTheRealParent()
	{
		var root = CreateRoot();
		var host = root.Add.Panel( "host" );
		host.Style.Width = 100;
		host.Style.Height = 100;

		var floater = new Floater { Parent = root, StyledUnder = host };
		floater.Style.Width = 20;
		floater.Style.Height = 20;
		Layout( root );

		// Laid out as the root's second child in a row, not inside the host
		Assert.AreEqual( 100, floater.Box.Rect.Left, 0.5f );
		Assert.AreEqual( 0, floater.Box.Rect.Top, 0.5f );
	}

	[TestMethod]
	public void WorksAcrossRoots()
	{
		var a = CreateRoot( ".host .thing { width: 50px; height: 10px; }" );
		var host = a.Add.Panel( "host" );
		host.Style.FontColor = Color.Red;
		Layout( a );

		var b = CreateRoot();
		var floater = new Floater { Parent = b, StyledUnder = host };
		floater.AddClass( "thing" );
		Layout( b );

		Assert.AreEqual( 50, floater.Box.Rect.Width, 0.5f, "rule from the other root's sheet" );
		Assert.AreEqual( Color.Red, floater.ComputedStyle.FontColor, "inherited from the other root" );
	}

	[TestMethod]
	public void ChildrenOfTheFloaterFollowIt()
	{
		var root = CreateRoot( ".host .inner { width: 40px; height: 10px; }" );
		var host = root.Add.Panel( "host" );
		var floater = new Floater { Parent = root, StyledUnder = host };
		var inner = floater.Add.Panel( "inner" );

		Layout( root );

		Assert.AreEqual( 40, inner.Box.Rect.Width, 0.5f );
	}
}
