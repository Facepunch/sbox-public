using NativeEngine;

namespace Sandbox.UI;

/// <summary>
/// Mouse and keyboard state for a <see cref="UISurface"/>. The window hosting the surface
/// pushes events in here - nothing is read from the global mouse or the game input context.
/// </summary>
internal class SurfaceInput : PanelInput
{
	readonly UISurface Surface;

	internal SurfaceInput( UISurface surface )
	{
		Surface = surface;
	}

	/// <summary>
	/// Cursor position in surface pixels, top left is 0,0.
	/// </summary>
	public Vector2 MousePosition { get; set; }

	/// <summary>
	/// Should we be doing hover and click tests at all? False when the mouse isn't over us.
	/// </summary>
	public bool MouseInside { get; set; }

	/// <summary>
	/// The cursor the hovered panel is asking for. Null means default.
	/// </summary>
	public string Cursor { get; private set; }

	public void SetMouseButton( MouseButtons button, bool down, KeyboardModifiers modifiers )
	{
		var code = ToButtonCode( button );

		AddMouseButton( code, down, modifiers );
		Surface.System.InputEventQueue.AddButtonEvent( code, down, modifiers );

		if ( down ) Surface.System.InputEventQueue.AddButtonTyped( code, modifiers );
	}

	public void SetTripleClick( MouseButtons button )
	{
		Surface.System.InputEventQueue.AddTripleClick( ToButtonCode( button ).ToString() );
	}

	public void SetDoubleClick( MouseButtons button )
	{
		Surface.System.InputEventQueue.AddDoubleClick( ToButtonCode( button ).ToString() );
	}

	public void SetMouseWheel( Vector2 delta, KeyboardModifiers modifiers )
	{
		AddMouseWheel( delta, modifiers );
	}

	Vector2 frameDelta;
	Vector2 velocity;

	internal override Vector2 CursorPosition => MousePosition;
	internal override Vector2 CursorDelta => frameDelta;
	internal override Vector2 CursorVelocity => velocity;

	/// <summary>
	/// Called once a frame with the cursor's absolute position, which makes the delta here a
	/// per-frame delta - what the drag detector needs. The velocity is smoothed exactly the
	/// way <see cref="Mouse.Velocity"/> is: per-frame units, not per-second.
	/// </summary>
	public void MouseMoved( Vector2 position )
	{
		var delta = position - MousePosition;
		MousePosition = position;
		frameDelta = delta;

		velocity = (velocity * 2.0f + delta) / 3.0f;

		Surface.System.InputEventQueue.MouseMoved( delta );
	}

	public void SetMouseButton( ButtonCode code, bool down, KeyboardModifiers modifiers )
	{
		AddMouseButton( code, down, modifiers );
		Surface.System.InputEventQueue.AddButtonEvent( code, down, modifiers );

		if ( down ) Surface.System.InputEventQueue.AddButtonTyped( code, modifiers );
	}

	public void SetKey( ButtonCode code, bool down, KeyboardModifiers modifiers )
	{
		Surface.System.InputEventQueue.AddButtonEvent( code, down, modifiers );

		if ( down ) Surface.System.InputEventQueue.AddButtonTyped( code, modifiers );
	}

	public void SetKey( string button, bool down, int virtualKey, KeyboardModifiers modifiers )
	{
		if ( string.IsNullOrEmpty( button ) )
			return;

		Surface.System.InputEventQueue.AddButtonEvent( button, down, virtualKey, modifiers );

		if ( down ) Surface.System.InputEventQueue.AddButtonTyped( button, virtualKey, modifiers );
	}

	public void TypeText( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;

		foreach ( var c in text )
		{
			Surface.System.InputEventQueue.AddKeyTyped( c );
		}
	}

	internal override InputData GetInputData()
	{
		var data = base.GetInputData();
		data.MousePos = MousePosition;
		return data;
	}

	public override void SetCursor( string name )
	{
		if ( Cursor == name )
			return;

		Cursor = name;
		Surface.OnCursorChanged?.Invoke( name );
	}

	static ButtonCode ToButtonCode( MouseButtons button ) => button switch
	{
		MouseButtons.Left => ButtonCode.MouseLeft,
		MouseButtons.Right => ButtonCode.MouseRight,
		MouseButtons.Middle => ButtonCode.MouseMiddle,
		MouseButtons.Back => ButtonCode.MouseBack,
		MouseButtons.Forward => ButtonCode.MouseForward,
		_ => ButtonCode.MouseLeft
	};
}
