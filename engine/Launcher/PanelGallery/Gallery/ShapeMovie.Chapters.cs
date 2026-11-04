namespace Sandbox.PanelGallery;

internal sealed partial class ShapeMovie
{
	static readonly Color Cyan = GalleryPalette.CyanHex;
	static readonly Color Pink = GalleryPalette.PinkHex;
	static readonly Color Lime = GalleryPalette.LimeHex;

	/// <summary>
	/// Composes a movie from shape-specific geometry lessons followed by applicable stroke
	/// lessons. Chapters are data, so adding a feature does not change playback or controls.
	/// </summary>
	static List<Chapter> Chapters( string shape )
	{
		List<Chapter> chapters = [];
		bool open = shape is "Line" or "Arc" or "Quadratic Bezier" or "Cubic Bezier";
		void Add( string title, string note, string code, DrawFeature draw ) => chapters.Add( new( title, note, code, draw ) );
		void Base( Painter p, Rect r, float u ) => Geometry( p, r, shape, u );
		void Ink( Painter p ) { p.Fill = open ? Fill.None : Cyan; p.Stroke = open ? Stroke.Solid( Pink, 5 ) : Stroke.None; }

		switch ( shape )
		{
			case "Capsule":
				Add( "Equal radii", "Two equal discs joined along their tangents form a capsule.", "painter.Capsule( from, to, fromRadius: 20 + 35*u, toRadius: 20 + 35*u );", ( p, r, u ) => { Ink( p ); p.Capsule( r.Center - new Vector2( 90, 0 ), r.Center + new Vector2( 90, 0 ), 20 + 35 * u, 20 + 35 * u ); } );
				Add( "Uneven radii", "Grow one end while the other stays fixed. The sides remain tangent to both discs.", "painter.Capsule( from, to, fromRadius: 25, toRadius: 10 + 70*u );", ( p, r, u ) => { Ink( p ); p.Capsule( r.Center - new Vector2( 90, 0 ), r.Center + new Vector2( 90, 0 ), 25, 10 + 70 * u ); } );
				Add( "Endpoints", "Move one endpoint vertically; the tapered body follows both centers.", "painter.Capsule( from, to + new Vector2( 0, -60 + 120*u ), 25, 50 );", ( p, r, u ) => { Ink( p ); p.Capsule( r.Center - new Vector2( 90, 0 ), r.Center + new Vector2( 90, -60 + 120 * u ), 25, 50 ); } );
				Add( "Contained endpoint", "As the smaller disc enters the larger one, the capsule becomes a circle.", "painter.Capsule( center, center + new Vector2( 120*u, 0 ), 65, 20 );", ( p, r, u ) => { Ink( p ); p.Capsule( r.Center, r.Center + new Vector2( 120 * u, 0 ), 65, 20 ); } );
				break;
			case "Crescent":
				Add( "Cutout offset", "Move the cutout from a fully covered moon toward a full disc.", "painter.Crescent( center, radius: 90, cutoutRadius: 90, cutoutOffset: new Vector2( 180*u, 0 ) );", ( p, r, u ) => { Ink( p ); p.Crescent( r.Center, 90, 90, new Vector2( 180 * u, 0 ) ); } );
				Add( "Cutout radius", "Increase the bite without moving its center. A smaller cutout can form an enclosed hole.", "painter.Crescent( center, 90, cutoutRadius: 20 + 110*u, cutoutOffset: new Vector2( 45, 0 ) );", ( p, r, u ) => { Ink( p ); p.Crescent( r.Center, 90, 20 + 110 * u, new Vector2( 45, 0 ) ); } );
				Add( "Direction", "Orbit the cutout around the moon to change which way the crescent faces.", "painter.Crescent( center, 90, 80, new Vector2( MathF.Cos(angle), MathF.Sin(angle) ) * 45 );", ( p, r, u ) => { Ink( p ); float angle = u * MathF.Tau; p.Crescent( r.Center, 90, 80, new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * 45 ); } );
				Add( "Outer radius", "Resize the moon while the cutout radius and offset stay fixed.", "painter.Crescent( center, radius: 45 + 55*u, cutoutRadius: 65, cutoutOffset: new Vector2( 40, -15 ) );", ( p, r, u ) => { Ink( p ); p.Crescent( r.Center, 45 + 55 * u, 65, new Vector2( 40, -15 ) ); } );
				break;
			case "Heart":
				Add( "Size", "A single size keeps the round lobes and pointed base in proportion.", "painter.Heart( center, size: 30 + 170*u );", ( p, r, u ) => { Ink( p ); p.Heart( r.Center, 30 + 170 * u ); } );
				break;
			case "Star":
				Add( "Body radius", "Grow the body while every spike extends 40 pixels beyond it.", "painter.Star( center, bodyRadius: 10 + 45*u, spikeLength: 40 );", ( p, r, u ) => { Ink( p ); p.Star( r.Center, 10 + 45 * u, 40 ); } );
				Add( "Spike length", "Extend the tips from a fixed body to form a pointed star.", "painter.Star( center, bodyRadius: 40, spikeLength: 60*u );", ( p, r, u ) => { Ink( p ); p.Star( r.Center, 40, 60 * u ); } );
				Add( "Point count", "Compare stars with three through eight points.", "painter.Star( center, 40, 50, points: 3 + (int)(5*u) );", ( p, r, u ) => { Ink( p ); p.Star( r.Center, 40, 50, 3 + (int)(5 * u) ); } );
				Add( "Rotation", "Turn the star without changing its radii or number of points.", "painter.Star( center, 40, 50, rotation: -90 + 360*u );", ( p, r, u ) => { Ink( p ); p.Star( r.Center, 40, 50, rotation: -90 + 360 * u ); } );
				break;
			case "Cross":
				Add( "Width", "Widen both bars together until the cross becomes a square.", "painter.Cross( center, width: 8 + 172*u, length: 180 );", ( p, r, u ) => { Ink( p ); p.Cross( r.Center, 8 + 172 * u, 180 ); } );
				Add( "Length", "Extend the arms while the bar thickness stays fixed.", "painter.Cross( center, width: 40, length: 40 + 140*u );", ( p, r, u ) => { Ink( p ); p.Cross( r.Center, 40, 40 + 140 * u ); } );
				Add( "Single contour", "One filled polygon keeps the center evenly translucent and the outline free of internal seams.", "painter.Fill = cyan.WithAlpha( 0.25f + 0.5f*u );\npainter.Stroke = Stroke.Solid( pink, 5 );\npainter.Cross( center, 45, 180 );", ( p, r, u ) => { p.Fill = Cyan.WithAlpha( 0.25f + 0.5f * u ); p.Stroke = Stroke.Solid( Pink, 5 ); p.Cross( r.Center, 45, 180 ); } );
				break;
			case "Tick":
				Add( "Width", "Thicken the check mark while the centerline stays fixed.", "painter.Tick( center, width: 4 + 40*u, length: 180 );", ( p, r, u ) => { Ink( p ); p.Tick( r.Center, 4 + 40 * u, 180 ); } );
				Add( "Length", "Extend both arms while keeping their thickness fixed.", "painter.Tick( center, width: 20, length: 50 + 150*u );", ( p, r, u ) => { Ink( p ); p.Tick( r.Center, 20, 50 + 150 * u ); } );
				Add( "Single contour", "A continuous contour keeps the bend free of overlapping fill and internal outlines.", "painter.Fill = cyan.WithAlpha( 0.25f + 0.5f*u );\npainter.Stroke = Stroke.Solid( pink, 5 );\npainter.Tick( center, 30, 180 );", ( p, r, u ) => { p.Fill = Cyan.WithAlpha( 0.25f + 0.5f * u ); p.Stroke = Stroke.Solid( Pink, 5 ); p.Tick( r.Center, 30, 180 ); } );
				break;
			case "Rectangle":
				Add( "Width and height", "Change width while height stays fixed. The dotted rectangle is the maximum extent.", "painter.Rect( new Rect( center - size / 2, size ) );", ( p, r, u ) =>
				{ BoundsGuide( p, r ); Ink( p ); var size = new Vector2( 40 + u * 280, r.Height ); p.Rect( new Rect( r.Center - size / 2, size ) ); } );
				Add( "Rounded corners", "A rectangle becomes a capsule as the radius grows. Only the corner radius changes.", "painter.Rect( bounds, 90 * u );", ( p, r, u ) => { Ink( p ); p.Rect( r, 90 * u ); } );
				Add( "Independent corners", "Each corner has its own radius; opposite corners round independently.", "painter.Rect( bounds, new Painter.CornerRadii( new(90*u,90*u), Vector2.Zero, new(90*u,90*u), Vector2.Zero ) );", ( p, r, u ) => { Ink( p ); p.Rect( r, new Painter.CornerRadii( new Vector2( 90 * u, 90 * u ), Vector2.Zero, new Vector2( 90 * u, 90 * u ), Vector2.Zero ) ); } );
				break;
			case "Circle":
				Add( "Radius", "The center stays fixed while the radius grows from a point to 90 pixels and back.", "painter.Circle( center, 90 * u );", ( p, r, u ) => { RadiusGuide( p, r.Center, 90 * u ); Ink( p ); p.Circle( r.Center, 90 * u ); } );
				Add( "Center", "Move the center along a guide without changing radius or scaling the circle.", "painter.Circle( center + new Vector2( -110 + 220*u, 0 ), 55 );", ( p, r, u ) => { GuideLine( p, r.Center - new Vector2( 110, 0 ), r.Center + new Vector2( 110, 0 ) ); Ink( p ); p.Circle( r.Center + new Vector2( -110 + 220 * u, 0 ), 55 ); } );

				Add( "Horizontal radius", "Keep the vertical radius fixed while the horizontal radius changes.", "painter.Circle( center, size ); // size = (40 + 280*u, 160)", ( p, r, u ) => { BoundsGuide( p, r ); Ink( p ); var size = new Vector2( 40 + 280 * u, 160 ); p.Circle( r.Center, size ); } );
				Add( "Vertical radius", "A wide ellipse grows into a circle. Width stays fixed this time.", "painter.Circle( center, size ); // size = (180, 20 + 160*u)", ( p, r, u ) => { Ink( p ); var size = new Vector2( 180, 20 + 160 * u ); p.Circle( r.Center, size ); } );
				break;
			case "Triangle":
			case "Quad":
			case "Polygon":
				Add( "Moving vertices", "The lime handles are the input vertices. Watch the contour follow them individually.", shape == "Triangle" ? "painter.Triangle( a, movingVertex, c );" : shape == "Quad" ? "painter.Quad( a, movingVertex, c, d );" : "painter.Polygon( vertices ); // animate vertices independently", ( p, r, u ) => { Ink( p ); VertexShape( p, r, shape, u, true ); } );
				if ( shape == "Polygon" )
					Add( "Convex to concave", "Alternate vertices move inward, turning a convex polygon into a concave star.", "painter.Polygon( vertices ); // alternate radii: 85 and 85 - 55*u", ( p, r, u ) => { Ink( p ); Star( p, r, 85 - 55 * u ); } );
				Add( "Winding", "The outline is traced in the opposite direction halfway through. The filled region stays the same.", "Array.Reverse( vertices );\npainter.Polygon( vertices );", ( p, r, u ) =>
				{
					var points = Vertices( r, shape, 0.5f );
					if ( u > 0.5f ) Array.Reverse( points );
					Ink( p ); DrawVertices( p, points, shape );
					p.Fill = Lime; p.Circle( points[0], 5 );
					p.Stroke = Stroke.Dashed( Pink, 3, 12, 8, u * 80 ); p.Fill = Fill.None; DrawVertices( p, points, shape );
				} );
				break;
			case "Line":
				Add( "Endpoints", "Two handles define the segment. One endpoint moves while the other stays fixed.", "painter.Line( from, to );", ( p, r, u ) => { Ink( p ); var a = r.BottomLeft; var b = new Vector2( r.Right, r.Bottom - u * r.Height ); p.Line( a, b ); Handles( p, [a, b] ); } );
				Add( "Connected segments", "A single span of points makes a continuous polyline. Its joins follow the moving vertices.", "painter.Line( points );", ( p, r, u ) => { Ink( p ); p.Stroke = Stroke.Solid( Pink, 10 ); p.Line( [r.BottomLeft, new Vector2( r.Center.x, r.Bottom - u * r.Height ), r.BottomRight] ); } );
				break;
			case "Arrow":
				Add( "Shaft width", "The shaft widens while the head and endpoints stay fixed.", "painter.Arrow( from, to, width: 2 + 38*u, headSize: 65 );", ( p, r, u ) => { Ink( p ); p.Arrow( new Vector2( r.Left, r.Center.y ), new Vector2( r.Right, r.Center.y ), 2 + 38 * u, 65 ); } );
				Add( "Head size", "The arrowhead grows independently of the shaft width.", "painter.Arrow( from, to, width: 16, headSize: 5 + 100*u );", ( p, r, u ) => { Ink( p ); p.Arrow( new Vector2( r.Left, r.Center.y ), new Vector2( r.Right, r.Center.y ), 16, 5 + 100 * u ); } );
				Add( "Direction", "Move the destination around the start point. Head and shaft remain aligned with the segment.", "painter.Arrow( from, to, width: 14, headSize: 45 );", ( p, r, u ) => { Ink( p ); float angle = u * MathF.Tau; p.Arrow( r.Center, r.Center + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * 100, 14, 45 ); } );
				break;
			case "Ring":
				Add( "Sweep angle", "Open a ring sector from zero to a full turn. A full ring has no radial seam.", "painter.Ring( center, 50, 90, startAngle: -90, sweepAngle: 360*u );", ( p, r, u ) => { Ink( p ); p.Ring( r.Center, 50, 90, -90, 360 * u ); } );
				Add( "Start angle", "Rotate a fixed 120-degree band around its center.", "painter.Ring( center, 50, 90, startAngle: -90 + 360*u, sweepAngle: 120 );", ( p, r, u ) => { Ink( p ); p.Ring( r.Center, 50, 90, -90 + 360 * u, 120 ); } );
				Add( "Reverse sweep", "Negative sweeps open the band counterclockwise from the same start angle.", "painter.Ring( center, 50, 90, startAngle: -90, sweepAngle: -300*u );", ( p, r, u ) => { Ink( p ); p.Ring( r.Center, 50, 90, -90, -300 * u ); } );
				Add( "Sector outline", "The stroke follows both curved edges and both radial edges as the band opens.", "painter.Fill = cyan;\npainter.Stroke = Stroke.Solid( pink, 5 );\npainter.Ring( center, 50, 90, -90, 30 + 300*u );", ( p, r, u ) => { p.Fill = Cyan; p.Stroke = Stroke.Solid( Pink, 5 ); p.Ring( r.Center, 50, 90, -90, 30 + 300 * u ); } );
				Add( "Inner radius", "Open the center of a disc to make an increasingly thin ring. Outer radius stays 90.", "painter.Ring( center, innerRadius: 82*u, outerRadius: 90 );", ( p, r, u ) => { Ink( p ); p.Ring( r.Center, 82 * u, 90 ); } );
				Add( "Outer radius", "The hole remains fixed while the outside grows. These are two independent radii.", "painter.Ring( center, innerRadius: 35, outerRadius: 36 + 54*u );", ( p, r, u ) => { Ink( p ); p.Ring( r.Center, 35, 36 + 54 * u ); } );
				break;
			case "Arc":
			case "Pie":
				Add( "Radius", "Grow the radius while start angle and sweep remain fixed.", $"painter.{shape}( center, radius: 10 + 80*u, startAngle: -90, sweepAngle: 240 );", ( p, r, u ) => { Ink( p ); if ( shape == "Arc" ) p.Arc( r.Center, 10 + 80 * u, -90, 240 ); else p.Pie( r.Center, 10 + 80 * u, -90, 240 ); } );
				Add( "Sweep angle", "Start at twelve o'clock and grow from zero to a full turn, then unwind.", $"painter.{shape}( center, radius: 90, startAngle: -90, sweepAngle: 360*u );", ( p, r, u ) => { Ink( p ); Sweep( p, r, shape, -90, 360 * u ); } );
				Add( "Start angle", "The sweep stays at 120 degrees while its start angle travels around the circle.", $"painter.{shape}( center, 90, startAngle: -90 + 360*u, sweepAngle: 120 );", ( p, r, u ) => { Ink( p ); Sweep( p, r, shape, -90 + 360 * u, 120 ); } );
				Add( "Reverse sweep", "Negative sweep draws in the opposite direction, from the same starting angle.", $"painter.{shape}( center, 90, startAngle: -90, sweepAngle: -300*u );", ( p, r, u ) => { Ink( p ); Sweep( p, r, shape, -90, -300 * u ); } );
				break;
			case "Quadratic Bezier":
			case "Cubic Bezier":
				Add( "Control points", "Dashed guides connect the endpoints to their control points. The curve follows the moving handles.", shape == "Quadratic Bezier" ? "painter.Bezier( start, control, end );" : "painter.Bezier( start, control1, control2, end );", ( p, r, u ) => { Ink( p ); Curve( p, r, shape, u, true ); } );
				Add( "Straight to curved", "Collinear controls produce a straight line. Lift the controls to bend it gradually.", shape == "Quadratic Bezier" ? "painter.Bezier( start, center + new Vector2( 0, -160*u ), end );" : "painter.Bezier( start, control1, control2, end ); // lift controls in opposite directions", ( p, r, u ) => { Ink( p ); Curve( p, r, shape, u, false ); } );
				break;
		}

		if ( !open )
		{
			Add( "Fill and outline", "The fill fades away to expose the stroked contour. Geometry stays fixed.", "painter.Fill = cyan.WithAlpha( 1-u );\npainter.Stroke = Stroke.Solid( pink, 5 );", ( p, r, u ) => { p.Fill = Cyan.WithAlpha( 1 - u ); p.Stroke = Stroke.Solid( Pink, 5 ); Base( p, r, 0.5f ); } );
			foreach ( var alignment in Enum.GetValues<Stroke.StrokeAlignment>() )
				Add( $"Stroke: {alignment.ToString().ToLowerInvariant()}", "The faint filled shape marks the original boundary. Watch where the growing stroke is placed.", $"painter.Stroke = Stroke.Solid( pink, 2 + 20*u ).WithAlignment( Stroke.StrokeAlignment.{alignment} );", ( p, r, u ) => { p.Fill = Cyan.WithAlpha( 0.2f ); p.Stroke = Stroke.Solid( Pink, 2 + 20 * u ).WithAlignment( alignment ); Base( p, r, 0.5f ); } );
		}
		else
		{
			Add( "Stroke width", "The path stays fixed while its stroke grows from a hairline to a thick band.", "painter.Stroke = Stroke.Solid( pink, 0.5f + 23.5f*u );", ( p, r, u ) => { p.Fill = Fill.None; p.Stroke = Stroke.Solid( Pink, 0.5f + 23.5f * u ); Base( p, r, 0.5f ); } );
			foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
				Add( $"Cap: {cap.ToString().ToLowerInvariant()}", "Increasing the stroke width makes the endpoint's cap shape and extension easy to see.", $"painter.Stroke = Stroke.Solid( pink, 4 + 22*u ) with {{ Cap = Stroke.LineCap.{cap} }};", ( p, r, u ) => { p.Fill = Fill.None; p.Stroke = Stroke.Solid( Pink, 4 + 22 * u ) with { Cap = cap }; Base( p, r, 0.5f ); } );
		}
		if ( shape is "Line" or "Triangle" or "Quad" or "Polygon" )
			foreach ( var join in Enum.GetValues<Stroke.LineJoin>() )
				Add( $"Join: {join.ToString().ToLowerInvariant()}", "An acute corner changes shape. Watch the outer edge where the stroke segments meet.", $"painter.Stroke = Stroke.Solid( pink, 14 ) with {{ Join = Stroke.LineJoin.{join}, MiterLimit = 4 }};", ( p, r, u ) =>
				{
					p.Fill = Fill.None; p.Stroke = Stroke.Solid( Pink, 14 ) with { Join = join, MiterLimit = 4 };
					if ( shape == "Line" ) p.Line( [r.BottomLeft, new Vector2( r.Center.x, r.Top + 80 * u ), new Vector2( r.Center.x + 30 + 90 * u, r.Bottom )] );
					else VertexShape( p, r, shape, u, false );
				} );
		if ( shape is "Line" or "Triangle" or "Quad" or "Polygon" )
			Add( "Miter limit", "A fixed sharp corner switches from a bevel to a pointed miter as its allowed length increases.", "painter.Stroke = Stroke.Solid( pink, 14 ) with { Join = Stroke.LineJoin.Miter, MiterLimit = 1 + 9*u };", ( p, r, u ) =>
			{
				p.Fill = Fill.None; p.Stroke = Stroke.Solid( Pink, 14 ) with { Join = Stroke.LineJoin.Miter, MiterLimit = 1 + 9 * u };
				Vector2[] points = [new( r.Center.x - 35, r.Bottom ), new( r.Center.x, r.Top ), new( r.Center.x + 35, r.Bottom )];
				if ( shape == "Line" ) p.Line( points );
				else if ( shape == "Triangle" ) p.Triangle( points[0], points[1], points[2] );
				else if ( shape == "Quad" ) p.Quad( points[0], points[1], points[2], new Vector2( r.Center.x, r.Bottom + 15 ) );
				else p.Polygon( points );
			} );
		Add( "Dashes", "Dash phase travels continuously along the contour. The underlying geometry remains stationary.", "painter.Stroke = Stroke.Dashed( pink, 6, dashLength: 16, gap: 10, offset: 100*u );", ( p, r, u ) => { p.Fill = Fill.None; p.Stroke = Stroke.Dashed( Pink, 6, 16, 10, 100 * u ); Base( p, r, 0.5f ); } );
		Add( "Dots", "Round dots travel around the path. Compare the pattern at joins and at the endpoints.", "painter.Stroke = Stroke.Dotted( pink, 8, gap: 10, offset: 100*u );", ( p, r, u ) => { p.Fill = Fill.None; p.Stroke = Stroke.Dotted( Pink, 8, 10, 100 * u ); Base( p, r, 0.5f ); } );
		return chapters;
	}

	/// <summary>
	/// Draws a representative contour without changing caller-owned fill or stroke state.
	/// </summary>
	static void Geometry( Painter p, Rect r, string shape, float u )
	{
		switch ( shape )
		{
			case "Rectangle": p.Rect( r, 24 ); break;
			case "Circle": p.Circle( r.Center, 85 ); break;
			case "Star": p.Star( r.Center, 40, 50 ); break;
			case "Cross": p.Cross( r.Center, 45, 180 ); break;
			case "Tick": p.Tick( r.Center, 30, 180 ); break;
			case "Capsule": p.Capsule( r.Center - new Vector2( 80, 0 ), r.Center + new Vector2( 80, 0 ), 30, 65 ); break;
			case "Crescent": p.Crescent( r.Center, 90, 80, new Vector2( 45, -15 ) ); break;
			case "Heart": p.Heart( r.Center, 180 ); break;

			case "Triangle": case "Quad": case "Polygon": VertexShape( p, r, shape, u, false ); break;
			case "Line": p.Line( r.BottomLeft, r.TopRight ); break;
			case "Arrow": p.Arrow( new Vector2( r.Left, r.Center.y ), new Vector2( r.Right, r.Center.y ), 24, 70 ); break;
			case "Ring": p.Ring( r.Center, 55, 90 ); break;
			case "Arc": case "Pie": Sweep( p, r, shape, -90, 260 ); break;
			default: Curve( p, r, shape, u, false ); break;
		}
	}

	static void Sweep( Painter p, Rect r, string shape, float start, float sweep )
	{
		if ( shape == "Arc" ) p.Arc( r.Center, 90, start, sweep );
		else p.Pie( r.Center, 90, start, sweep );
	}

	static Vector2[] Vertices( Rect r, string shape, float u ) => shape switch
	{
		"Triangle" => [r.BottomLeft, new Vector2( r.Left + 60 + 200 * u, r.Top ), r.BottomRight],
		"Quad" => [new Vector2( r.Left + 90 * u, r.Top ), r.TopRight, new Vector2( r.Right - 90 * u, r.Bottom ), r.BottomLeft],
		_ => [r.TopLeft, r.TopRight, r.BottomRight, new Vector2( r.Center.x, r.Bottom - 140 * u ), r.BottomLeft]
	};

	static void DrawVertices( Painter p, Vector2[] points, string shape )
	{
		if ( shape == "Triangle" ) p.Triangle( points[0], points[1], points[2] );
		else if ( shape == "Quad" ) p.Quad( points[0], points[1], points[2], points[3] );
		else p.Polygon( points );
	}

	static void VertexShape( Painter p, Rect r, string shape, float u, bool handles )
	{
		var points = Vertices( r, shape, u );
		DrawVertices( p, points, shape );
		if ( handles ) Handles( p, points );
	}

	static void Star( Painter p, Rect r, float inner )
	{
		Span<Vector2> points = stackalloc Vector2[10];
		for ( int i = 0; i < points.Length; i++ )
		{
			float angle = -MathF.PI / 2 + i * MathF.Tau / points.Length;
			points[i] = r.Center + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * (i % 2 == 0 ? 90 : inner);
		}
		p.Polygon( points );
		Handles( p, points );
	}

	static void Curve( Painter p, Rect r, string shape, float u, bool guides )
	{
		var a = new Vector2( r.Left, r.Center.y );
		var b = new Vector2( r.Right, r.Center.y );
		var c = new Vector2( r.Center.x, r.Center.y - 160 * u );
		var d = new Vector2( r.Right - 60, r.Center.y + 160 * u );
		if ( guides )
		{
			GuideLine( p, a, c ); GuideLine( p, c, shape == "Quadratic Bezier" ? b : d );
			if ( shape == "Cubic Bezier" ) GuideLine( p, d, b );
		}
		if ( shape == "Quadratic Bezier" ) p.Bezier( a, c, b );
		else p.Bezier( a, c, d, b );
		if ( guides ) Handles( p, shape == "Quadratic Bezier" ? [a, c, b] : [a, c, d, b] );
	}

	static void GuideLine( Painter p, Vector2 a, Vector2 b )
	{
		using var scope = p.Scope();
		p.Fill = Fill.None; p.Stroke = Stroke.Dashed( Lime.WithAlpha( 0.5f ), 1, 4, 4 ); p.Line( a, b );
	}

	static void BoundsGuide( Painter p, Rect r )
	{
		using var scope = p.Scope();
		p.Fill = Fill.None; p.Stroke = Stroke.Dotted( Lime.WithAlpha( 0.5f ), 2, 5 ); p.Rect( r );
	}

	static void RadiusGuide( Painter p, Vector2 center, float radius ) => GuideLine( p, center, center + new Vector2( radius, 0 ) );

	static void Handles( Painter p, ReadOnlySpan<Vector2> points )
	{
		using var scope = p.Scope();
		p.Fill = Lime; p.Stroke = Stroke.Solid( Lime, 2 );
		foreach ( var point in points ) p.Circle( point, 4 );
	}
}
