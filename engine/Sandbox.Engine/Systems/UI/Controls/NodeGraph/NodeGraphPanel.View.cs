namespace Sandbox.UI.NodeGraph;

//
// The view: zoom, framing, panning while dragging past the edge, and keeping it in a cookie.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// Layout pixels per canvas unit; 1 at the default zoom.
	/// </summary>
	public float Zoom => (CanvasToScreen( Vector2.One ) - CanvasToScreen( Vector2.Zero )).x / ScaleToScreen;

	/// <summary>
	/// The furthest the wheel zooms out, as a <see cref="Zoom"/>.
	/// </summary>
	public float MinZoom { get; set; } = 0.1f;

	/// <summary>
	/// The furthest the wheel zooms in, as a <see cref="Zoom"/>.
	/// </summary>
	public float MaxZoom { get; set; } = 5f;

	/// <summary>
	/// Centre the view on <paramref name="center"/> at <paramref name="zoom"/>.
	/// </summary>
	public void SetView( Vector2 center, float zoom )
	{
		var half = Viewport.Size / ScaleToScreen / zoom * 0.5f;
		SetView( center - half, center + half );
	}

	/// <summary>
	/// Centre the view on the selected nodes and wires, keeping the zoom.
	/// </summary>
	public void CenterOnSelection()
	{
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );
		var any = false;

		foreach ( var node in SelectedNodes )
		{
			min = Vector2.Min( min, node.Position );
			max = Vector2.Max( max, node.Position + node.Size );
			any = true;
		}

		foreach ( var connection in SelectedConnections )
		{
			min = Vector2.Min( min, connection.Bounds.TopLeft );
			max = Vector2.Max( max, connection.Bounds.BottomRight );
			any = true;
		}

		if ( !any ) return;

		SetView( (min + max) * 0.5f, Zoom );
	}

	/// <summary>
	/// While something is dragged past the edge of the view, scroll towards it.
	/// </summary>
	void AutoPan()
	{
		var dragging = IsBoxSelecting || _plugDragging || _wireDragging || _handleConnection is not null || _nodes.Any( x => x.IsDragging );
		if ( !dragging ) return;

		var viewport = Viewport;
		var mouse = MousePosition;
		if ( viewport.IsInside( mouse ) ) return;

		var overshoot = mouse - mouse.Clamp( viewport.Position, viewport.Position + viewport.Size );
		Pan( -overshoot / 8f );
	}

	//
	// View persistence
	//

	/// <summary>
	/// Cookie name the view's centre and zoom are kept under. Null doesn't keep them.
	/// </summary>
	protected virtual string ViewCookie => null;

	Vector2 _savedCenter;
	float _savedZoom;

	/// <summary>
	/// Go back to the centre and zoom saved under <see cref="ViewCookie"/>.
	/// </summary>
	public void RestoreViewFromCookie()
	{
		if ( ViewCookie is not { } cookie || Game.Cookies is not { } cookies ) return;

		var center = cookies.Get( $"{cookie}.view.center", (ViewMin + ViewMax) * 0.5f );
		var zoom = cookies.Get( $"{cookie}.view.scale", Zoom );

		if ( !float.IsFinite( center.x ) || !float.IsFinite( center.y ) || !float.IsFinite( zoom ) ) return;

		SetView( center, zoom.Clamp( MinZoom, MaxZoom ) );

		_savedCenter = (ViewMin + ViewMax) * 0.5f;
		_savedZoom = Zoom;
	}

	void SaveViewCookie()
	{
		if ( ViewCookie is not { } cookie || Game.Cookies is not { } cookies ) return;

		var center = (ViewMin + ViewMax) * 0.5f;
		var zoom = Zoom;

		if ( center != _savedCenter )
		{
			_savedCenter = center;
			cookies.Set( $"{cookie}.view.center", center );
		}

		if ( zoom != _savedZoom )
		{
			_savedZoom = zoom;
			cookies.Set( $"{cookie}.view.scale", zoom );
		}
	}
}
