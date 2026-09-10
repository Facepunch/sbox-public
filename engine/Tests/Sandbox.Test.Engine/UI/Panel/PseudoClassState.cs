using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class PseudoClassStateTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	/// <summary>
	/// A freshly constructed panel starts with the :intro and :empty pseudo
	/// classes and nothing else.
	/// </summary>
	[TestMethod]
	public void CreationFlags()
	{
		var p = new Panel();

		Assert.AreEqual( PseudoClass.Intro | PseudoClass.Empty, p.PseudoClass );
		Assert.IsTrue( p.HasIntro );
		Assert.IsFalse( p.HasHovered );
		Assert.IsFalse( p.HasActive );
		Assert.IsFalse( p.HasFocus );
		Assert.IsFalse( p.HasOutro );
	}

	/// <summary>
	/// Switch toggles individual pseudo class flags and reports whether the
	/// flag actually changed state.
	/// </summary>
	[TestMethod]
	public void SwitchTogglesFlags()
	{
		var p = new Panel();

		Assert.IsTrue( p.Switch( PseudoClass.Hover, true ) );
		Assert.IsTrue( p.HasHovered );

		// Already on - no change reported
		Assert.IsFalse( p.Switch( PseudoClass.Hover, true ) );

		Assert.IsTrue( p.Switch( PseudoClass.Hover, false ) );
		Assert.IsFalse( p.HasHovered );

		Assert.IsTrue( p.Switch( PseudoClass.Active, true ) );
		Assert.IsTrue( p.HasActive );
	}

	/// <summary>
	/// :intro styles apply to the panel's very first layout only - the flag
	/// is switched off during that layout, so the next pass uses the normal
	/// rule again.
	/// </summary>
	[TestMethod]
	public void IntroAppliesToFirstLayoutOnly()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".fade { width: 100px; height: 50px; } .fade:intro { width: 10px; }" );

		var p = new Panel { Parent = root };
		p.AddClass( "fade" );

		Assert.IsTrue( p.HasIntro );

		root.Layout();

		// The first layout used the :intro rule, then dropped the flag
		Assert.AreEqual( 10, p.Box.Rect.Width );
		Assert.IsFalse( p.HasIntro );

		root.Layout();

		Assert.AreEqual( 100, p.Box.Rect.Width );
	}

	/// <summary>
	/// Focus() routes through InputFocus: only after the focus tick does the
	/// panel and its ancestors gain :focus, and Blur() removes it again.
	/// </summary>
	[TestMethod]
	public void FocusAndBlurDriveFocusPseudo()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new Panel { Parent = root };
		p.AcceptsFocus = true;

		Assert.IsTrue( p.Focus() );

		// Focus only swaps on the input focus tick
		Assert.IsFalse( p.HasFocus );

		InputFocus.Tick();

		Assert.IsTrue( p.HasFocus );
		Assert.IsTrue( root.HasFocus );
		Assert.AreSame( p, InputFocus.Current );

		p.Blur();
		InputFocus.Tick();

		Assert.IsFalse( p.HasFocus );
		Assert.IsFalse( root.HasFocus );
		Assert.IsNull( InputFocus.Current );
	}

	/// <summary>
	/// Focusing a panel that doesn't accept focus walks up the tree and gives
	/// focus to the nearest ancestor with AcceptsFocus instead.
	/// </summary>
	[TestMethod]
	public void FocusFallsBackToAncestor()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var holder = new Panel { Parent = root };
		holder.AcceptsFocus = true;
		var leaf = new Panel { Parent = holder };

		Assert.IsTrue( leaf.Focus() );
		InputFocus.Tick();

		Assert.AreSame( holder, InputFocus.Current );
		Assert.IsTrue( holder.HasFocus );
		Assert.IsFalse( leaf.HasFocus );

		holder.Blur();
		InputFocus.Tick();

		Assert.IsNull( InputFocus.Current );
	}

	/// <summary>
	/// A :focus stylesheet rule starts matching once the panel really has
	/// input focus and stops matching after it is blurred.
	/// </summary>
	[TestMethod]
	public void FocusSelectorMatches()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".btn { width: 100px; height: 50px; } .btn:focus { width: 250px; }" );

		var p = new Panel { Parent = root };
		p.AddClass( "btn" );
		p.AcceptsFocus = true;
		root.Layout();

		Assert.AreEqual( 100, p.Box.Rect.Width );

		p.Focus();
		InputFocus.Tick();
		root.Layout();

		Assert.AreEqual( 250, p.Box.Rect.Width );

		p.Blur();
		InputFocus.Tick();
		root.Layout();

		Assert.AreEqual( 100, p.Box.Rect.Width );
	}

	/// <summary>
	/// Deferred deletion marks the panel with :outro and IsDeleting but keeps
	/// it parented until the outro finishes; immediate deletion detaches and
	/// invalidates it without ever setting :outro.
	/// </summary>
	[TestMethod]
	public void DeleteSetsOutro()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var deferred = new Panel { Parent = root };
		deferred.Delete();

		Assert.IsTrue( deferred.IsDeleting );
		Assert.IsTrue( deferred.HasOutro );
		Assert.AreSame( root, deferred.Parent );

		var direct = new Panel { Parent = root };
		direct.Delete( true );

		Assert.IsTrue( direct.IsDeleting );
		Assert.IsFalse( direct.HasOutro );
		Assert.IsNull( direct.Parent );
		Assert.IsFalse( direct.IsValid );
	}

	/// <summary>
	/// The first-child/last-child/only-child flags are maintained eagerly as
	/// children are added, including demoting the previous last child before
	/// any layout pass runs.
	/// </summary>
	[TestMethod]
	public void SiblingPositionFlags()
	{
		var parent = new Panel();
		var a = parent.AddChild<Panel>();

		Assert.IsTrue( (a.PseudoClass & PseudoClass.FirstChild) != 0 );
		Assert.IsTrue( (a.PseudoClass & PseudoClass.LastChild) != 0 );
		Assert.IsTrue( (a.PseudoClass & PseudoClass.OnlyChild) != 0 );

		var b = parent.AddChild<Panel>();

		// a was demoted immediately, before any layout pass
		Assert.IsTrue( (a.PseudoClass & PseudoClass.FirstChild) != 0 );
		Assert.IsTrue( (a.PseudoClass & PseudoClass.LastChild) == 0 );
		Assert.IsTrue( (a.PseudoClass & PseudoClass.OnlyChild) == 0 );

		Assert.IsTrue( (b.PseudoClass & PseudoClass.FirstChild) == 0 );
		Assert.IsTrue( (b.PseudoClass & PseudoClass.LastChild) != 0 );
		Assert.IsTrue( (b.PseudoClass & PseudoClass.OnlyChild) == 0 );
	}

	/// <summary>
	/// The :empty flag clears lazily on the next layout when a child is
	/// added, but is restored synchronously when the last child is removed.
	/// </summary>
	[TestMethod]
	public void EmptyFlagTracksChildren()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var parent = new Panel { Parent = root };
		root.Layout();

		Assert.IsTrue( (parent.PseudoClass & PseudoClass.Empty) != 0 );

		var child = new Panel { Parent = parent };

		// Adding only marks the indexes dirty - the flag drops on the next layout
		Assert.IsTrue( (parent.PseudoClass & PseudoClass.Empty) != 0 );

		root.Layout();

		Assert.IsTrue( (parent.PseudoClass & PseudoClass.Empty) == 0 );

		child.Parent = null;

		Assert.IsTrue( (parent.PseudoClass & PseudoClass.Empty) != 0 );
	}
}
