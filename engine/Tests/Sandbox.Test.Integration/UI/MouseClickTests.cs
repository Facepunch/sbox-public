using Sandbox.Engine;
using Sandbox.UI;
using NativeEngine;

namespace UITests;

[TestClass]
[DoNotParallelize]
public class MouseClickTests
{
	sealed class Recorder : Panel
	{
		public int ClickCount;
		public int DoubleClicks;
		public int TripleClicks;
		public override void CreateEvent( PanelEvent e ) => DispatchEventImmediate( e );
		protected override void OnMouseDown( MousePanelEvent e ) => ClickCount = e.ClickCount;
		protected override void OnDoubleClick( MousePanelEvent e ) => DoubleClicks++;
		protected override void OnTripleClick( MousePanelEvent e ) => TripleClicks++;
	}

	[TestMethod]
	public void NativeWindowInputForwardsOsCountAndKeepsGestureEventsOnPress()
	{
		using var surface = new UISurface();
		var panel = new Recorder { Parent = surface.Root };
		void Button( bool down, int count )
		{
			PanelWindowInput.DispatchMouseButton( surface, ButtonCode.MouseLeft, down, count, default );
			surface.Input.MouseStates[0].Update( down, panel );
			surface.System.InputEventQueue.Tick( panel, down ? panel : null );
		}
		Button( true, 1 );
		Button( false, 1 );
		Button( true, 2 );
		Assert.AreEqual( 2, panel.ClickCount );
		Assert.AreEqual( 1, panel.DoubleClicks );
		Button( false, 2 );
		Assert.AreEqual( 1, panel.DoubleClicks );
		Button( true, 1 );
		Assert.AreEqual( 1, panel.ClickCount, "The OS can reset a click sequence independently of timing." );
		Button( false, 1 );
		Button( true, 3 );
		Assert.AreEqual( 3, panel.ClickCount );
		Assert.AreEqual( 1, panel.TripleClicks );
		Button( false, 3 );
		Assert.AreEqual( 1, panel.TripleClicks );
	}
	[TestMethod]
	public void GameInputReportsPressCountButKeepsGestureEventsOnRelease()
	{
		using var surface = new UISurface();
		var panel = new Recorder { Parent = surface.Root };
		var context = new InputContext { TargetUISystem = surface.System, timeSinceClick = 1 };
		context.UpdateInputFromUI( InputContext.InputState.UI, panel, false, InputContext.InputState.UI, panel );
		void Button( bool down )
		{
			context.IN_Button( down, ButtonCode.MouseLeft, ButtonCode.MouseLeft, false, default );
			surface.Input.MouseStates[0].Update( down, panel );
			surface.System.InputEventQueue.Tick( panel, down ? panel : null );
		}
		Button( true );
		Assert.AreEqual( 1, panel.ClickCount );
		Button( false );
		context.timeSinceClick = 0;
		Button( true );
		Assert.AreEqual( 2, panel.ClickCount );
		Assert.AreEqual( 0, panel.DoubleClicks );
		Button( false );
		Assert.AreEqual( 1, panel.DoubleClicks );
		context.timeSinceClick = 0;
		Button( true );
		Assert.AreEqual( 3, panel.ClickCount );
		Assert.AreEqual( 0, panel.TripleClicks );
		Button( false );
		Assert.AreEqual( 1, panel.TripleClicks );
		context.timeSinceClick = 1;
		Button( true );
		Assert.AreEqual( 1, panel.ClickCount );
		Button( false );
		Assert.AreEqual( 1, panel.DoubleClicks );
		Assert.AreEqual( 1, panel.TripleClicks );
	}
}
