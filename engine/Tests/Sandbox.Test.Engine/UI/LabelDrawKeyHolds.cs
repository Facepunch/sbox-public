using Sandbox.UI;
using System.Reflection;

namespace UITests;

/// <summary>
/// A label checks its draw key in place, and the answer is the one building the key and comparing it gives: after a text
/// change, a move, a selection and a style that changes how the glyphs are drawn.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LabelDrawKeyHoldsTest
{
	bool _wasMainThread;

	[TestInitialize]
	public void Begin()
	{
		_wasMainThread = ThreadSafe.IsMainThread;
		ThreadSafe.MarkMainThread();
	}

	[TestCleanup]
	public void Cleanup()
	{
		typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, _wasMainThread );
	}

	static void Frame( RootPanel root )
	{
		root.Layout();
		root.BuildCommandList();
	}

	/// <summary>
	/// The label's key as it was, which holds now exactly when building it again gives the same key.
	/// </summary>
	static void AssertAgrees( Label label, in Panel.DrawKey before, bool expectHolds )
	{
		var built = label.TryGetDrawKey( out var now ) && now == before;
		Assert.AreEqual( built, label.DrawKeyHolds( in before ), "in place and built must agree" );
		Assert.AreEqual( expectHolds, built );
	}

	static (RootPanel Root, Label Label, Panel.DrawKey Key) Labelled()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 800, 600 ) };
		var label = root.AddChild<Label>();
		label.Text = "hello world";
		label.Style.Set( "font-size: 20px; margin-left: 10px;" );
		for ( int i = 0; i < 3; i++ ) Frame( root );
		Assert.IsTrue( label.TryGetDrawKey( out var key ) );
		Assert.IsNotNull( key.Source, "the label draws its text block" );
		return (root, label, key);
	}

	[TestMethod]
	public void UnchangedHolds()
	{
		var (root, label, key) = Labelled();
		Frame( root );
		AssertAgrees( label, key, true );
	}

	[TestMethod]
	public void NewTextDoesNotHold()
	{
		var (root, label, key) = Labelled();
		label.Text = "goodbye";
		Frame( root );
		AssertAgrees( label, key, false );
	}

	[TestMethod]
	public void MovedDoesNotHold()
	{
		var (root, label, key) = Labelled();
		label.Style.Set( "margin-left: 40px;" );
		Frame( root );
		AssertAgrees( label, key, false );
	}

	[TestMethod]
	public void SelectionDoesNotHold()
	{
		var (root, label, key) = Labelled();
		label.ShouldDrawSelection = true;
		label.SelectionStart = 1;
		label.SelectionEnd = 4;
		Frame( root );
		AssertAgrees( label, key, false );
	}

	[TestMethod]
	public void AliasedDoesNotHold()
	{
		var (root, label, key) = Labelled();
		label.Style.Set( "font-smooth: never;" );
		Frame( root );
		AssertAgrees( label, key, false );
	}

	[TestMethod]
	public void AnotherLabelsKeyDoesNotHold()
	{
		var (_, label, _) = Labelled();
		var (_, _, other) = Labelled();
		AssertAgrees( label, other, false );
	}
}
