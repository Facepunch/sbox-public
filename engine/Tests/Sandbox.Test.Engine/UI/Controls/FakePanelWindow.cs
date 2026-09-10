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
	public void SetCursorPosition( Vector2 position ) { }
	public Vector2 ToSurface( Vector2 windowPosition ) => windowPosition;
	public bool Frame( bool interactiveResize ) => false;
	public IPanelWindow.WindowHitTest HitTest( Vector2 position ) => IPanelWindow.WindowHitTest.Normal;
	public void RequestClose() => CloseRequested = true;
	public void Moved() { }
	public void Resized() { }
	public void StateChanged( int state ) { }
	public void FocusChanged( bool focused ) { }
	public void DisplayChanged() { }
	public bool IsPopup { get; init; }
	public IPanelWindow Parent { get; init; }
	public bool IgnoresInput { get; init; }
	public bool AllowNestedFrame { get; set; }
	public bool IsFocused => false;
	public bool AlwaysFullFrameRate { get; set; }

	public void ShowPopup( Popup popup, Panel source, Popup.PositionMode position, float offset ) => popup.Parent = Surface.Root;
	public void HidePopup( Popup popup ) => RequestClose();
}
