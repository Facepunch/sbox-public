using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class PanelClassListTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// SetClass accepts strings with multiple space separated tokens and adds
	/// or removes every token at once.
	/// </summary>
	[TestMethod]
	public void SetClassMultipleTokens()
	{
		var p = new Panel();

		p.SetClass( "one two", true );

		Assert.AreEqual( 2, p.Class.Count() );
		Assert.IsTrue( p.HasClass( "one" ) );
		Assert.IsTrue( p.HasClass( "two" ) );

		p.SetClass( "one two", false );

		Assert.AreEqual( 0, p.Class.Count() );
	}

	/// <summary>
	/// ToggleClass adds the class when it is missing and removes it when it
	/// is present.
	/// </summary>
	[TestMethod]
	public void ToggleClassFlips()
	{
		var p = new Panel();

		p.ToggleClass( "flip" );
		Assert.IsTrue( p.HasClass( "flip" ) );

		p.ToggleClass( "flip" );
		Assert.IsFalse( p.HasClass( "flip" ) );

		p.ToggleClass( "flip" );
		Assert.IsTrue( p.HasClass( "flip" ) );
	}

	/// <summary>
	/// FlashClass applies the class synchronously - the timed removal only
	/// happens once the requested duration has really elapsed.
	/// </summary>
	[TestMethod]
	public void FlashClassAddsImmediately()
	{
		var p = new Panel();

		// Far enough in the future (~23 days, still below the int milliseconds
		// limit) that the scheduled removal can never fire while the test host lives
		p.FlashClass( "boom", 2000000.0f );

		Assert.IsTrue( p.HasClass( "boom" ) );
	}

	/// <summary>
	/// Class names are stored lowercased and compared case-insensitively, so
	/// HasClass matches regardless of the casing used by the caller.
	/// </summary>
	[TestMethod]
	public void ClassNamesAreCaseInsensitive()
	{
		var p = new Panel();

		p.AddClass( "MiXeD" );

		Assert.AreEqual( "mixed", p.Class.First() );
		Assert.IsTrue( p.HasClass( "mixed" ) );
		Assert.IsTrue( p.HasClass( "MIXED" ) );
	}

	/// <summary>
	/// The Classes string property replaces the whole class list when set and
	/// reflects the current list when read.
	/// </summary>
	[TestMethod]
	public void ClassesStringReplacesAll()
	{
		var p = new Panel();

		p.Classes = "one two";

		Assert.AreEqual( 2, p.Class.Count() );
		Assert.IsTrue( p.HasClass( "one" ) );
		Assert.IsTrue( p.HasClass( "two" ) );

		p.Classes = "three";

		Assert.AreEqual( 1, p.Class.Count() );
		Assert.IsFalse( p.HasClass( "one" ) );
		Assert.IsFalse( p.HasClass( "two" ) );
		Assert.AreEqual( "three", p.Classes );
	}

	/// <summary>
	/// RemoveClass and HasClass are safe no-ops for panels without any
	/// classes and for null or whitespace class names.
	/// </summary>
	[TestMethod]
	public void RemoveClassToleratesMissing()
	{
		var p = new Panel();

		p.RemoveClass( "never-added" );
		p.RemoveClass( null );
		p.RemoveClass( "  " );

		Assert.IsFalse( p.HasClass( null ) );
		Assert.IsFalse( p.HasClass( " " ) );
		Assert.AreEqual( 0, p.Class.Count() );
	}

	/// <summary>
	/// Adding or removing a class dirties the style selectors, so the next
	/// layout recomputes the computed style from the now-matching rules.
	/// </summary>
	[TestMethod]
	public void ClassChangeUpdatesComputedStyle()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".a { width: 100px; height: 50px; } .a.wide { width: 200px; }" );

		var p = new Panel { Parent = root };
		p.AddClass( "a" );
		root.Layout();

		Assert.AreEqual( 100, p.Box.Rect.Width );

		p.AddClass( "wide" );
		root.Layout();

		Assert.AreEqual( 200, p.Box.Rect.Width );
		Assert.AreEqual( 200, p.ComputedStyle.Width.Value.Value );

		p.RemoveClass( "wide" );
		root.Layout();

		Assert.AreEqual( 100, p.Box.Rect.Width );
	}

	/// <summary>
	/// BindClass evaluates the bound function every tick and switches the
	/// class on or off to mirror its result.
	/// </summary>
	[TestMethod]
	public void BindClassFollowsFunction()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = root };
		bool active = true;
		p.BindClass( "on", () => active );

		root.Layout();
		Assert.IsTrue( p.HasClass( "on" ) );

		active = false;
		root.Layout();
		Assert.IsFalse( p.HasClass( "on" ) );
	}
}
