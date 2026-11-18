namespace Sandbox;

partial class Gizmo
{
	[System.Obsolete( "Use Gizmo.Pressed.This" )]
	public static bool IsPressed => Pressed.This;

	[System.Obsolete( "Use Gizmo.Pressed.Any" )]
	public static bool HasPressed => Pressed.Any;

	[System.Obsolete( "Use Gizmo.Pressed.Ray" )]
	public static Ray PressRay => Pressed.Ray;

	/// <summary>
	/// Access to the currently pressed path information
	/// </summary>
	public static class Pressed
	{
		/// <summary>
		/// The ray representing the cursor direction
		/// </summary>
		public static Ray Ray => Active.pressed.Input.CursorRay;

		/// <summary>
		/// True if the current gizmo scope is pressed
		/// </summary>
		public static bool This => (IsLeftMouseDown || Active.HasReleaseInput) && Active.current.PressedPath == Path;

		/// <summary>
		/// True if any object is currently pressed
		/// </summary>
		public static bool Any => Instance.HasActivePress;

		/// <summary>
		/// The distance the cursor has travelled since press started
		/// </summary>
		public static Vector2 CursorDelta => (CursorPosition - Gizmo.CursorPosition);

		/// <summary>
		/// The cursor position at the start of the press
		/// </summary>
		public static Vector2 CursorPosition => Active.pressed.Input.CursorPosition;

		/// <summary>
		/// True if press is active. This generally means that the left mouse button is down
		/// </summary>
		public static bool IsActive => Gizmo.IsLeftMouseDown;

		public static void ClearPath() => Active.ClearPress();
	}
}
