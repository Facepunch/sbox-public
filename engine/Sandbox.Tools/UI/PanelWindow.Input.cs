using Sandbox.UI;

namespace Editor;

//
// Input. PanelWindowInput hands us what the OS sent for this window - see
// Systems/UI/Surface/PanelWindowInput.cs.
//
public sealed partial class PanelWindow
{
	Vector2 _mousePosition;
	bool _mouseInside;
	string _cursor;

	/// <summary>
	/// How far from the edge of a borderless window counts as a resize handle, in pixels.
	/// </summary>
	public float ResizeBorder { get; set; } = 6.0f;

	bool IPanelWindow.MouseInside
	{
		get => _mouseInside;
		set => _mouseInside = value;
	}

	void IPanelWindow.SetCursorPosition( Vector2 position )
	{
		_mousePosition = position;
		_mouseInside = position.x >= 0 && position.y >= 0 && position.x < Surface.Size.x && position.y < Surface.Size.y;
	}

	void ApplyCursorShape()
	{
		// The cursor follows the mouse, not the keyboard - it changes over an unfocused
		// window too. MouseInside keeps two windows from fighting over it
		if ( !Surface.MouseInside ) return;

		NativeEngine.InputSystem.SetCursorStandard( Sandbox.Engine.InputRouter.GetStandardCursor( _cursor ) );
	}

	/// <summary>
	/// Ask the OS to pick a folder, starting at <paramref name="defaultPath"/>. Null when the
	/// user cancels. One dialog at a time - asking again while one is open joins the first.
	/// </summary>
	public System.Threading.Tasks.Task<string> PickFolder( string defaultPath = null )
		=> PanelWindowDialogs.PickFolder( _window, defaultPath );

	/// <summary>
	/// Ask the OS for a file to open. The filter is name and extension list pairs, like
	/// "Scene files|scene;prefab|All files|*". Null when the user cancels.
	/// </summary>
	public System.Threading.Tasks.Task<string> PickOpenFile( string defaultPath = null, string filters = null )
		=> PanelWindowDialogs.PickOpenFile( _window, defaultPath, filters );

	/// <summary>
	/// Ask the OS where to save a file. <paramref name="defaultPath"/> can end in a suggested
	/// file name. Filters as in <see cref="PickOpenFile"/>. Null when the user cancels.
	/// </summary>
	public System.Threading.Tasks.Task<string> PickSaveFile( string defaultPath = null, string filters = null )
		=> PanelWindowDialogs.PickSaveFile( _window, defaultPath, filters );

	IPanelWindow.WindowHitTest IPanelWindow.HitTest( Vector2 position ) => HitTest( position );

	internal IPanelWindow.WindowHitTest HitTest( Vector2 position )
	{
		var size = Surface.Size;
		if ( size.x < 1 || size.y < 1 ) return IPanelWindow.WindowHitTest.Normal;

		// SDL asks in window points, we lay out in pixels
		position *= Surface.DpiScale;

		if ( !IsMaximized )
		{
			var border = ResizeBorder * Surface.DpiScale;

			var left = position.x <= border;
			var right = position.x >= size.x - border;
			var top = position.y <= border;
			var bottom = position.y >= size.y - border;

			if ( top && left ) return IPanelWindow.WindowHitTest.ResizeTopLeft;
			if ( top && right ) return IPanelWindow.WindowHitTest.ResizeTopRight;
			if ( bottom && left ) return IPanelWindow.WindowHitTest.ResizeBottomLeft;
			if ( bottom && right ) return IPanelWindow.WindowHitTest.ResizeBottomRight;
			if ( left ) return IPanelWindow.WindowHitTest.ResizeLeft;
			if ( right ) return IPanelWindow.WindowHitTest.ResizeRight;
			if ( top ) return IPanelWindow.WindowHitTest.ResizeTop;
			if ( bottom ) return IPanelWindow.WindowHitTest.ResizeBottom;
		}

		// The deepest panel at the cursor that says either way. Looking for the classes rather than
		// walking up from whatever happens to be on top means a decorative overlay - a centred
		// title, say - can't turn a button underneath it into a drag handle.
		var panel = Surface.FindPanelAt( position, x => x.HasClass( "window-drag" ) || x.HasClass( "window-nodrag" ) );

		if ( panel is null ) return IPanelWindow.WindowHitTest.Normal;

		return panel.HasClass( "window-nodrag" ) ? IPanelWindow.WindowHitTest.Normal : IPanelWindow.WindowHitTest.Draggable;
	}
}
