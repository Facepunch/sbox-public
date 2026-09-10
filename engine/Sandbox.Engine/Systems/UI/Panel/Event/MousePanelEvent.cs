namespace Sandbox.UI;

/// <summary>
/// Mouse related <see cref="PanelEvent"/>.
/// </summary>
public class MousePanelEvent : PanelEvent
{
	/// <summary>
	/// Position of the cursor relative to the panel's top left corner at the time the event was triggered.
	/// </summary>
	public Vector2 LocalPosition;

	/// <summary>
	/// Which button triggered the event, in string form.
	/// </summary>
	public new string Button;

	/// <summary>
	/// Which button triggered the event, as a <see cref="MouseButtons"/> enum.
	/// </summary>
	public MouseButtons MouseButton { get; set; }

	/// <summary>
	/// The modifier keys held down when this happened - so a click can tell shift+click from
	/// a plain one.
	/// </summary>
	public KeyboardModifiers KeyboardModifiers { get; set; }

	/// <summary>
	/// Whether <c>Shift</c> was held down at the time of the event.
	/// </summary>
	public bool HasShift => KeyboardModifiers.Contains( KeyboardModifiers.Shift );

	/// <summary>
	/// Whether <c>Control</c> was held down at the time of the event.
	/// </summary>
	public bool HasCtrl => KeyboardModifiers.Contains( KeyboardModifiers.Ctrl );

	/// <summary>
	/// Whether <c>Alt</c> was held down at the time of the event.
	/// </summary>
	public bool HasAlt => KeyboardModifiers.Contains( KeyboardModifiers.Alt );

	public MousePanelEvent( string event_name, Panel active, string button ) : base( event_name, active )
	{
		Name = event_name;
		Target = active;
		LocalPosition = Target.MousePosition;
		Button = button;

		if ( button == "mouseleft" ) MouseButton = MouseButtons.Left;
		if ( button == "mouseright" ) MouseButton = MouseButtons.Right;
		if ( button == "mousemiddle" ) MouseButton = MouseButtons.Middle;
	}
}
