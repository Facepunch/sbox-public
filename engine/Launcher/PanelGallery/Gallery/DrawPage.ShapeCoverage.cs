namespace Sandbox.PanelGallery;

public partial class DrawPage
{
	delegate void ShapeDraw( Painter painter, Rect rect, float detail );
	static readonly Dictionary<string, ShapeDraw> ShapeExamples = new()
	{
		["Rectangle"] = ( p, r, detail ) => { p.Rect( r, detail ); },
		["Circle"] = ( p, r, detail ) => { var radius = MathF.Min( r.Width, r.Height ) * 0.5f; p.Circle( r.Center, radius ); },
		["Star"] = ( p, r, detail ) => p.Star( r.Center, 30, 40 ),
		["Cross"] = ( p, r, detail ) => p.Cross( r.Center, 30, 120 ),
		["Tick"] = ( p, r, detail ) => p.Tick( r.Center, 25, 120 ),
		["Capsule"] = ( p, r, detail ) => p.Capsule( r.Center - new Vector2( 35, 0 ), r.Center + new Vector2( 35, 0 ), 25, 40 ),
		["Crescent"] = ( p, r, detail ) => p.Crescent( r.Center, 60, 50, new Vector2( 25, -10 ) ),
		["Heart"] = ( p, r, detail ) => p.Heart( r.Center, 120 ),
		["Ellipse"] = ( p, r, detail ) => { p.Circle( r ); },
		["Triangle"] = ( p, r, detail ) => { p.Triangle( r.BottomLeft, r.TopLeft + new Vector2( r.Width * 0.5f, 0 ), r.BottomRight ); },
		["Quad"] = ( p, r, detail ) => { p.Quad( r.TopLeft + new Vector2( r.Width * 0.2f, 0 ), r.TopRight, r.BottomRight - new Vector2( r.Width * 0.2f, 0 ), r.BottomLeft ); },
		["Polygon"] = ( p, r, detail ) => { p.Polygon( [r.TopLeft, r.TopRight, new Vector2( r.Right, r.Center.y ), r.Center, new Vector2( r.Center.x, r.Bottom ), r.BottomLeft] ); },
		["Line"] = ( p, r, detail ) => { p.Line( r.BottomLeft, r.TopRight ); },
		["Arrow"] = ( p, r, detail ) => { p.Arrow( r.BottomLeft, r.TopRight, MathF.Min( r.Width, r.Height ) * 0.15f, MathF.Min( r.Width, r.Height ) * 0.55f ); },
		["Ring"] = ( p, r, detail ) => { var radius = MathF.Min( r.Width, r.Height ) * 0.5f; p.Ring( r.Center, radius * 0.6f, radius ); },
		["Arc"] = ( p, r, detail ) => { var radius = MathF.Min( r.Width, r.Height ) * 0.5f; p.Arc( r.Center, radius, -90, detail ); },
		["Pie"] = ( p, r, detail ) => { var radius = MathF.Min( r.Width, r.Height ) * 0.5f; p.Pie( r.Center, radius, -90, detail ); },
		["Quadratic Bezier"] = ( p, r, detail ) => { p.Bezier( r.BottomLeft, new Vector2( r.Center.x, r.Top - r.Height ), r.BottomRight ); },
		["Cubic Bezier"] = ( p, r, detail ) => { p.Bezier( r.BottomLeft, r.TopLeft, r.BottomRight, r.TopRight ); },
	};

	static void PaintShape( Painter p, Rect r, string shape, float detail = 0 ) => ShapeExamples[shape]( p, r, ShapeDetail( shape, detail ) );

	static float ShapeDetail( string shape, float detail ) => detail != 0 ? detail : shape is "Arc" or "Pie" ? 240 : 0;

	void ShapeSpecificCases( string shape )
	{
		if ( shape is "Triangle" or "Quad" or "Polygon" )
		{
			var row = Examples( "Winding and degenerate geometry" );
			foreach ( var variant in new[] { "Clockwise", "Counterclockwise", "Collinear", "Repeated vertex" } )
			{
				int count = shape == "Triangle" ? 3 : shape == "Quad" ? 4 : 6;
				var vertices = new Vector2[count];
				for ( int i = 0; i < count; i++ )
				{
					float angle = i * MathF.Tau / count;
					float radius = shape == "Polygon" && i % 2 == 0 ? 0.25f : 0.5f;
					vertices[i] = new Vector2( 0.5f + MathF.Cos( angle ) * radius, 0.5f + MathF.Sin( angle ) * radius );
				}
				if ( variant == "Counterclockwise" ) Array.Reverse( vertices );
				if ( variant == "Collinear" ) for ( int i = 0; i < count; i++ ) vertices[i] = new Vector2( i / (float)(count - 1), 0.5f );
				if ( variant == "Repeated vertex" ) vertices[1] = vertices[0];
				Sample( row, variant, "Reversing winding should preserve the fill. Collinear and repeated vertices should remain stable without stray pixels.", ( p, r ) =>
				{
					p.Fill = GalleryPalette.Cyan.WithAlpha( 0.5f );
					p.Stroke = Stroke.Solid( Color.White, 3 );
					r = r.Shrink( 15 );
					Span<Vector2> points = stackalloc Vector2[vertices.Length];
					for ( int i = 0; i < points.Length; i++ ) points[i] = r.Position + vertices[i] * r.Size;
					if ( shape == "Triangle" ) p.Triangle( points[0], points[1], points[2] );
					else if ( shape == "Quad" ) p.Quad( points[0], points[1], points[2], points[3] );
					else p.Polygon( points );
				}, animateBounds: false );
			}
		}
		if ( shape is "Circle" or "Ellipse" )
		{
			var row = Examples( "Tiny and zero geometry" );
			foreach ( var radius in new[] { 0f, 0.5f, 60f } )
				Sample( row, $"Radius {radius}px", "Zero size draws nothing. Subpixel geometry should fade smoothly instead of becoming a full-size pixel.", ( p, r ) =>
				{
					p.Fill = GalleryPalette.Cyan;
					if ( shape == "Circle" ) p.Circle( r.Center, radius );
					else p.Circle( new Rect( r.Center - new Vector2( radius, radius * 0.5f ), new Vector2( radius * 2, radius ) ) );
				}, animateBounds: false );
		}
		if ( shape is "Arrow" )
		{
			var row = Examples( "Head and shaft limits" );
			foreach ( var head in new[] { 0f, 12f, 120f } )
				Sample( row, $"Head {head}px", "Zero width draws nothing; oversized heads must clamp to the segment without inverting the shaft.", ( p, r ) =>
				{
					p.Fill = GalleryPalette.Cyan;
					p.Stroke = Stroke.Solid( Color.White, 2 );
					var from = r.Center - new Vector2( 40, 0 );
					var to = r.Center + new Vector2( 40, 0 );
					p.Arrow( from, to, 18, head );
				}, animateBounds: false );
		}
		if ( shape is "Quadratic Bezier" or "Cubic Bezier" )
		{
			var row = Examples( "Control point limits" );
			foreach ( var variant in new[] { "Straight", "Reversed", "Loop", "Coincident" } )
			{
				var (start, end, control1, control2) = variant switch
				{
					"Straight" => ("r.BottomLeft", "r.BottomRight", "r.BottomLeft", "r.BottomRight"),
					"Reversed" => ("r.BottomRight", "r.BottomLeft", "r.TopLeft", "r.TopRight"),
					"Loop" => ("r.BottomLeft", "r.BottomLeft", "r.TopLeft", "r.TopRight"),
					_ => ("r.Center", "r.Center", "r.Center", "r.Center")
				};
				Sample( row, variant, "Straight, reversed, looping and coincident control points exercise curve flattening and dash continuity.", ( p, r ) =>
				{
					p.Stroke = Stroke.Dashed( GalleryPalette.Cyan, 4, 8, 4 );
					var a = r.BottomLeft;
					var b = r.BottomRight;
					var c = r.TopLeft;
					var d = r.TopRight;
					if ( variant == "Straight" ) { c = a; d = b; }
					if ( variant == "Reversed" ) { (a, b) = (b, a); }
					if ( variant == "Loop" ) b = a;
					if ( variant == "Coincident" ) a = b = c = d = r.Center;
					if ( shape == "Quadratic Bezier" ) p.Bezier( a, c, b );
					else p.Bezier( a, c, d, b );
				}, animateBounds: false );
			}
		}
		if ( shape == "Rectangle" )
		{
			var row = Examples( "Corner radius limits" );
			foreach ( var radius in new[] { 4f, 1000f } )
				Sample( row, $"Radius {radius}px", "Oversized radii must clamp cleanly to a pill; fill and stroke should agree.", ( p, r ) =>
				{
					p.Fill = GalleryPalette.Cyan;
					p.Stroke = Stroke.Solid( Color.White, 4 );
					p.Rect( r.Shrink( 12 ), radius );
				}, animateBounds: false );
		}
		if ( shape is "Arc" or "Pie" )
		{
			var row = Examples( "Sweep direction and limits" );
			foreach ( var detail in new[] { -360f, 1, 270 } )
				Sample( row, $"{detail}", "Compare clockwise, counterclockwise, tiny and full-circle sweeps.", ( p, r ) =>
				{
					p.Fill = shape == "Arc" ? Fill.None : GalleryPalette.Cyan;
					p.Stroke = Stroke.Dashed( Color.White, 3, 7, 4 );
					PaintShape( p, r.Shrink( 15 ), shape, detail );
				}, animateBounds: false );
		}
		if ( shape is "Line" or "Arrow" or "Quadratic Bezier" or "Cubic Bezier" )
		{
			var row = Examples( "Stroke caps and joins" );
			foreach ( var cap in new[] { Stroke.LineCap.Butt, Stroke.LineCap.Round, Stroke.LineCap.Square } )
				Sample( row, cap.ToString(), "Thick strokes reveal endpoint extension and how the contour joins.", ( p, r ) =>
				{
					p.Fill = GalleryPalette.Cyan.WithAlpha( 0.3f );
					p.Stroke = Stroke.Solid( Color.White, 14 ) with { Cap = cap };
					PaintShape( p, r.Shrink( 20 ), shape );
				}, animateBounds: false );
		}
	}
}
