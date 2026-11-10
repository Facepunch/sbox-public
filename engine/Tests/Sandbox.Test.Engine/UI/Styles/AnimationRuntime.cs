using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + the shared panel clock
public class AnimationRuntimeTest
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
		Assert.IsTrue( styles.ApplyAnimation( r ) );      // start the animation - mid-animation applies report a change

		PanelRealTime.TimeNow = 1000;    // far past the 1s duration
		Assert.IsTrue( styles.ApplyAnimation( r ) );      // first apply after finishing holds the end frame (forwards) and reports one last change

		// The animation is over - it must report no further change instead of animating forever
		Assert.IsFalse( styles.ApplyAnimation( r ) );
	}
}
