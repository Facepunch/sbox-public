using NativeEngine;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Cross-window drag input routing and pointer capture handoff.
/// </summary>
[TestClass]
[DoNotParallelize] // The window registry and drag session are global
public class PanelWindowDragTests
{
	sealed class FakeDragSession : IPanelWindowDragSession
	{
		internal Action FrameAction;
		internal Action CancelAction;
		internal Func<IntPtr, ButtonCode, bool, bool> MouseButton;
		internal Func<IntPtr, ButtonCode, bool, bool> Key;
		internal Action<IntPtr, bool> Focus;
		internal Action<IPanelWindow> WindowClosing;
		internal int Frames;
		internal int Cancellations;

		void IPanelWindowDragSession.Frame()
		{
			Frames++;
			FrameAction?.Invoke();
		}

		void IPanelWindowDragSession.Cancel()
		{
			Cancellations++;
			CancelAction?.Invoke();
		}

		bool IPanelWindowDragSession.OnMouseButton( IntPtr window, ButtonCode button, bool down ) => MouseButton?.Invoke( window, button, down ) ?? false;
		bool IPanelWindowDragSession.OnKey( IntPtr window, ButtonCode button, bool down ) => Key?.Invoke( window, button, down ) ?? false;
		void IPanelWindowDragSession.OnFocus( IntPtr window, bool focused ) => Focus?.Invoke( window, focused );
		void IPanelWindowDragSession.OnWindowClosing( IPanelWindow window ) => WindowClosing?.Invoke( window );
	}

	sealed class RecordingPanel : Panel
	{
		internal readonly List<string> Events = new();
		internal string LastKey;

		/// <inheritdoc/>
		public override bool WantsDrag => true;

		/// <inheritdoc/>
		public override void CreateEvent( PanelEvent evnt )
		{
			Events.Add( evnt.Name );
			base.CreateEvent( evnt );
		}

		/// <inheritdoc/>
		public override void OnButtonTyped( ButtonEvent evnt ) => LastKey = evnt.Button;
	}

	IPanelWindowDragSession previousSession;
	FakeDragSession session;
	FakePanelWindow main, hovered, ignored, popup;

	/// <summary>Register windows without native surfaces or input mapping.</summary>
	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		previousSession = PanelWindows.DragSession;
		PanelWindows.DragSession = session = new FakeDragSession();
		main = new FakePanelWindow { Handle = 1 };
		hovered = new FakePanelWindow { Handle = 2, MouseInside = true };
		ignored = new FakePanelWindow { Handle = 3, IgnoresInput = true };
		popup = new FakePanelWindow { Handle = 4, IsPopup = true, Parent = main };
		foreach ( var window in new[] { main, hovered, ignored, popup } ) PanelWindows.Register( window );
	}

	/// <summary>Restore the global session and remove only these test windows.</summary>
	[TestCleanup]
	public void Cleanup()
	{
		PanelWindows.DragSession = null;
		foreach ( var window in new[] { main, hovered, ignored, popup } ) PanelWindows.Unregister( window );
		PanelWindows.DragSession = previousSession;
	}

	/// <summary>The raw event reaches the session before hover, ignored-input and popup routing.</summary>
	[TestMethod]
	[DataRow( 1, true )]
	[DataRow( 1, false )]
	[DataRow( 3, true )]
	[DataRow( 3, false )]
	[DataRow( 99, false )]
	public void ConsumedMouseButtonBypassesWindowRouting( int handle, bool down )
	{
		var calls = 0;
		session.MouseButton = ( window, button, pressed ) =>
		{
			Assert.AreEqual( (IntPtr)handle, window );
			Assert.AreEqual( ButtonCode.MouseLeft, button );
			Assert.AreEqual( down, pressed );
			calls++;
			return true;
		};

		// Null surfaces deliberately fail if the event leaks into ordinary routing.
		PanelWindowInput.OnMouseButton( (IntPtr)handle, ButtonCode.MouseLeft, down, 1, 0 );

		Assert.AreEqual( 1, calls );
		Assert.AreEqual( 0, session.Frames );
		Assert.IsFalse( popup.CloseRequested );
	}

	/// <summary>A session can decline input without changing the normal ignored-window behavior.</summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void UnconsumedInputStillDismissesPopups( bool armed )
	{
		if ( !armed ) PanelWindows.DragSession = null;

		PanelWindowInput.OnMouseButton( ignored.Handle, ButtonCode.MouseLeft, true, 1, 0 );

		Assert.IsTrue( popup.CloseRequested );
		Assert.AreEqual( 0, session.Frames );
	}

	/// <summary>Escape belongs to the session even with an open popup or an unknown source window.</summary>
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 99 )]
	public void EscapeCancelsWithoutReachingPopupInput( int handle )
	{
		var calls = 0;
		session.Key = ( window, button, down ) =>
		{
			Assert.AreEqual( (IntPtr)handle, window );
			Assert.AreEqual( ButtonCode.KEY_ESCAPE, button );
			calls++;
			if ( down ) ((IPanelWindowDragSession)session).Cancel();
			return true;
		};

		PanelWindowInput.OnKey( (IntPtr)handle, ButtonCode.KEY_ESCAPE, true, 0 );
		PanelWindowInput.OnKey( (IntPtr)handle, ButtonCode.KEY_ESCAPE, false, 0 );

		Assert.AreEqual( 2, calls );
		Assert.AreEqual( 1, session.Cancellations );
		Assert.AreEqual( 0, session.Frames );
		Assert.IsFalse( popup.CloseRequested );
	}

	/// <summary>Focus notification precedes the ordinary loss-of-focus cleanup.</summary>
	[TestMethod]
	public void FocusNotifiesSessionBeforeOrdinaryRouting()
	{
		main.MouseInside = true;
		var calls = 0;
		session.Focus = ( window, focused ) =>
		{
			Assert.AreEqual( main.Handle, window );
			Assert.IsFalse( focused );
			Assert.IsTrue( main.MouseInside );
			Assert.IsFalse( popup.CloseRequested );
			calls++;
		};

		PanelWindowInput.OnFocus( main.Handle, false );

		Assert.AreEqual( 1, calls );
		Assert.IsFalse( main.MouseInside );
		Assert.IsTrue( popup.CloseRequested );
		Assert.AreEqual( 0, session.Frames );
	}

	/// <summary>Closing a window notifies the current session without detaching it first.</summary>
	[TestMethod]
	public void UnregisterNotifiesCurrentSession()
	{
		var replacement = new FakeDragSession();
		var calls = 0;
		session.WindowClosing = _ => Assert.Fail( "The replaced session must not receive closing events." );
		replacement.WindowClosing = window =>
		{
			Assert.AreSame( main, window );
			Assert.AreSame( replacement, PanelWindows.DragSession );
			Assert.IsNull( PanelWindows.Find( main.Handle ) );
			calls++;
		};
		PanelWindows.DragSession = replacement;

		PanelWindows.Unregister( main );

		Assert.AreEqual( 1, calls );
		Assert.AreEqual( 0, replacement.Frames );
	}

	/// <summary>Window mutations at the frame boundary can reenter drawing without reticking the drag.</summary>
	[TestMethod]
	public void FrameCallbackDoesNotReenter()
	{
		session.FrameAction = () =>
		{
			Assert.AreEqual( 1, session.Frames );
			PanelWindows.Unregister( main );
			PanelWindows.FrameAll();
		};

		PanelWindows.FrameAll();

		Assert.AreEqual( 1, session.Frames );
		session.FrameAction = null;
		PanelWindows.FrameAll();
		Assert.AreEqual( 2, session.Frames );
	}

	/// <summary>A deferred cancellation can finish after the last window has closed.</summary>
	[TestMethod]
	public void SessionFramesWithoutWindows()
	{
		foreach ( var window in new[] { main, hovered, ignored, popup } ) PanelWindows.Unregister( window );
		Assert.AreEqual( 0, PanelWindows.All.Count );

		Assert.IsFalse( PanelWindows.FrameAll() );
		Assert.AreEqual( 1, session.Frames );
	}

	/// <summary>Failure cancels the failing session, contains cleanup errors and releases the frame guard.</summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void FrameFailureCancelsOriginalSession( bool cancelThrows )
	{
		var replacement = new FakeDragSession();
		session.FrameAction = () =>
		{
			PanelWindows.DragSession = replacement;
			throw new InvalidOperationException( "Frame failure" );
		};
		session.CancelAction = () =>
		{
			PanelWindows.FrameAll();
			if ( cancelThrows ) throw new InvalidOperationException( "Cancel failure" );
		};

		PanelWindows.FrameAll();

		Assert.AreEqual( 1, session.Cancellations );
		Assert.AreEqual( 0, replacement.Cancellations );
		Assert.AreEqual( 0, replacement.Frames );
		Assert.AreSame( replacement, PanelWindows.DragSession );
		PanelWindows.FrameAll();
		Assert.AreEqual( 1, replacement.Frames );
	}

	/// <summary>Pointer handoff clears every held button and capture without losing focus or selection.</summary>
	[TestMethod]
	public void CancelPointerInteractionClearsCaptureWithoutClickOrDrop()
	{
		using var surface = new UISurface { Size = new Vector2( 200, 200 ), MouseInside = true };
		var panel = new RecordingPanel { Parent = surface.Root, AcceptsFocus = true };
		panel.Style.Width = 100;
		panel.Style.Height = 100;
		panel.Style.PointerEvents = PointerEvents.All;
		surface.MousePosition = new Vector2( 20, 20 );
		Frame( surface );
		Frame( surface );

		var input = surface.Input;
		// Raw button state avoids the native virtual-key mapper used by queued button events.
		input.AddMouseButton( ButtonCode.MouseLeft, true, default );
		Frame( surface );
		Frame( surface );
		Assert.AreSame( panel, input.Active );
		Assert.AreSame( panel, surface.Focus );

		input.MouseStates[0].Dragged = true;
		input.UpdateMouse( surface.Root, input.GetInputData() );
		Assert.AreSame( panel, input.DropTarget );

		var selection = input.Selection;
		var mouseDownEvent = typeof( PanelInput.MouseButtonState ).GetField( "MouseDownEvent", BindingFlags.Instance | BindingFlags.NonPublic );
		Assert.IsNotNull( mouseDownEvent.GetValue( input.MouseStates[0] ) );
		foreach ( var state in input.MouseStates )
		{
			input.AddMouseButton( state.MouseButton, true, default );
			state.Pressed = true;
			state.Active = panel;
			state.Dragged = true;
			state.DragTarget = panel;
			state.StartHoldOffsetLocal = new Vector2( 12, 34 );
			state.StartHoldOffsetScreen = new Vector2( 56, 78 );
		}
		panel.Events.Clear();
		surface.SetKey( "f5", true );

		input.CancelPointerInteraction();
		input.CancelPointerInteraction();

		Assert.IsNull( input.Active );
		Assert.IsNull( input.DropTarget );
		Assert.AreSame( selection, input.Selection );
		Assert.AreSame( panel, input.Hovered );
		Assert.AreSame( panel, surface.Focus );
		Assert.AreEqual( (PseudoClass)0, panel.PseudoClass & PseudoClass.Active );
		Assert.AreEqual( (PseudoClass)0, surface.Root.PseudoClass & PseudoClass.Active );
		foreach ( var state in input.MouseStates )
		{
			Assert.IsFalse( state.Pressed );
			Assert.IsFalse( state.Dragged );
			Assert.IsNull( state.Active );
			Assert.IsNull( state.DragTarget );
			Assert.AreEqual( Vector2.Zero, state.StartHoldOffsetLocal );
			Assert.AreEqual( Vector2.Zero, state.StartHoldOffsetScreen );
			Assert.IsNull( mouseDownEvent.GetValue( state ) );
		}
		var data = input.GetInputData();
		Assert.IsFalse( data.Mouse0 || data.Mouse1 || data.Mouse2 || data.Mouse3 || data.Mouse4 );
		CollectionAssert.AreEqual( new[] { "ondragleave" }, panel.Events );
		surface.System.InputEventQueue.TickFocused( surface.Focus );
		Assert.AreEqual( "f5", panel.LastKey );

		panel.Events.Clear();
		foreach ( var state in input.MouseStates )
		{
			input.AddMouseButton( state.MouseButton, false, default );
			state.Update( false, panel );
		}
		Assert.AreEqual( 0, panel.Events.Count, "Late releases must not click or finish a drag." );

		input.MouseStates[0].Update( true, panel );
		input.MouseStates[0].Update( false, panel );
		Assert.AreEqual( 1, panel.Events.FindAll( name => name == "onclick" ).Count );
		Assert.IsFalse( panel.Events.Contains( "ondrop" ) );
	}

	/// <summary>Capture can be abandoned after its panel has been deleted.</summary>
	[TestMethod]
	public void CancelPointerInteractionToleratesDeletedCapture()
	{
		using var surface = new UISurface();
		var panel = new RecordingPanel { Parent = surface.Root };
		var state = surface.Input.MouseStates[0];
		state.Pressed = true;
		state.Active = panel;
		state.DragTarget = panel;
		state.Dragged = true;
		panel.Delete( true );
		panel.Events.Clear();

		surface.Input.CancelPointerInteraction();
		state.Update( false, null );

		Assert.IsNull( state.Active );
		Assert.IsNull( state.DragTarget );
		Assert.IsFalse( state.Pressed );
		Assert.AreEqual( 0, panel.Events.Count );
	}
}
