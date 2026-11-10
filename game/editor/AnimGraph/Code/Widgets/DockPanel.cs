namespace Editor.AnimGraph;

internal static class DockPanel
{
	/// <summary>
	/// The rounded background that grouped content sits on, inside a section.
	/// </summary>
	public static void PaintBackground( Rect rect )
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground.Darken( 0.1f ) );
		Paint.DrawRect( rect, Theme.ControlRadius );
	}

	/// <summary>
	/// A centered hint for a panel with nothing in it yet.
	/// </summary>
	public static void PaintHint( Rect rect, string hint )
	{
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.4f ) );
		Paint.SetDefaultFont();
		Paint.DrawText( rect, hint, TextFlag.Center );
	}
}
