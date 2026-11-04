namespace Sandbox.UI;

public partial class CurveEditor
{
	sealed partial class CurveCanvas
	{
		Curve[] _areaCurves;
		Vector2 _areaMin, _areaMax;
		Rect _areaPlot;
		Vector2[] _area = [];

		Color CurveColor( int index ) => _editor.ChannelColor( index );

		public override void OnDraw( Painter painter )
		{
			base.OnDraw( painter );
			using var scope = painter.Scope();
			painter.Clip( Plot );
			DrawNormalRange( painter );
			if ( _editor.IsRange )
				DrawArea( painter );
			foreach ( int c in Enumerable.Range( 0, _editor._curves.Length ).OrderBy( c => c == _editor.ActiveCurve ) )
			{
				bool active = c == _editor.ActiveCurve;
				var color = CurveColor( c );
				DrawCurve( painter, _editor._curves[c], active ? color : color.WithAlpha( color.a * 0.5f ), active ? 1.5f : 1 );
			}
			if ( HasTangents )
			{
				var color = CurveColor( _editor.ActiveCurve );
				for ( int i = 0; i < 2; i++ )
				{
					var p = TangentPosition( i == 0 );
					painter.Stroke = Stroke.Solid( color.WithAlpha( 0.5f ), 1 );
					painter.Line( KeyPosition( PrimaryKey ), p );
				}
			}
		}

		void DrawNormalRange( Painter painter )
		{
			using var scope = painter.Scope();
			float top = CanvasToScreen( Vector2.One ).y;
			float bottom = CanvasToScreen( Vector2.Zero ).y;
			painter.Stroke = Stroke.None;
			painter.Fill = AxisColor.WithAlpha( 0.045f );
			if ( top > Plot.Top )
				painter.Rect( new Rect( Plot.Left, Plot.Top, Plot.Width, Math.Min( top, Plot.Bottom ) - Plot.Top ) );
			if ( bottom < Plot.Bottom )
				painter.Rect( new Rect( Plot.Left, Math.Max( bottom, Plot.Top ), Plot.Width, Plot.Bottom - Math.Max( bottom, Plot.Top ) ) );
		}

		void DrawCurve( Painter painter, Curve curve, Color color, float width )
		{
			if ( curve.Length == 0 )
				return;
			painter.Stroke = Stroke.Solid( color, width );
			Vector2 Point( float x, float y ) => CanvasToScreen( _editor.ToDisplay( curve, new( x, y ) ) );
			var first = curve.Frames[0];
			var last = curve.Frames[^1];
			painter.Line( CanvasToScreen( new( ViewMin.x, _editor.ToDisplay( curve, new( first.Time, first.Value ) ).y ) ), Point( first.Time, first.Value ) );
			for ( int i = 0; i < curve.Length - 1; i++ )
			{
				var a = curve.Frames[i];
				var b = curve.Frames[i + 1];
				var start = Point( a.Time, a.Value );
				var end = Point( b.Time, b.Value );
				if ( a.Mode == Curve.HandleMode.Stepped )
				{
					var corner = Point( b.Time, a.Value );
					painter.Line( start, corner );
					painter.Line( corner, end );
				}
				else if ( a.Mode == Curve.HandleMode.Linear )
				{
					painter.Line( start, end );
				}
				else
				{
					float dx = (b.Time - a.Time) / 3;
					float outgoing = a.Mode == Curve.HandleMode.Flat ? 0 : a.Out, incoming = b.Mode == Curve.HandleMode.Flat ? 0 : b.In;
					painter.Bezier( start, Point( a.Time + dx, a.Value + outgoing * dx ), Point( b.Time - dx, b.Value + incoming * dx ), end );
				}
			}
			painter.Line( Point( last.Time, last.Value ), CanvasToScreen( new( ViewMax.x, _editor.ToDisplay( curve, new( last.Time, last.Value ) ).y ) ) );
		}

		void DrawArea( Painter painter )
		{
			if ( _areaCurves != _editor._curves || _areaMin != ViewMin || _areaMax != ViewMax || _areaPlot != Plot )
			{
				_areaCurves = _editor._curves;
				_areaMin = ViewMin;
				_areaMax = ViewMax;
				_areaPlot = Plot;
				_area = BuildRangeArea( _editor.RangeValue, ViewMin.x, ViewMax.x, Math.Clamp( (int)(Plot.Width / 3), 32, 2048 ) ).Select( CanvasToScreen ).ToArray();
			}
			painter.Stroke = Stroke.None;
			painter.Fill = CurveColor( 0 ).WithAlpha( 0.12f );
			painter.Polygon( _area );
		}

		Vector2 Snap( Vector2 point ) => _editor.SnapPoint( point );

	}

	/// <summary>
	/// Sample a filled envelope in curve A's normalized display space, retaining both boundaries' key times and steps.
	/// </summary>
	internal static Vector2[] BuildRangeArea( CurveRange range, float min, float max, int samples )
	{
		var times = new SortedSet<float>();
		samples = Math.Clamp( samples, 2, 2048 );
		for ( int i = 0; i <= samples; i++ )
			times.Add( min + (max - min) * i / samples );
		float DisplayTime( Curve curve, float x ) => (curve.TimeRange.x + x * (curve.TimeRange.y - curve.TimeRange.x) - range.A.TimeRange.x) / (range.A.TimeRange.y - range.A.TimeRange.x);
		foreach ( var curve in new[] { range.A, range.B } )
		{
			for ( int i = 0; i < curve.Length; i++ )
			{
				float t = DisplayTime( curve, curve.Frames[i].Time );
				if ( t < min || t > max )
					continue;
				times.Add( t );
				if ( i > 0 && curve.Frames[i - 1].Mode == Curve.HandleMode.Stepped )
					times.Add( Math.Max( min, t - Math.Max( MathF.Abs( t ) * 0.000001f, 0.000001f ) ) );
			}
		}
		var xs = times.ToArray();
		var result = new Vector2[xs.Length * 2];
		for ( int i = 0; i < xs.Length; i++ )
		{
			float time = range.A.TimeRange.x + xs[i] * (range.A.TimeRange.y - range.A.TimeRange.x);
			float a = (range.A.Evaluate( time ) - range.A.ValueRange.x) / (range.A.ValueRange.y - range.A.ValueRange.x);
			float b = (range.B.Evaluate( time ) - range.A.ValueRange.x) / (range.A.ValueRange.y - range.A.ValueRange.x);
			result[i] = new( xs[i], a );
			result[^(i + 1)] = new( xs[i], b );
		}
		return result;
	}
}
