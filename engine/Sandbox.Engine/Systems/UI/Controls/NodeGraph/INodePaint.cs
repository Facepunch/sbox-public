namespace Sandbox.UI.NodeGraph;

/// <summary>
/// Drawing surface for <see cref="INode.OnPaint{T}"/>, in node units. A pen draws outlines and
/// text, a brush fills shapes. Implemented by <see cref="NodePaint"/>, and by editors that draw
/// nodes some other way, so a node's drawing is written once.
/// </summary>
public interface INodePaint
{
	/// <summary>
	/// The node is under the cursor.
	/// </summary>
	bool HasMouseOver { get; }

	void SetPen( Color color, float width = 1 );
	void ClearPen();

	void SetBrush( Color color );

	/// <summary>
	/// Fill with <paramref name="texture"/> tiled at its own size.
	/// </summary>
	void SetBrush( Texture texture );

	void ClearBrush();

	/// <summary>
	/// The default font; <paramref name="size"/> is in points.
	/// </summary>
	void SetDefaultFont( float size = 8, int weight = 400, bool italic = false );

	/// <summary>
	/// <paramref name="size"/> is in points.
	/// </summary>
	void SetFont( string name, float size, int weight = 400, bool italic = false );

	void DrawRect( Rect rect, float radius = 0 );
	void DrawCircle( Rect rect );
	void DrawLine( Vector2 from, Vector2 to );
	void DrawPolygon( params ReadOnlySpan<Vector2> points );

	/// <summary>
	/// Draw text aligned in <paramref name="rect"/>, returning the rect it covers.
	/// </summary>
	Rect DrawText( Rect rect, string text, TextFlag flags = TextFlag.Center );

	/// <summary>
	/// Draw a Material icon <paramref name="size"/> units tall.
	/// </summary>
	void DrawIcon( Rect rect, string icon, float size, TextFlag flags = TextFlag.Center );

	/// <summary>
	/// Text size with the current font.
	/// </summary>
	Vector2 MeasureText( string text );

	/// <summary>
	/// Draw <paramref name="texture"/> stretched over <paramref name="rect"/>.
	/// </summary>
	void Draw( Rect rect, Texture texture );
}
