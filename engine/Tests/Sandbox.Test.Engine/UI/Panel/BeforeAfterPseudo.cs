using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public partial class BeforeAfterPseudoTest
{
	/// <summary>
	/// Matching ::before / ::after rules flag the panel's style during rule building, and the
	/// next tick generates the pseudo-element children: both are labels named "element", with
	/// the ::before kept first and the ::after kept last, and the rules' styles apply to them.
	/// </summary>
	[TestMethod]
	public void PseudoElementsCreatedFromRules()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".decorated::before { width: 10px; height: 10px; } .decorated::after { width: 20px; height: 20px; }" );

		var p = root.Add.Panel( "decorated" );
		var middle = p.Add.Panel();

		// The first layout builds the style rules - the pseudo elements themselves are
		// only created by the tick of the following layout.
		root.Layout();

		Assert.IsTrue( p.Style.HasBeforeElement );
		Assert.IsTrue( p.Style.HasAfterElement );
		Assert.AreEqual( 1, p.Children.Count() );

		root.Layout();

		Assert.AreEqual( 3, p.Children.Count() );

		var before = p.Children.First();
		var after = p.Children.Last();

		Assert.IsInstanceOfType( before, typeof( Label ) );
		Assert.IsInstanceOfType( after, typeof( Label ) );

		Assert.AreEqual( "element", before.ElementName );
		Assert.AreEqual( "element", after.ElementName );
		Assert.IsTrue( before.PseudoClass.HasFlag( PseudoClass.Before ) );
		Assert.IsTrue( after.PseudoClass.HasFlag( PseudoClass.After ) );

		// The regular child is sandwiched between the two pseudo elements
		Assert.AreEqual( middle, p.Children.ElementAt( 1 ) );

		// The ::before / ::after rule styles cascade onto the generated elements
		Assert.AreEqual( 10f, before.ComputedStyle.Width.Value.Value, 0.001f );
		Assert.AreEqual( 20f, after.ComputedStyle.Width.Value.Value, 0.001f );
	}

	/// <summary>
	/// The content property declared on a ::before rule ends up on the generated pseudo
	/// element's computed style, with the CSS quotes stripped.
	/// </summary>
	[TestMethod]
	public void ContentPropertyAppliesToPseudoElement()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		// display: none keeps the generated label from building a text texture during
		// layout, which this test tier must never do.
		root.StyleSheet.Parse( ".tagged::before { content: 'pre'; display: none; }" );

		var p = root.Add.Panel( "tagged" );
		root.Layout();
		root.Layout();

		Assert.AreEqual( 1, p.Children.Count() );

		var before = p.Children.First();
		Assert.AreEqual( "pre", before.ComputedStyle.Content );
		Assert.IsFalse( before.IsVisible );
	}

	/// <summary>
	/// Removing the class that matched the ::before / ::after rules turns the style flags off,
	/// after which the next tick deletes the pseudo elements via deferred deletion.
	/// </summary>
	[TestMethod]
	public void RemovingClassDestroysPseudoElements()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".decorated::before { width: 10px; height: 10px; } .decorated::after { width: 20px; height: 20px; }" );

		var p = root.Add.Panel( "decorated" );
		root.Layout();
		root.Layout();

		Assert.AreEqual( 2, p.Children.Count() );
		var before = p.Children.First();

		p.RemoveClass( "decorated" );
		root.Layout(); // rules rebuild - the pseudo flags turn off

		Assert.IsFalse( p.Style.HasBeforeElement );
		Assert.IsFalse( p.Style.HasAfterElement );

		root.Layout(); // the tick notices and queues the pseudo elements for deletion

		Assert.IsTrue( before.IsDeleting );

		GlobalContext.Current.UISystem.RunDeferredDeletion();

		Assert.AreEqual( 0, p.Children.Count() );
		Assert.IsFalse( before.IsValid );
	}
}
