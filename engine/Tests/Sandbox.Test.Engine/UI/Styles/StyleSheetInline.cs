using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class StyleSheetInlineTest
{
	[TestCleanup]
	public void Cleanup()
	{
		// Runs even when an assert throws, so a failure doesn't leak roots into the next test
		Sandbox.Engine.GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// Inline sheets are parsed once and shared - the same key returns the same instance,
	/// and only a content change causes a reparse.
	/// </summary>
	[TestMethod]
	public void CachedAndReparsedOnChange()
	{
		var sheet = StyleSheet.FromInline( ".thing { margin-top: 10px; }", "inline:UITests.CacheTest" );

		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".thing" ) ) );

		var again = StyleSheet.FromInline( ".thing { margin-top: 10px; }", "inline:UITests.CacheTest" );
		Assert.AreSame( sheet, again );

		// changed content reparses in place, keeping the shared instance
		var changed = StyleSheet.FromInline( ".other { margin-top: 20px; }", "inline:UITests.CacheTest" );
		Assert.AreSame( sheet, changed );
		Assert.IsTrue( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".other" ) ) );
		Assert.IsFalse( sheet.Nodes.Any( n => n.SelectorStrings.Contains( ".thing" ) ) );
	}

	/// <summary>
	/// [StyleSheet.Inline] carries the sheet name and content.
	/// </summary>
	[TestMethod]
	public void InlineAttribute()
	{
		var attr = new StyleSheet.InlineAttribute( "test", ".x { color: red; }" );
		Assert.AreEqual( "test", attr.Name );
		Assert.AreEqual( ".x { color: red; }", attr.Styles );
	}

	/// <summary>
	/// NavigationHost hides the page it navigated away from with an inline rule, so it stands in
	/// here for any control that styles itself this way.
	/// </summary>
	[TestMethod]
	public void DeclaringTypeGetsItsOwnStyles()
	{
		AssertPageHides( new Sandbox.UI.Navigation.NavigationHost() );
	}

	class DerivedNavigationHost : Sandbox.UI.Navigation.NavigationHost { }

	/// <summary>
	/// Every navigator that gets used is a subclass - a settings page, a package browser - so
	/// styles that stop at the type declaring them leave all of them with none.
	/// </summary>
	[TestMethod]
	public void DerivedTypeInheritsTheStyles()
	{
		AssertPageHides( new DerivedNavigationHost() );
	}

	class DeeperNavigationHost : DerivedNavigationHost { }

	/// <summary>
	/// Two levels down. DropDown : PopupButton : Button is a real engine chain of this shape.
	/// </summary>
	[TestMethod]
	public void TwiceDerivedTypeInheritsTheStyles()
	{
		AssertPageHides( new DeeperNavigationHost() );
	}

	/// <summary>
	/// DropDown : PopupButton : Button - a real chain with the styles two levels up and nothing in
	/// between. Asking reflection for an inherited attribute only ever reaches the immediate base,
	/// which leaves the grandparent's styles behind without anything looking broken at the surface.
	/// </summary>
	[TestMethod]
	public void StylesComeFromTheWholeBaseChain()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var dropdown = new DropDown { Parent = root };

		var sheets = dropdown.AllStyleSheets.Select( x => x.FileName ).ToArray();

		CollectionAssert.Contains( sheets, "inline:dropdown", "its own" );
		CollectionAssert.Contains( sheets, "inline:button", "its grandparent's" );
	}

	/// <summary>
	/// A control that declares its own inline styles has said where its styles come from, so it
	/// shouldn't also go looking for the .scss that used to sit beside it.
	/// </summary>
	[TestMethod]
	public void DeclaringItsOwnStylesStopsTheFileLookup()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var button = new Button { Parent = root };

		var own = button.StyleSheet.List?.Select( x => x.FileName ).ToArray() ?? [];

		Assert.IsTrue( own.All( x => x.StartsWith( "inline:" ) ),
			$"expected only inline sheets, got: {string.Join( ", ", own )}" );
	}

	static void AssertPageHides( Panel host )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		host.Parent = root;

		var showing = new Panel { Parent = host };
		showing.AddClass( "navigator-body" );

		var hidden = new Panel { Parent = host };
		hidden.AddClass( "navigator-body" );
		hidden.AddClass( "hidden" );

		root.Layout();

		Assert.AreNotEqual( DisplayMode.None, showing.ComputedStyle?.Display, $"the page being shown in {host.GetType().Name}" );
		Assert.AreEqual( DisplayMode.None, hidden.ComputedStyle?.Display, $"the page navigated away from in {host.GetType().Name}" );
	}
}
