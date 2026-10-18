namespace Sandbox.UI.NodeGraph;

/// <summary>
/// Draws a node graph panel in node units (canvas units at zoom 1) through a panel's
/// <see cref="Painter"/>. Font sizes are in points.
/// </summary>
public ref struct NodePaint : INodePaint
{
	public const string DefaultFont = "Inter";
	public const string IconFont = "Material Icons";

	readonly Painter _painter;
	readonly float _scale;

	Color? _pen;
	float _penWidth = 1;
	Fill _brush = Fill.None;
	TextStyle _text = TextStyle.Default with { FontName = DefaultFont, FontSize = Points( 8 ) };

	/// <summary>
	/// Paint into <paramref name="panel"/>, whose layout size is in node units.
	/// </summary>
	public NodePaint( Panel panel, Painter painter )
	{
		_painter = painter;
		_scale = panel.ScaleToScreen;
	}

	/// <summary>
	/// The panel being drawn is under the cursor.
	/// </summary>
	public bool HasMouseOver { get; init; }

	static float Points( float size ) => size * 4f / 3f;

	Rect Scale( Rect rect ) => new( rect.Position * _scale, rect.Size * _scale );

	Stroke CurrentStroke => _pen is { } pen ? Stroke.Solid( pen, _penWidth * _scale ) : Stroke.None;

	/// <summary>
	/// Text size in node units, for layout outside of drawing.
	/// </summary>
	public static Vector2 Measure( string text, float size = 8, int weight = 400, bool italic = false, string font = DefaultFont )
	{
		if ( string.IsNullOrEmpty( text ) ) return Vector2.Zero;
		return new TextRendering.Scope( text, Color.White, Points( size ), font ?? DefaultFont, weight ) { FontItalic = italic }.Measure();
	}

	public void SetPen( Color color, float width = 1 )
	{
		_pen = color;
		_penWidth = width;
		_text = _text with { Color = color };
	}

	public void ClearPen() => _pen = null;

	public void SetBrush( Texture texture ) => _brush = texture is null ? Fill.None : Fill.Image( texture,
		width: Length.Pixels( texture.Width * _scale ), height: Length.Pixels( texture.Height * _scale ), repeat: BackgroundRepeat.Repeat );

	public void SetBrush( Color color ) => _brush = color;

	/// <summary>
	/// A gradient from <paramref name="a"/> on the left to <paramref name="b"/> on the right of each shape.
	/// </summary>
	public void SetBrushLinear( Color a, Color b ) => _brush = Fill.LinearGradient().WithStop( 0, a ).WithStop( 1, b );

	public void ClearBrush() => _brush = Fill.None;

	public void SetBrushAndPen( Color color, float width = 1 )
	{
		SetBrush( color );
		SetPen( color, width );
	}

	public void SetDefaultFont( float size = 8, int weight = 400, bool italic = false )
		=> SetFont( DefaultFont, size, weight, italic );

	public void SetFont( string name, float size, int weight = 400, bool italic = false )
		=> _text = _text with { FontName = name ?? DefaultFont, FontSize = Points( size ), FontWeight = weight, Italic = italic };

	public void DrawRect( Rect rect, float radius = 0 )
	{
		using var scope = _painter.Scope();
		_painter.Fill = _brush;
		_painter.Stroke = CurrentStroke;
		_painter.Rect( Scale( rect ), radius * _scale );
	}

	public void DrawCircle( Rect rect )
	{
		using var scope = _painter.Scope();
		_painter.Fill = _brush;
		_painter.Stroke = CurrentStroke;
		_painter.Circle( Scale( rect ) );
	}

	public void DrawLine( Vector2 from, Vector2 to )
	{
		if ( _pen is null ) return;

		using var scope = _painter.Scope();
		_painter.Stroke = CurrentStroke;
		_painter.Line( from * _scale, to * _scale );
	}

	public void DrawPolygon( params ReadOnlySpan<Vector2> points )
	{
		if ( points.Length < 3 ) return;

		Span<Vector2> scaled = points.Length <= 64 ? stackalloc Vector2[points.Length] : new Vector2[points.Length];
		for ( var i = 0; i < points.Length; i++ ) scaled[i] = points[i] * _scale;

		using var scope = _painter.Scope();
		_painter.Fill = _brush;
		_painter.Stroke = CurrentStroke;
		_painter.Polygon( scaled );
	}

	/// <summary>
	/// Draw text aligned in <paramref name="rect"/>, returning the rect it covers. Like Qt, text
	/// bigger than the rect overflows it instead of being dropped.
	/// </summary>
	public Rect DrawText( Rect rect, string text, TextFlag flags = TextFlag.Center )
	{
		if ( string.IsNullOrEmpty( text ) ) return new Rect( rect.Position, 0 );

		using var scope = _painter.Scope();
		var drawn = DrawAligned( _text with { Alignment = flags }, Scale( rect ), text, flags );
		return new Rect( drawn.Position / _scale, drawn.Size / _scale );
	}

	/// <summary>
	/// Draw a Material icon, <paramref name="size"/> in node units.
	/// </summary>
	public void DrawIcon( Rect rect, string icon, float size, TextFlag flags = TextFlag.Center )
	{
		if ( string.IsNullOrEmpty( icon ) ) return;

		using var scope = _painter.Scope();
		DrawAligned( _text with { FontName = IconFont, FontSize = size, FontWeight = 400, Italic = false, Alignment = flags }, Scale( rect ), icon, flags );
	}

	/// <summary>
	/// Lay <paramref name="text"/> out at its own size, wrapping only to the rect's width, and align it in <paramref name="bounds"/>.
	/// </summary>
	Rect DrawAligned( TextStyle style, Rect bounds, string text, TextFlag flags )
	{
		_painter.TextStyle = style;

		var wrap = (flags & TextFlag.WordWrap) != 0 && bounds.Width > 0;
		var size = _painter.MeasureText( text, wrap ? new Vector2( bounds.Width, 100000 ) : default );
		var drawn = bounds.Align( new Vector2( MathF.Ceiling( size.x ) + 1, MathF.Ceiling( size.y ) + 1 ), flags );

		_painter.Text( text, drawn );
		return drawn;
	}

	/// <summary>
	/// Text size in node units with the current font.
	/// </summary>
	public Vector2 MeasureText( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return Vector2.Zero;

		using var scope = _painter.Scope();
		_painter.TextStyle = _text;
		return _painter.MeasureText( text ) / _scale;
	}

	public void Draw( Rect rect, Texture texture )
	{
		if ( texture is null ) return;
		_painter.Texture( texture, Scale( rect ) );
	}
}
