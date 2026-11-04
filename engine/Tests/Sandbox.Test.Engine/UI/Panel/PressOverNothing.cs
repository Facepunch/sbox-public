using Sandbox.UI;
using static UITests.UiTesting;

namespace UITests.Panels;

/// <summary>
/// A press where there's no panel at all still registers - that press is what closes an open
/// popup when you click on nothing.
/// </summary>
[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class PressOverNothingTest
{
	[TestMethod]
	public void PressingOverNothingStillCountsAsAPress()
	{
		using var surface = new UISurface { Size = new Vector2( 800, 600 ), MouseInside = true };
		surface.Root.Style.PointerEvents = PointerEvents.None;
		Frame( surface );

		surface.MousePosition = new Vector2( 700, 500 );
		HoldLeftButton( surface, true );
		Frame( surface );

		Assert.IsNull( surface.Hovered );
		Assert.IsTrue( surface.Input.MouseStates[0].Pressed );

		HoldLeftButton( surface, false );
		Frame( surface );

		Assert.IsFalse( surface.Input.MouseStates[0].Pressed );
	}

	/// <summary>
	/// Sets the raw button state the input tick reads. SetMouseButton would do it, but it also
	/// queues a button event that asks the native input system for a virtual key, and there's
	/// no native input system here.
	/// </summary>
	static void HoldLeftButton( UISurface surface, bool down )
	{
		surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, down, default );
	}
}
