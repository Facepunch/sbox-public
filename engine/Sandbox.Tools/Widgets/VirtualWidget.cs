namespace Editor;

public class VirtualWidget
{
	public Rect Rect;
	public object Object;
	public bool Hovered;
	public bool Selected;
	public bool Pressed;
	public bool Dropping;
	public bool Dragging;
	public int Row;
	public int Column;

	// for tree
	public float Indent;
	public bool HasChildren;
	public bool IsOpen;
	public Rect ChildrenRect;

	/// <summary>
	/// Generically paint a background for this item
	/// </summary>
	public void PaintBackground( Color background, float radius )
	{
		var c = background;
		var r = Rect;
		Paint.ClearPen();

		if ( Hovered )
		{
			Paint.SetPen( Color.Lerp( c, Theme.Blue, 0.8f ), 1, PenStyle.Dot );
			r = r.Shrink( 1 );
			c = Color.Lerp( c, Theme.Blue, 0.3f );
		}

		if ( Selected )
		{
			Paint.SetPen( Theme.Blue, 1 );
			c = Color.Lerp( c, Theme.Blue, 0.3f );
		}

		if ( c.a > 0 )
		{
			Paint.SetBrush( c );
			Paint.DrawRect( r, radius );
		}
	}

	/// <summary>
	/// 
	/// </summary>
	public Color GetForegroundColor()
	{
		return Theme.Text;
	}
}
