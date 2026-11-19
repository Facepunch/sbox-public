namespace Sandbox.UI.Dev;

/// <summary>
/// A handle dragged to size part of the console - its window's bottom right corner, or the splitter between its log and
/// a line's details
/// </summary>
public class ConsoleGrip : Panel
{
	/// <summary>
	/// Dragged to this point on the screen
	/// </summary>
	public Action<Vector2> OnDragTo { get; set; }

	/// <summary>
	/// Let go
	/// </summary>
	public Action OnDragDone { get; set; }

	public override bool WantsDrag => true;

	protected override void OnDragStart( DragEvent e )
	{
		e.StopPropagation();
	}

	protected override void OnDrag( DragEvent e )
	{
		e.StopPropagation();
		OnDragTo?.Invoke( e.ScreenPosition );
	}

	protected override void OnDragEnd( DragEvent e )
	{
		e.StopPropagation();
		OnDragDone?.Invoke();
	}
}
