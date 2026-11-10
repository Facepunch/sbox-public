namespace Sandbox.UI;

public partial class CanvasPanel
{
	/// <summary>
	/// Allow left-dragging empty canvas space to draw a selection rectangle. Off by default.
	/// </summary>
	public bool BoxSelectionEnabled { get; set; }

	/// <summary>
	/// The current selection rectangle in canvas coordinates, or null when not selecting.
	/// </summary>
	public Rect? SelectionBox { get; private set; }
	public bool IsBoxSelecting => SelectionBox.HasValue;

	/// <summary>
	/// Begins a selection gesture. The argument indicates Shift was held for additive selection.
	/// </summary>
	public event Action<bool> BoxSelectionStarted;

	/// <summary>
	/// The selection rectangle changed, in canvas coordinates. The caller decides which items to select.
	/// </summary>
	public event Action<Rect> BoxSelectionChanged;

	/// <summary>
	/// Ends the gesture. True means cancelled (Escape, focus/capture loss, or disabling the option).
	/// </summary>
	public event Action<bool> BoxSelectionFinished;
	Vector2 _boxStart;
	Panel _selectionBox;

	/// <summary>
	/// Start a box gesture after a custom canvas has checked its own hit targets.
	/// </summary>
	protected void BeginBoxSelection( bool additive )
	{
		if ( !BoxSelectionEnabled || !CanNavigate || IsPanning || IsBoxSelecting )
			return;
		Focus();
		_boxStart = ScreenToCanvas( MousePosition );
		SelectionBox = new Rect( _boxStart, Vector2.Zero );
		_selectionBox ??= AddChild<Panel>( "canvas-selection-box" );
		_selectionBox.Style.Display = DisplayMode.Flex;
		BoxSelectionStarted?.Invoke( additive );
		UpdateBoxSelection();
	}

	void UpdateBoxSelection()
	{
		var point = ScreenToCanvas( new Vector2( Math.Clamp( MousePosition.x, Viewport.Left, Viewport.Right ), Math.Clamp( MousePosition.y, Viewport.Top, Viewport.Bottom ) ) );
		var rect = new Rect( Vector2.Min( _boxStart, point ), Vector2.Max( _boxStart, point ) - Vector2.Min( _boxStart, point ) );
		SelectionBox = rect;
		var a = CanvasToScreen( rect.Position );
		var b = CanvasToScreen( rect.Position + rect.Size );
		var min = Vector2.Min( a, b ) / ScaleToScreen;
		var size = (Vector2.Max( a, b ) - Vector2.Min( a, b )) / ScaleToScreen;
		_selectionBox.Style.Left = min.x;
		_selectionBox.Style.Top = min.y;
		_selectionBox.Style.Width = size.x;
		_selectionBox.Style.Height = size.y;
		BoxSelectionChanged?.Invoke( rect );
	}

	/// <summary>
	/// Complete or cancel the active selection gesture.
	/// </summary>
	protected void FinishBoxSelection( bool cancelled = false )
	{
		if ( !IsBoxSelecting )
			return;
		SelectionBox = null;
		_selectionBox.Style.Display = DisplayMode.None;
		BoxSelectionFinished?.Invoke( cancelled );
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( IsBoxSelecting && e.Button == "escape" )
		{
			FinishBoxSelection( true );
			e.StopPropagation = true;
			return;
		}
		base.OnButtonTyped( e );
	}
}
