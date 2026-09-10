using NativeEngine;

namespace Sandbox.UI;

/// <summary>
/// Keyboard (and mouse) key press <see cref="PanelEvent"/>.
/// </summary>
public record ButtonEvent
{
	/// <summary>
	/// The button that triggered the event.
	/// </summary>
	public string Button { get; }

	/// <summary>
	/// Whether the button was pressed in, or release.
	/// </summary>
	public bool Pressed { get; }


	public int VirtualKey { get; }

	/// <summary>
	/// The keyboard modifier keys that were held down at the moment the event triggered.
	/// </summary>
	public KeyboardModifiers KeyboardModifiers { get; }

	/// <summary>
	/// Whether <c>Shift</c> key was being held down at the time of the event.
	/// </summary>
	public bool HasShift => KeyboardModifiers.Contains( KeyboardModifiers.Shift );

	/// <summary>
	/// Whether <c>Control</c> key was being held down at the time of the event.
	/// </summary>
	public bool HasCtrl => KeyboardModifiers.Contains( KeyboardModifiers.Ctrl );

	/// <summary>
	/// Whether <c>Alt</c> key was being held down at the time of the event.
	/// </summary>
	public bool HasAlt => KeyboardModifiers.Contains( KeyboardModifiers.Alt );

	/// <summary>
	/// Set to <see langword="true"/> to prevent the event from propagating to the parent panel.
	/// </summary>
	public bool StopPropagation { get; set; }


	internal ButtonEvent( ButtonCode button, bool pressed, KeyboardModifiers modifiers )
	{
		Button = InputEventQueue.NormalizeButtonName( button.ToString() );
		Pressed = pressed;
		VirtualKey = InputSystem.ButtonCodeToVirtualKey( button );
		KeyboardModifiers = modifiers;
	}

	internal ButtonEvent( ButtonCode button, bool pressed )
	{
		Button = InputEventQueue.NormalizeButtonName( button.ToString() );
		Pressed = pressed;
	}

	internal ButtonEvent( string button, bool pressed, int virtualKey, KeyboardModifiers modifiers )
	{
		Button = InputEventQueue.NormalizeButtonName( button );
		Pressed = pressed;
		VirtualKey = virtualKey;
		KeyboardModifiers = modifiers;
	}

	public override string ToString() => $"{Button} {(Pressed ? "pressed" : "released")}";
}
