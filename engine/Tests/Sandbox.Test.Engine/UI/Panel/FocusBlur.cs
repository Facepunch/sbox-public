using Sandbox.UI;
namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global (InputFocus lives on the UISystem)
public class PanelFocusTest
{
	/// <summary>
	/// Clears any global focus state left behind by a previous test so each test starts
	/// with InputFocus.Current == null.
	/// </summary>
	[TestInitialize]
	public void ResetFocusState()
	{
		InputFocus.Clear();
		InputFocus.Tick();
	}

	/// <summary>
	/// Clears global focus state so panels created by this test can't leak into other tests.
	/// </summary>
	[TestCleanup]
	public void ClearFocusState()
	{
		InputFocus.Clear();
		InputFocus.Tick();
	}

	/// <summary>
	/// Builds a RootPanel with screen-like bounds, matching how the real UI system hosts panels.
	/// </summary>
	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		return root;
	}

	/// <summary>
	/// AcceptsFocus defaults to false, and Focus() on a panel with no focusable ancestors
	/// fails - nothing becomes the current focus.
	/// </summary>
	[TestMethod]
	public void FocusRequiresAcceptsFocus()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root };

		Assert.IsFalse( p.AcceptsFocus );
		Assert.IsFalse( p.Focus() );

		InputFocus.Tick();

		Assert.IsFalse( p.HasFocus );
		Assert.IsNull( InputFocus.Current );
	}

	/// <summary>
	/// Focus() only queues the change - the panel becomes InputFocus.Current and gains the
	/// :focus pseudo class when InputFocus ticks, after which Next is reset to null.
	/// </summary>
	[TestMethod]
	public void FocusAppliesOnInputFocusTick()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root, AcceptsFocus = true };

		Assert.IsTrue( p.Focus() );

		// Queued but not applied yet
		Assert.IsFalse( p.HasFocus );
		Assert.IsNull( InputFocus.Current );
		Assert.AreEqual( p, InputFocus.Next );

		InputFocus.Tick();

		Assert.IsTrue( p.HasFocus );
		Assert.AreEqual( p, InputFocus.Current );
		Assert.IsNull( InputFocus.Next );
	}

	/// <summary>
	/// Focusing a second panel removes the :focus pseudo class from the previously focused
	/// panel - only one panel is the current focus at a time. The shared ancestor keeps its
	/// :focus pseudo class because it's also an ancestor of the new focus.
	/// </summary>
	[TestMethod]
	public void OnlyOneFocusedPanelAtATime()
	{
		var root = CreateRoot();
		var p1 = new Panel { Parent = root, AcceptsFocus = true };
		var p2 = new Panel { Parent = root, AcceptsFocus = true };

		p1.Focus();
		InputFocus.Tick();

		Assert.IsTrue( p1.HasFocus );
		Assert.AreEqual( p1, InputFocus.Current );

		p2.Focus();
		InputFocus.Tick();

		Assert.IsFalse( p1.HasFocus );
		Assert.IsTrue( p2.HasFocus );
		Assert.IsTrue( root.HasFocus );
		Assert.AreEqual( p2, InputFocus.Current );
	}

	/// <summary>
	/// When a panel gains focus the :focus pseudo class is applied to it and every ancestor,
	/// but only the panel itself is InputFocus.Current.
	/// </summary>
	[TestMethod]
	public void FocusPseudoAppliesToAncestors()
	{
		var root = CreateRoot();
		var parent = new Panel { Parent = root };
		var child = new Panel { Parent = parent, AcceptsFocus = true };

		child.Focus();
		InputFocus.Tick();

		Assert.IsTrue( child.HasFocus );
		Assert.IsTrue( parent.HasFocus );
		Assert.IsTrue( root.HasFocus );
		Assert.AreEqual( child, InputFocus.Current );
	}

	/// <summary>
	/// Focus() on a panel that doesn't accept focus walks up the tree and focuses the nearest
	/// ancestor with AcceptsFocus.
	/// </summary>
	[TestMethod]
	public void FocusDelegatesToFocusableAncestor()
	{
		var root = CreateRoot();
		var parent = new Panel { Parent = root, AcceptsFocus = true };
		var child = new Panel { Parent = parent };

		Assert.IsTrue( child.Focus() );
		InputFocus.Tick();

		Assert.AreEqual( parent, InputFocus.Current );
		Assert.IsTrue( parent.HasFocus );
	}

	/// <summary>
	/// Blur() on the focused panel clears the current focus and its :focus pseudo class on the
	/// next InputFocus tick when no ancestor accepts focus.
	/// </summary>
	[TestMethod]
	public void BlurClearsFocus()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root, AcceptsFocus = true };

		p.Focus();
		InputFocus.Tick();
		Assert.IsTrue( p.HasFocus );

		Assert.IsTrue( p.Blur() );
		InputFocus.Tick();

		Assert.IsFalse( p.HasFocus );
		Assert.IsNull( InputFocus.Current );
	}

	/// <summary>
	/// Blur() re-runs the focus search from the panel's parent, so blurring a focused panel
	/// whose ancestor accepts focus moves the focus to that ancestor instead of clearing it.
	/// </summary>
	[TestMethod]
	public void BlurMovesFocusToFocusableAncestor()
	{
		var root = CreateRoot();
		var parent = new Panel { Parent = root, AcceptsFocus = true };
		var child = new Panel { Parent = parent, AcceptsFocus = true };

		child.Focus();
		InputFocus.Tick();
		Assert.AreEqual( child, InputFocus.Current );

		child.Blur();
		InputFocus.Tick();

		Assert.IsFalse( child.HasFocus );
		Assert.IsTrue( parent.HasFocus );
		Assert.AreEqual( parent, InputFocus.Current );
	}

	/// <summary>
	/// Gaining and losing focus queues "onfocus" and "onblur" panel events on the affected
	/// panel, which are delivered to event listeners on the next tick.
	/// </summary>
	[TestMethod]
	public void FocusAndBlurEventsDispatched()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root, AcceptsFocus = true };

		int focusEvents = 0;
		int blurEvents = 0;
		p.AddEventListener( "onfocus", () => focusEvents++ );
		p.AddEventListener( "onblur", () => blurEvents++ );

		p.Focus();
		InputFocus.Tick();
		root.TickInternal();

		Assert.AreEqual( 1, focusEvents );
		Assert.AreEqual( 0, blurEvents );

		p.Blur();
		InputFocus.Tick();
		root.TickInternal();

		Assert.AreEqual( 1, focusEvents );
		Assert.AreEqual( 1, blurEvents );
	}

	/// <summary>
	/// The default OnEscape handler blurs the panel when it has focus, so dispatching an
	/// "onescape" event to the focused panel removes the focus on the next InputFocus tick.
	/// </summary>
	[TestMethod]
	public void EscapeBlursFocusedPanel()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root, AcceptsFocus = true };

		p.Focus();
		InputFocus.Tick();
		Assert.IsTrue( p.HasFocus );

		p.CreateEvent( "onescape" );
		root.TickInternal();
		InputFocus.Tick();

		Assert.IsFalse( p.HasFocus );
		Assert.IsNull( InputFocus.Current );
	}

	/// <summary>
	/// A queued focus target that stops accepting focus before the InputFocus tick is discarded
	/// instead of becoming the current focus.
	/// </summary>
	[TestMethod]
	public void IneligibleNextIsDiscarded()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root, AcceptsFocus = true };

		p.Focus();
		p.AcceptsFocus = false;

		InputFocus.Tick();

		Assert.IsNull( InputFocus.Current );
		Assert.IsFalse( p.HasFocus );
	}

	/// <summary>
	/// If the currently focused panel becomes ineligible (stops accepting focus), the next
	/// InputFocus tick removes the focus from it.
	/// </summary>
	[TestMethod]
	public void FocusLostWhenPanelBecomesIneligible()
	{
		var root = CreateRoot();
		var p = new Panel { Parent = root, AcceptsFocus = true };

		p.Focus();
		InputFocus.Tick();
		Assert.IsTrue( p.HasFocus );

		p.AcceptsFocus = false;
		InputFocus.Tick();

		Assert.IsFalse( p.HasFocus );
		Assert.IsNull( InputFocus.Current );
	}
}
