using System.Buffers;

using Sandbox.UI;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Draws a path using the current <see cref="Stroke"/> in drawing pixels, with no fixed vertex limit.
	/// The path is open. Use Polygon with Fill.None for a closed contour. Subpixel strokes use coverage fading.
	/// </summary>
	public void Line( ReadOnlySpan<Vector2> points )
	{
		var context = ActiveContext;
		Path.DrawPolyline( context, points, in context.State.Stroke );
	}

	/// <summary>
	/// Draws a line using the current <see cref="Stroke"/> in drawing pixels. Subpixel strokes use coverage fading.
	/// </summary>
	public void Line( Vector2 from, Vector2 to )
	{
		var context = ActiveContext;
		Path.DrawSimpleLine( context, from, to, in context.State.Stroke );
	}

	/// <summary>
	/// Draws a line using <see cref="Stroke"/> and its X/Y coordinates in drawing pixels. Z is ignored.
	/// </summary>
	public void Line( Line line )
		=> Line( (Vector2)line.Start, (Vector2)line.End );

	/// <summary>
	/// Draws a polygon using <see cref="Fill"/> and the current <see cref="Stroke"/>. Uses the even-odd fill rule.
	/// Requires at least 3 vertices, with no fixed upper limit. Concave shapes and either winding are supported.
	/// The closing edge is implicit. Use Fill.None to draw only the stroke.
	/// </summary>
	public void Polygon( ReadOnlySpan<Vector2> points )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( points.Length, 3 );
		var context = ActiveContext;
		ref var state = ref context.State;
		if ( Path.TryDrawOutlinedPolygon( context, points ) ) return;
		if ( !state.Fill.IsTransparent ) Path.DrawPolygon( context, points, in state.Fill );
		if ( HasStroke( state.Stroke ) ) Path.DrawPolyline( context, points, in state.Stroke, closed: true );
	}

	/// <summary>
	/// Draws a triangle using <see cref="Fill"/> and the current <see cref="Stroke"/>, in drawing pixels.
	/// </summary>
	public void Triangle( Vector2 a, Vector2 b, Vector2 c )
		=> Polygon( [a, b, c] );

	/// <summary>
	/// Draws a triangle using <see cref="Fill"/>, <see cref="Stroke"/> and its X/Y coordinates in drawing pixels. Z is ignored.
	/// </summary>
	public void Triangle( Triangle triangle )
		=> Triangle( (Vector2)triangle.A, (Vector2)triangle.B, (Vector2)triangle.C );

	/// <summary>
	/// Draws a quad using <see cref="Fill"/> and the current <see cref="Stroke"/>. Vertices follow the perimeter in drawing pixels.
	/// </summary>
	public void Quad( Vector2 a, Vector2 b, Vector2 c, Vector2 d )
		=> Polygon( [a, b, c, d] );

	/// <summary>
	/// Draws a rectangle using Fill and Stroke. Pass a single radius for all corners,
	/// or CornerRadii for elliptical corners ordered top-left, top-right, bottom-right, bottom-left.
	/// Radii are in drawing pixels and follow CSS overlap clamping; negative or non-finite radii become zero.
	/// </summary>
	public void Rect( Rect rect, CornerRadii corners = default ) => DrawRect( rect, corners.Resolve( rect ) );

	/// <summary>
	/// Draw a filled box with independent border widths using the current drawing state.
	/// </summary>
	internal void BorderedRect( Rect rect, Color color, Vector4 cornerRadius, Vector4 borderWidth, Color borderColor )
		=> Add( ActiveContext, new BoxDescriptor( rect, color )
		{
			BorderRadius = cornerRadius,
			Stroke = ResolveBoxStroke( borderWidth, borderColor, borderColor, borderColor, borderColor, Stroke.Style ),
		} );

	internal void DrawRect( Rect rect, BorderRadii radii, BorderShape shape = null )
	{
		if ( !ValidBounds( rect ) ) return;
		var context = ActiveContext;
		ref var state = ref context.State;
		var combined = TryGetBoxStroke( state.Stroke, out var border );
		if ( !state.Fill.IsTransparent || combined )
		{
			state.Fill.CreateDescriptor( rect, context, out var desc );
			desc.Radii = radii;
			SetBorderShape( ref desc, shape );
			if ( combined ) desc.Stroke = border.WithAlphaMultiplied( context.InheritedOpacity );
			Add( context, desc );
		}
		if ( !combined ) StrokeRect( rect, radii, Stroke );
	}

	/// <summary>
	/// Adds a fully resolved box in layout coordinates. Opacity and blend mode are already baked in and drawing state is ignored.
	/// </summary>
	internal void Resolved( in BoxDescriptor descriptor ) => GetActiveContext().Batcher.Add( descriptor );

	/// <summary>
	/// Draws a CSS box in layout coordinates, using the panel destination without custom drawing state.
	/// </summary>
	internal void Rect( in BoxDescriptor descriptor )
	{
		if ( !ValidBounds( descriptor.Rect ) ) return;
		var context = GetActiveContext();
		context.Batcher.Add( descriptor, context.InheritedOpacity, ResolveBlendMode( descriptor, context.InitialBlendMode ) );
	}

	internal void Outline( Rect rect, Color color, float width, BorderRadii radii, float offset )
	{
		var context = GetActiveContext();
		context.Batcher.Add( new OutlineDescriptor( rect, color.WithAlphaMultiplied( context.InheritedOpacity ), width )
		{
			Radii = radii,
			Offset = offset,
			OverrideBlendMode = context.InitialBlendMode
		}, Matrix.Identity, -1 );
	}

	void StrokeRect( Rect rect, BorderRadii radii, Stroke stroke )
	{
		if ( !HasStroke( stroke ) ) return;
		if ( radii.IsZero )
		{
			Path.DrawPolyline( ActiveContext, [rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft], stroke, closed: true );
			return;
		}

		Span<Vector2> sizes = [radii.TopLeft, radii.TopRight, radii.BottomRight, radii.BottomLeft];
		Span<Vector2> centers = [rect.TopLeft + radii.TopLeft,
			rect.TopRight + new Vector2( -radii.TopRight.x, radii.TopRight.y ),
			rect.BottomRight - radii.BottomRight,
			rect.BottomLeft + new Vector2( radii.BottomLeft.x, -radii.BottomLeft.y )];
		Span<int> counts = stackalloc int[4];
		int count = 0;
		for ( int i = 0; i < 4; i++ )
		{
			counts[i] = sizes[i].x <= 0 ? 1 : CurveSegments( MathF.Max( sizes[i].x, sizes[i].y ), 90 ) + 1;
			count += counts[i];
		}
		var rented = ArrayPool<Vector2>.Shared.Rent( count );
		try
		{
			var points = rented.AsSpan( 0, count );
			int offset = 0;
			for ( int i = 0; i < 4; i++ )
			{
				SampleArc( points.Slice( offset, counts[i] ), centers[i], sizes[i], 180 + i * 90, 90 );
				offset += counts[i];
			}
			Path.DrawPolyline( ActiveContext, points, stroke, closed: true );
		}
		finally
		{
			ArrayPool<Vector2>.Shared.Return( rented );
		}
	}

	/// <summary>
	/// Draws a circle using <see cref="Fill"/> and the current <see cref="Stroke"/>. Radius and width are in drawing pixels.
	/// </summary>
	public void Circle( Vector2 center, float radius )
	{
		if ( !float.IsFinite( radius ) || radius <= 0 ) return;
		var rect = new Rect( center - new Vector2( radius ), new Vector2( radius * 2 ) );
		if ( !ValidBounds( rect ) ) return;
		FillEllipse( rect, Fill );
		if ( HasStroke( Stroke ) ) Arc( center, radius, 0, 360 );
	}

	/// <summary>
	/// Draws an ellipse fitted to the bounds, using <see cref="Fill"/> and the current <see cref="Stroke"/>, in drawing pixels.
	/// </summary>
	public void Circle( Rect rect )
	{
		if ( !ValidBounds( rect ) ) return;
		var radius = rect.Size * 0.5f;
		FillEllipse( rect, Fill );
		if ( !HasStroke( Stroke ) ) return;
		int count = CurveSegments( MathF.Max( radius.x, radius.y ), 360 );
		var rented = ArrayPool<Vector2>.Shared.Rent( count );
		try
		{
			var points = rented.AsSpan( 0, count );
			SampleArc( points, rect.Center, radius, 0, 360, true );
			Path.DrawPolyline( ActiveContext, points, Stroke, closed: true );
		}
		finally
		{
			ArrayPool<Vector2>.Shared.Return( rented );
		}
	}

	/// <summary>
	/// Draws an elliptical circle centered at <paramref name="center"/> with the given full width
	/// and height in drawing pixels, using <see cref="Fill"/> and the current <see cref="Stroke"/>.
	/// </summary>
	public void Circle( Vector2 center, Vector2 size )
		=> Circle( new Rect( center - size * 0.5f, size ) );

	/// <summary>
	/// Draws the band between inner and outer radii using <see cref="Fill"/>, with the current
	/// <see cref="Stroke"/> on both circular edges. Inside/outside alignment is relative to the band.
	/// Radii are in drawing pixels.
	/// A zero inner radius draws a circle. Negative, non-finite, equal or reversed radii draw nothing.
	/// </summary>
	public void Ring( Vector2 center, float innerRadius, float outerRadius )
	{
		if ( !center.IsFinite || !float.IsFinite( innerRadius ) || !float.IsFinite( outerRadius )
			|| innerRadius < 0 || outerRadius <= innerRadius ) return;
		if ( innerRadius == 0 )
		{
			Circle( center, outerRadius );
			return;
		}

		if ( !Fill.IsTransparent )
		{
			// A solid circular band uses the analytic arc geometry, with paint mapped over its full bounds.
			var width = outerRadius - innerRadius;
			Path.DrawArc( ActiveContext, center, innerRadius + width * 0.5f, 0, 360, new Stroke( Fill, width ) );
		}
		if ( !HasStroke( Stroke ) ) return;
		var context = ActiveContext;
		if ( !context.State.HasArea || context.State.Opacity == 0 ) return;
		if ( Stroke.Alignment != Stroke.StrokeAlignment.Center && !float.IsFinite( Stroke.Width * 2 ) ) return;

		Path.AlignmentMask? mask = Stroke.Alignment == Stroke.StrokeAlignment.Center ? null : Path.CircleMask( context, center, outerRadius, innerRadius );
		Path.DrawArc( ActiveContext, center, outerRadius, 0, 360, Stroke, mask );
		Path.DrawArc( ActiveContext, center, innerRadius, 0, 360, Stroke, mask );
	}

	/// <summary>
	/// Draws a ring sector using Fill and Stroke, including both radial edges. Angles are clockwise
	/// degrees from right; negative sweeps run counterclockwise. A full sweep has no radial seams.
	/// A zero inner radius draws a pie. Invalid radii or angles and zero sweeps draw nothing.
	/// </summary>
	public void Ring( Vector2 center, float innerRadius, float outerRadius, float startAngle, float sweepAngle )
	{
		if ( !center.IsFinite || !float.IsFinite( innerRadius ) || !float.IsFinite( outerRadius )
			|| innerRadius < 0 || outerRadius <= innerRadius || !float.IsFinite( startAngle )
			|| !float.IsFinite( sweepAngle ) || sweepAngle == 0 ) return;
		if ( MathF.Abs( sweepAngle ) >= 360 )
		{
			Ring( center, innerRadius, outerRadius );
			return;
		}
		if ( innerRadius == 0 )
		{
			Pie( center, outerRadius, startAngle, sweepAngle );
			return;
		}

		int count = CurveSegments( outerRadius, sweepAngle ) + 1;
		var rented = ArrayPool<Vector2>.Shared.Rent( count * 2 );
		try
		{
			// Trace the outside forward and the inside backward to form one closed band contour.
			// Polygon applies fill and stroke alignment to the sector, including its radial edges.
			var points = rented.AsSpan( 0, count * 2 );
			startAngle %= 360;
			SampleArc( points[..count], center, new Vector2( outerRadius ), startAngle, sweepAngle );
			SampleArc( points[count..], center, new Vector2( innerRadius ), startAngle + sweepAngle, -sweepAngle );
			Polygon( points );
		}
		finally
		{
			ArrayPool<Vector2>.Shared.Return( rented );
		}
	}

	/// <summary>
	/// Draws an arc using the current <see cref="Stroke"/> with angles in clockwise degrees from right. Negative sweeps run counterclockwise.
	/// Radius measures to the centerline in drawing pixels. Sweeps of at least 360 degrees form a closed ring.
	/// </summary>
	public void Arc( Vector2 center, float radius, float startAngle, float sweepAngle )
		=> Path.DrawArc( ActiveContext, center, radius, startAngle, sweepAngle, Stroke );

	/// <summary>
	/// Draws a pie sector using <see cref="Fill"/> and the current <see cref="Stroke"/>. Angles are clockwise degrees from right; negative sweeps are supported.
	/// A full sweep draws a circle without radial seams.
	/// </summary>
	public void Pie( Vector2 center, float radius, float startAngle, float sweepAngle )
	{
		if ( !center.IsFinite || !float.IsFinite( radius ) || radius <= 0
			|| !float.IsFinite( startAngle ) || !float.IsFinite( sweepAngle ) || sweepAngle == 0 ) return;
		if ( MathF.Abs( sweepAngle ) >= 360 )
		{
			Circle( center, radius );
			return;
		}
		int count = CurveSegments( radius, sweepAngle ) + 2;
		var rented = ArrayPool<Vector2>.Shared.Rent( count );
		try
		{
			var points = rented.AsSpan( 0, count );
			points[0] = center;
			SampleArc( points[1..], center, new Vector2( radius ), startAngle, sweepAngle );
			Polygon( points );
		}
		finally
		{
			ArrayPool<Vector2>.Shared.Return( rented );
		}
	}

	/// <summary>
	/// Draws a quadratic Bezier using <see cref="Stroke"/>.
	/// </summary>
	public void Bezier( Vector2 from, Vector2 control, Vector2 to )
		=> Bezier( from, from + (control - from) * (2f / 3f), to + (control - to) * (2f / 3f), to );

	/// <summary>
	/// Draws a cubic Bezier using <see cref="Stroke"/>.
	/// </summary>
	public void Bezier( Vector2 from, Vector2 control1, Vector2 control2, Vector2 to )
	{
		if ( !HasStroke( Stroke ) || !from.IsFinite || !control1.IsFinite || !control2.IsFinite || !to.IsFinite ) return;
		var points = new CurvePoints( from );
		try
		{
			FlattenBezier( ref points, from, control1, control2, to );
			Line( points.Span );
		}
		finally
		{
			points.Dispose();
		}
	}

	/// <summary>
	/// Grows pooled point storage as adaptive subdivision discovers the curve's required resolution.
	/// </summary>
	ref struct CurvePoints
	{
		Vector2[] _points;
		int _count;

		/// <summary>
		/// Starts a curve with its first endpoint.
		/// </summary>
		public CurvePoints( Vector2 first )
		{
			_points = ArrayPool<Vector2>.Shared.Rent( 16 );
			_points[0] = first;
			_count = 1;
		}

		/// <summary>
		/// Points appended so far, in subdivision order.
		/// </summary>
		public readonly ReadOnlySpan<Vector2> Span => _points.AsSpan( 0, _count );

		/// <summary>
		/// Appends an endpoint, returning superseded storage to the pool after growth.
		/// </summary>
		public void Add( Vector2 point )
		{
			if ( _count == _points.Length )
			{
				var grown = ArrayPool<Vector2>.Shared.Rent( checked(_count * 2) );
				_points.AsSpan( 0, _count ).CopyTo( grown );
				ArrayPool<Vector2>.Shared.Return( _points );
				_points = grown;
			}

			_points[_count++] = point;
		}

		/// <summary>
		/// Returns the final storage when drawing finishes or exits early.
		/// </summary>
		public readonly void Dispose() => ArrayPool<Vector2>.Shared.Return( _points );
	}

	static void FlattenBezier( ref CurvePoints points, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int depth = 0 )
	{
		if ( depth == 12 || (DistanceToSegmentSquared( b, a, d ) <= 0.0625 && DistanceToSegmentSquared( c, a, d ) <= 0.0625) )
		{
			points.Add( d );
			return;
		}
		var ab = a * 0.5f + b * 0.5f;
		var bc = b * 0.5f + c * 0.5f;
		var cd = c * 0.5f + d * 0.5f;
		var abc = ab * 0.5f + bc * 0.5f;
		var bcd = bc * 0.5f + cd * 0.5f;
		var middle = abc * 0.5f + bcd * 0.5f;
		FlattenBezier( ref points, a, ab, abc, middle, depth + 1 );
		FlattenBezier( ref points, middle, bcd, cd, d, depth + 1 );
	}

	/// <summary>
	/// Draws a star using Fill and Stroke. Body radius measures to the valleys; spike length
	/// extends beyond it to the tips, in drawing pixels. Rotation is clockwise degrees from right.
	/// At least three points and a positive body radius are required. Zero spike length draws a regular polygon.
	/// </summary>
	public void Star( Vector2 center, float bodyRadius, float spikeLength, int points = 5, float rotation = -90 )
	{
		float outerRadius = bodyRadius + spikeLength;
		if ( !center.IsFinite || !float.IsFinite( bodyRadius ) || bodyRadius <= 0
			|| !float.IsFinite( spikeLength ) || spikeLength < 0 || !float.IsFinite( outerRadius )
			|| !float.IsFinite( rotation ) || points < 3 || points > 4096 ) return;
		int count = points * 2;
		var rented = ArrayPool<Vector2>.Shared.Rent( count );
		try
		{
			var vertices = rented.AsSpan( 0, count );
			float start = (rotation % 360) * MathF.PI / 180;
			for ( int i = 0; i < count; i++ )
			{
				float angle = start + i * MathF.Tau / count;
				vertices[i] = center + new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * (i % 2 == 0 ? outerRadius : bodyRadius);
			}
			Polygon( vertices );
		}
		finally
		{
			ArrayPool<Vector2>.Shared.Return( rented );
		}
	}

	/// <summary>
	/// Draws a plus-shaped cross as one contour using Fill and Stroke, without overlapping bars.
	/// Width is the thickness of each bar; length is the full tip-to-tip span on both axes, in drawing pixels.
	/// Nonpositive, non-finite dimensions or a width greater than length draw nothing.
	/// </summary>
	public void Cross( Vector2 center, float width, float length )
	{
		if ( !center.IsFinite || !float.IsFinite( width ) || !float.IsFinite( length )
			|| width <= 0 || length <= 0 || width > length ) return;
		float w = width * 0.5f;
		float l = length * 0.5f;
		Polygon( [center + new Vector2( -w, -l ), center + new Vector2( w, -l ),
			center + new Vector2( w, -w ), center + new Vector2( l, -w ),
			center + new Vector2( l, w ), center + new Vector2( w, w ),
			center + new Vector2( w, l ), center + new Vector2( -w, l ),
			center + new Vector2( -w, w ), center + new Vector2( -l, w ),
			center + new Vector2( -l, -w ), center + new Vector2( -w, -w )] );
	}

	/// <summary>
	/// Draws a check mark as one filled contour using Fill and Stroke. Width is the arm thickness;
	/// length is the horizontal span of its centerline in drawing pixels. The long arm rises to the right.
	/// Nonpositive, non-finite dimensions or a width greater than half the length draw nothing.
	/// </summary>
	public void Tick( Vector2 center, float width, float length )
	{
		if ( !center.IsFinite || !float.IsFinite( width ) || !float.IsFinite( length )
			|| width <= 0 || length <= 0 || width > length * 0.5f ) return;
		var a = center + new Vector2( -0.5f, 0 ) * length;
		var b = center + new Vector2( -0.15f, 0.35f ) * length;
		var c = center + new Vector2( 0.5f, -0.35f ) * length;
		var n1 = (b - a).Normal.Perpendicular;
		var n2 = (c - b).Normal.Perpendicular;
		var miter = (n1 + n2) / (1 + Vector2.Dot( n1, n2 )) * (width * 0.5f);
		var offset1 = n1 * (width * 0.5f);
		var offset2 = n2 * (width * 0.5f);
		Polygon( [a + offset1, b + miter, c + offset2, c - offset2, b - miter, a - offset1] );
	}

	/// <summary>
	/// Draws a shafted arrow as one polygon using <see cref="Fill"/> and <see cref="Stroke"/>. All dimensions are in drawing pixels.
	/// Zero-length arrows or nonpositive shaft/head sizes draw nothing.
	/// </summary>
	/// <param name="from">Center of the shaft's starting edge.</param>
	/// <param name="to">Arrow tip.</param>
	/// <param name="width">Shaft width.</param>
	/// <param name="headSize">Head length and width. Length is limited to the arrow's length; width is at least the shaft width.</param>
	public void Arrow( Vector2 from, Vector2 to, float width = 4, float headSize = 16 )
	{
		var delta = to - from;
		var length = delta.Length;
		if ( !float.IsFinite( length ) || length <= 0 || !float.IsFinite( width ) || width <= 0 ) return;
		if ( !float.IsFinite( headSize ) || headSize <= 0 ) return;

		var direction = delta / length;
		var head = to - direction * MathF.Min( headSize, length );
		var side = direction.Perpendicular * (width * 0.5f);
		var headSide = direction.Perpendicular * (MathF.Max( headSize, width ) * 0.5f);
		Polygon( [from - side, head - side, head - headSide, to, head + headSide, head + side, from + side] );
	}

	/// <summary>
	/// Draws an outline using <see cref="Stroke"/>. At zero offset, its inner edge follows the rectangle.
	/// Positive offsets push the outline outward; negative offsets pull it inward. All dimensions are in drawing pixels.
	/// </summary>
	public void Outline( Rect rect, float cornerRadius = 0, float offset = 0 )
		=> Outline( rect, new CornerRadii( cornerRadius ), offset );

	/// <summary>
	/// Draws an outline using Stroke and elliptical corners. At zero offset, its inner edge follows the rectangle.
	/// Positive offsets move it outward; negative offsets move it inward. Radii use the same clamping as Rect.
	/// </summary>
	public void Outline( Rect rect, CornerRadii corners, float offset = 0 )
	{
		if ( !HasStroke( Stroke ) || !ValidBounds( rect ) || !float.IsFinite( offset ) ) return;
		var expansion = offset + Stroke.Width * 0.5f;
		var bounds = rect.Grow( expansion );
		if ( ValidBounds( bounds ) ) StrokeRect( bounds, corners.Resolve( rect ).Grow( expansion ).Clamped( bounds.Width, bounds.Height ), Stroke with { Alignment = Stroke.StrokeAlignment.Center } );
	}

	void FillEllipse( Rect rect, Fill fill )
	{
		if ( fill.IsTransparent ) return;
		var radius = rect.Size * 0.5f;
		fill.CreateDescriptor( rect, ActiveContext, out var desc );
		desc.Radii = new BorderRadii { TopLeft = radius, TopRight = radius, BottomLeft = radius, BottomRight = radius };
		Add( ActiveContext, desc );
	}

	internal static BoxStroke ResolveBoxStroke( in Stroke stroke )
	{
		TryGetBoxStroke( stroke, out var result );
		return result;
	}

	static double DistanceToSegmentSquared( Vector2 point, Vector2 from, Vector2 to )
	{
		double dx = (double)to.x - from.x, dy = (double)to.y - from.y;
		double px = (double)point.x - from.x, py = (double)point.y - from.y;
		var length = dx * dx + dy * dy;
		var t = length == 0 ? 0 : Math.Clamp( (px * dx + py * dy) / length, 0, 1 );
		px -= t * dx;
		py -= t * dy;
		return px * px + py * py;
	}

	static bool HasStroke( in Stroke stroke ) => !stroke.IsDisabled && float.IsFinite( stroke.Width ) && stroke.Width > 0 && !Stroke.GetFill( in stroke ).IsTransparent;

	static bool ValidBounds( Rect rect ) => rect.Position.IsFinite
		&& float.IsFinite( rect.Width ) && float.IsFinite( rect.Height ) && rect.Width > 0 && rect.Height > 0
		&& float.IsFinite( rect.Right ) && float.IsFinite( rect.Bottom );

	static float ValidRadius( float radius ) => float.IsFinite( radius ) ? MathF.Max( radius, 0 ) : 0;

	static int CurveSegments( float radius, float sweep )
	{
		// Sagitta <= 0.25px. Double precision keeps the angle useful for large radii.
		var step = 2 * Math.Acos( Math.Clamp( 1 - 0.25 / radius, -1, 1 ) );
		step = Math.Clamp( step, Math.PI / 8192, Math.PI / 2 );
		return (int)Math.Clamp( Math.Ceiling( Math.Abs( sweep ) * (Math.PI / 180) / step ), 1, 4096 );
	}

	static void SampleArc( Span<Vector2> points, Vector2 center, Vector2 radius, float startAngle, float sweepAngle, bool closed = false )
	{
		var start = (startAngle % 360) * (Math.PI / 180);
		var sweep = sweepAngle * (Math.PI / 180);
		int segments = closed ? points.Length : Math.Max( points.Length - 1, 1 );
		for ( int i = 0; i < points.Length; i++ )
		{
			var angle = start + sweep * i / segments;
			points[i] = center + new Vector2( (float)Math.Cos( angle ) * radius.x, (float)Math.Sin( angle ) * radius.y );
		}
	}

	/// <summary>
	/// Elliptical corner radii in drawing pixels. Each vector is (horizontal, vertical).
	/// </summary>
	public readonly record struct CornerRadii( Vector2 TopLeft, Vector2 TopRight, Vector2 BottomRight, Vector2 BottomLeft )
	{
		/// <summary>
		/// Uses the same circular radius for all four corners.
		/// </summary>
		public CornerRadii( float radius ) : this( new Vector2( radius ) ) { }

		/// <summary>
		/// Uses the same horizontal and vertical radii for all four corners.
		/// </summary>
		public CornerRadii( Vector2 radius ) : this( radius, radius, radius, radius ) { }

		/// <summary>
		/// Use the same circular radius for all four corners.
		/// </summary>
		public static implicit operator CornerRadii( float radius ) => new( radius );

		internal BorderRadii Resolve( Rect rect ) => new BorderRadii
		{
			TopLeft = Clean( TopLeft ),
			TopRight = Clean( TopRight ),
			BottomRight = Clean( BottomRight ),
			BottomLeft = Clean( BottomLeft ),
		}.Clamped( rect.Width, rect.Height );
		static Vector2 Clean( Vector2 radius ) => new( ValidRadius( radius.x ), ValidRadius( radius.y ) );
	}
}
