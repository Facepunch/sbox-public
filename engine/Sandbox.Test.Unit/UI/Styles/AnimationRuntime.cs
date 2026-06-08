using Sandbox.UI;

namespace UITest;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + the shared panel clock
public class AnimationRuntime
{
	/// <summary>
	/// A finished finite animation should stop reporting changes, not re-run keyframes forever.
	/// </summary>
	[TestMethod]
	public void FinishedAnimationSettles()
	{
		var r = new RootPanel();
		r.StyleSheet.Parse( "@keyframes fade { 0% { opacity: 0; } 100% { opacity: 1; } }" );

		var styles = new Styles();
		Assert.IsTrue( styles.Set( "animation", "fade 1s 1 forwards" ) );

		PanelRealTime.TimeNow = 0;
		styles.ApplyAnimation( r );      // start the animation

		PanelRealTime.TimeNow = 1000;    // far past the 1s duration
		styles.ApplyAnimation( r );      // first apply after finishing (holds the end frame)

		// The animation is over - it must report no further change instead of animating forever
		Assert.IsFalse( styles.ApplyAnimation( r ) );
	}
}
