using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + the shared panel clock
public partial class PanelTransitionTest
{
	/// <summary>
	/// Resets the panel clock, then builds a root with the given stylesheet and a single panel
	/// with class "box", laid out once at time zero so the initial styles apply untransitioned.
	/// </summary>
	static (RootPanel Root, Panel Target) Create( string stylesheet )
	{
		PanelRealTime.TimeNow = 0;
		PanelRealTime.TimeDelta = 0;

		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( stylesheet );

		var target = root.Add.Panel( "box" );
		root.Layout();

		return (root, target);
	}

	/// <summary>
	/// An opacity transition triggered by a class change holds the old value at t=0, reports
	/// HasActiveTransitions and lerps linearly while running, then settles exactly on the new
	/// value with no active transitions once the duration has elapsed.
	/// </summary>
	[TestMethod]
	public void OpacityTransitionRunsOverTime()
	{
		var (root, target) = Create( ".box { transition: opacity 1s linear; opacity: 1; width: 50px; height: 50px; } .box.faded { opacity: 0; }" );

		Assert.IsFalse( target.HasActiveTransitions );
		Assert.AreEqual( 1, target.ComputedStyle.Opacity.Value, 0.001f );

		target.AddClass( "faded" );
		root.Layout();

		Assert.IsTrue( target.HasActiveTransitions );
		Assert.AreEqual( 1, target.ComputedStyle.Opacity.Value, 0.001f );

		PanelRealTime.TimeNow = 0.5;
		root.Layout();

		Assert.IsTrue( target.HasActiveTransitions );
		Assert.AreEqual( 0.5f, target.ComputedStyle.Opacity.Value, 0.01f );

		PanelRealTime.TimeNow = 2;
		root.Layout();

		Assert.IsFalse( target.HasActiveTransitions );
		Assert.AreEqual( 0, target.ComputedStyle.Opacity.Value, 0.001f );
	}

	/// <summary>
	/// Moving and fading a panel preserves its layout, updates its children's hit-test transforms
	/// and applies both ordinary and explicitly inherited opacity.
	/// </summary>
	[TestMethod]
	public void PaintTransitionKeepsLayoutAndUpdatesDescendants()
	{
		var (root, target) = Create( """
			.box { width: 100px; height: 50px; opacity: 1; transform: translateX(0px); transition: opacity 1s linear, transform 1s linear; }
			.box.moved { opacity: 0.2; transform: translateX(100px); }
			""" );
		var child = target.Add.Panel();
		child.Style.Opacity = 0.5f;
		var grandchild = child.Add.Panel();
		var inherits = target.Add.Panel();
		inherits.Style.Set( "opacity", "inherit" );
		root.Layout();

		target.AddClass( "moved" );
		root.Layout();
		var updates = target.LayoutTree.StyleUpdateCount;
		var originalBox = target.Box.Rect;

		PanelRealTime.TimeNow = 0.5;
		root.Layout();

		Assert.AreEqual( updates, target.LayoutTree.StyleUpdateCount );
		Assert.AreEqual( originalBox, target.Box.Rect );
		Assert.AreEqual( 0.3f, grandchild.Opacity, 0.001f );
		Assert.AreEqual( 0.36f, inherits.Opacity, 0.001f );
		Assert.AreEqual( -50f, grandchild.GlobalMatrix.Value.Transform( Vector3.Zero ).x, 0.01f );

		PanelRealTime.TimeNow = 2;
		root.Layout();

		Assert.AreEqual( updates, target.LayoutTree.StyleUpdateCount );
		Assert.AreEqual( 0.1f, grandchild.Opacity, 0.001f );
		Assert.AreEqual( 0.04f, inherits.Opacity, 0.001f );
		Assert.AreEqual( -100f, grandchild.GlobalMatrix.Value.Transform( Vector3.Zero ).x, 0.01f );
	}

	/// <summary>
	/// A width transition is fed through layout every frame, so halfway through the transition
	/// the laid-out box is halfway between the old and new widths.
	/// </summary>
	[TestMethod]
	public void WidthTransitionAffectsLayout()
	{
		var (root, target) = Create( ".box { transition: width 1s linear; width: 100px; height: 50px; } .box.wide { width: 300px; }" );

		Assert.AreEqual( 100, target.Box.Rect.Width, 0.5f );

		target.AddClass( "wide" );
		root.Layout();

		Assert.IsTrue( target.HasActiveTransitions );

		PanelRealTime.TimeNow = 0.5;
		root.Layout();

		Assert.AreEqual( 200, target.ComputedStyle.Width.Value.Value, 0.5f );
		Assert.AreEqual( 200, target.Box.Rect.Width, 0.5f );

		PanelRealTime.TimeNow = 2;
		root.Layout();

		Assert.IsFalse( target.HasActiveTransitions );
		Assert.AreEqual( 300, target.Box.Rect.Width, 0.5f );
	}

	/// <summary>
	/// A left transition on an absolutely positioned panel moves its box across the screen,
	/// passing the midpoint at half the duration and landing on the final position at the end.
	/// </summary>
	[TestMethod]
	public void LeftTransitionMovesBox()
	{
		var (root, target) = Create( ".box { transition: left 1s linear; position: absolute; left: 0px; top: 0px; width: 50px; height: 50px; } .box.moved { left: 100px; }" );

		Assert.AreEqual( 0, target.Box.Left, 0.5f );

		target.AddClass( "moved" );
		root.Layout();

		PanelRealTime.TimeNow = 0.5;
		root.Layout();

		Assert.IsTrue( target.HasActiveTransitions );
		Assert.AreEqual( 50, target.ComputedStyle.Left.Value.Value, 0.5f );
		Assert.AreEqual( 50, target.Box.Left, 0.5f );

		PanelRealTime.TimeNow = 2;
		root.Layout();

		Assert.IsFalse( target.HasActiveTransitions );
		Assert.AreEqual( 100, target.Box.Left, 0.5f );
	}

	/// <summary>
	/// SkipTransitions on a panel mid-transition jumps the property straight to its end value
	/// on the next layout and removes the active transition.
	/// </summary>
	[TestMethod]
	public void SkipJumpsToEndOfTransition()
	{
		var (root, target) = Create( ".box { transition: opacity 1s linear; opacity: 1; width: 50px; height: 50px; } .box.faded { opacity: 0; }" );

		target.AddClass( "faded" );
		root.Layout();

		Assert.IsTrue( target.HasActiveTransitions );

		PanelRealTime.TimeNow = 0.25;
		root.Layout();

		Assert.AreEqual( 0.75f, target.ComputedStyle.Opacity.Value, 0.01f );

		target.SkipTransitions();
		root.Layout();

		Assert.IsFalse( target.HasActiveTransitions );
		Assert.AreEqual( 0, target.ComputedStyle.Opacity.Value, 0.001f );
	}

	/// <summary>
	/// A transition keeps a fading panel visible, but not one switched to display:none - it has
	/// no layout, so drawing it would squash its content into a zero-sized box.
	/// </summary>
	[TestMethod]
	public void DisplayNoneHidesPanelWithRunningTransition()
	{
		var (root, target) = Create( ".box { transition: color 1s linear; color: white; width: 50px; height: 50px; } .box.gone { display: none; color: red; }" );

		target.AddClass( "gone" );
		root.Layout();

		Assert.IsTrue( target.HasActiveTransitions );
		Assert.IsFalse( target.IsVisible );
	}
}
