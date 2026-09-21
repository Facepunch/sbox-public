using NativeEngine;
using System;
using Sandbox.UI;

namespace UITests.Controls;

/// <summary>
/// A stand-in panel window - remembers whether it was asked to close, and can host a popup in
/// a surface of its own, closing when that popup goes.
/// </summary>
sealed class FakePanelWindow : IPanelWindow, IPopupHost
{
	public bool CloseRequested;

	public IntPtr Handle { get; init; }
	public UISurface Surface { get; init; }
	public bool IsOpen => !CloseRequested;
	public bool MouseInside { get; set; }
	public Vector2 CursorPosition { get; private set; }
	public void SetCursorPosition( Vector2 position ) => CursorPosition = position;
	public Vector2 ToSurface( Vector2 windowPosition ) => windowPosition;
	public bool Frame() => false;
	public Sdl.HitTestResult HitTest( Vector2 position ) => Sdl.HitTestResult.Normal;
	public void RequestClose() => CloseRequested = true;
	public void Moved() { }
	public void Resized() { }
	public int WindowState { get; private set; }
	public void StateChanged( int state ) => WindowState = state;
	public void FocusChanged( bool focused ) { }
	public void DisplayChanged() { }
	public bool IsPopup { get; init; }
	public IPanelWindow Parent { get; init; }
	public bool IgnoresInput { get; init; }
	public bool KeepKeyboardInParent { get; init; }
	public bool TakesKeyboardFocus => !IgnoresInput && !KeepKeyboardInParent;
	public bool AllowNestedFrame { get; set; }
	public bool IsFocused => false;
	public bool AlwaysFullFrameRate { get; set; }

	public void ShowPopup( Popup popup, Panel source, Popup.PositionMode position, float offset ) => popup.Parent = Surface.Root;
	public void HidePopup( Popup popup ) => RequestClose();
}
