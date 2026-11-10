namespace Sandbox.PanelGallery;

public partial class DrawPage
{
	void PolylineExamples()
	{
		var examples = Examples( "Connected lines / chart" );
		Color cyan = GalleryPalette.CyanHex, pink = GalleryPalette.PinkHex;
		int pointCount = 12;
		float amplitude = 75, width = 6, miterLimit = 4;
		var join = Stroke.LineJoin.Round;
		var pattern = BorderStyle.Solid;
		Sample( examples, "One continuous path", "Joins connect adjacent segments; dash spacing continues along the entire strip. The miter limit controls sharp corners when Miter is selected.", ( painter, r ) =>
		{
			Span<Vector2> points = stackalloc Vector2[pointCount];
			for ( int i = 0; i < points.Length; i++ )
			{
				float t = i / (float)(points.Length - 1);
				points[i] = new Vector2( r.Left + 20 + t * (r.Width - 40), r.Center.y - MathF.Sin( t * MathF.Tau * 2 ) * amplitude );
			}
			painter.Stroke = Stroke.Solid( cyan, width ) with
			{
				Join = join,
				MiterLimit = miterLimit,
				Cap = Stroke.LineCap.Round,
				Style = pattern,
				DashLength = 14,
				Gap = 10
			};
			painter.Line( points );
		}, animateBounds: false );

		var smoothExamples = Examples( "Smooth lines / through points" );
		foreach ( var smoothPattern in new[] { BorderStyle.Solid, BorderStyle.Dashed, BorderStyle.Dotted } )
		{
			Sample( smoothExamples, smoothPattern.ToString(), "The smooth line passes through every point. The faint straight line shows the original segments; dash spacing continues through the curve.", ( painter, r ) =>
			{
				Span<Vector2> points = [r.TopLeft + new Vector2( 20, r.Height * 0.7f ),
					r.TopLeft + new Vector2( r.Width * 0.3f, 25 ),
					r.TopLeft + new Vector2( r.Width * 0.5f, r.Height - 25 ),
					r.TopRight + new Vector2( -20, r.Height * 0.3f )];
				painter.Stroke = Stroke.Solid( cyan.WithAlpha( 0.25f ), 1 );
				painter.Line( points );
				painter.Stroke = Stroke.Solid( cyan, 4 ) with { Style = smoothPattern, Cap = Stroke.LineCap.Round };
				painter.LineSmooth( points );
				painter.Stroke = Stroke.None;
				painter.Fill = pink;
				foreach ( var point in points ) painter.Circle( point, 4 );
			}, animateBounds: false );
		}

		var row = Examples( "Connected segments / sharp joins" );
		foreach ( var corner in Enum.GetValues<Stroke.LineJoin>() )
		{
			Sample( row, corner.ToString(), "A single strip with acute corners. Miter extends the edges, Bevel cuts the tip, and Round rounds it off.", ( painter, r ) =>
			{
				painter.Translate( r.Center );
				painter.Stroke = Stroke.Solid( cyan, 16 ) with { Join = corner, MiterLimit = 8 };
				painter.Line( [new( -150, 65 ), new( -70, -65 ), new( -35, 65 ), new( 70, -65 ), new( 150, 65 )] );
			}, animateBounds: false );
		}
		row = Examples( "Continuous patterns / closed strips" );
		foreach ( var strokePattern in new[] { BorderStyle.Solid, BorderStyle.Dashed, BorderStyle.Dotted } )
		{
			Sample( row, strokePattern.ToString(), "Closing the strip joins the last point back to the first, including the closing corner. There are no endpoint caps.", ( painter, r ) =>
			{
				painter.Stroke = Stroke.Solid( pink, 6 ) with
				{
					Style = strokePattern,
					DashLength = 14,
					Gap = 10,
					Join = Stroke.LineJoin.Round
				};
				var box = r.Shrink( 30 );
				painter.Fill = Fill.None;
				painter.Polygon( [box.BottomLeft, box.TopLeft, box.TopRight, box.BottomRight] );
			}, animateBounds: false );
		}
	}
}
