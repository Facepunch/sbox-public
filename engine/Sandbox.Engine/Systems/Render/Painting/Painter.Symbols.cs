using System.Buffers;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Draws the convex hull of two discs using Fill and Stroke. Endpoint radii are in drawing pixels.
	/// Equal radii form a capsule; unequal radii taper along common tangents. A contained disc reduces to the larger circle.
	/// </summary>
	public void Capsule( Vector2 from, Vector2 to, float fromRadius, float toRadius )
	{
		if ( !from.IsFinite || !to.IsFinite || !float.IsFinite( fromRadius ) || !float.IsFinite( toRadius )
			|| fromRadius < 0 || toRadius < 0 ) return;
		var delta = to - from;
		float distance = delta.Length;
		if ( !float.IsFinite( distance ) ) return;
		if ( distance <= MathF.Abs( fromRadius - toRadius ) )
		{
			Circle( fromRadius >= toRadius ? from : to, MathF.Max( fromRadius, toRadius ) );
			return;
		}
		if ( fromRadius == 0 && toRadius == 0 ) return;
		var min = Vector2.Min( from - new Vector2( fromRadius ), to - new Vector2( toRadius ) );
		var max = Vector2.Max( from + new Vector2( fromRadius ), to + new Vector2( toRadius ) );
		var bounds = new Rect( min, max - min );
		SdfFill( bounds, new()
		{
			Kind = UICssBoxBatched.ShapeKind.Capsule,
			Circle = new Vector4( from.x - min.x, from.y - min.y, fromRadius, toRadius ),
			Polygon01 = new Vector4( to.x - min.x, to.y - min.y, 0, 0 )
		} );
		if ( !HasStroke( Stroke ) ) return;
		float angle = MathF.Atan2( delta.y, delta.x ) * 180 / MathF.PI;
		float tangent = MathF.Acos( Math.Clamp( (fromRadius - toRadius) / distance, -1, 1 ) ) * 180 / MathF.PI;
		StrokeArcPair( from, fromRadius, angle + tangent, 360 - 2 * tangent,
			to, toRadius, angle - tangent, 2 * tangent );
	}

	/// <summary>
	/// Draws a circle minus an offset circular cutout using Fill and Stroke. Dimensions and offset are in drawing pixels.
	/// Moving the cutout changes the moon's phase and direction. Disjoint circles leave a disc; a covering cutout draws nothing.
	/// </summary>
	public void Crescent( Vector2 center, float radius, float cutoutRadius, Vector2 cutoutOffset )
	{
		if ( !center.IsFinite || !cutoutOffset.IsFinite || !float.IsFinite( radius ) || radius <= 0
			|| !float.IsFinite( cutoutRadius ) || cutoutRadius < 0 ) return;
		float distance = cutoutOffset.Length;
		if ( !float.IsFinite( distance ) ) return;
		if ( distance + radius <= cutoutRadius ) return;
		if ( cutoutRadius == 0 || distance >= radius + cutoutRadius ) { Circle( center, radius ); return; }
		var cutout = center + cutoutOffset;
		var bounds = new Rect( center - new Vector2( radius ), new Vector2( radius * 2 ) );
		var shape = new UICssBoxBatched.BorderShape
		{
			Kind = UICssBoxBatched.ShapeKind.Crescent,
			Circle = new Vector4( radius, radius, radius, cutoutRadius ),
			Polygon01 = new Vector4( cutout.x - bounds.Left, cutout.y - bounds.Top, 0, 0 )
		};
		SdfFill( bounds, shape );
		if ( !HasStroke( Stroke ) ) return;
		if ( distance + cutoutRadius <= radius )
		{
			shape.Circle.x = center.x; shape.Circle.y = center.y;
			shape.Polygon01 = new Vector4( cutout.x, cutout.y, 0, 0 );
			var mask = new Path.Data( shape, [] );
			Path.DrawArc( ActiveContext, center, radius, 0, 360, Stroke, mask );
			Path.DrawArc( ActiveContext, cutout, cutoutRadius, 0, 360, Stroke, mask );
			return;
		}
		float direction = MathF.Atan2( cutoutOffset.y, cutoutOffset.x ) * 180 / MathF.PI;
		float outerAngle = (float)(Math.Acos( Math.Clamp( ((double)radius * radius + (double)distance * distance - (double)cutoutRadius * cutoutRadius) / (2.0 * radius * distance), -1, 1 ) ) * 180 / Math.PI);
		float innerAngle = (float)(Math.Acos( Math.Clamp( ((double)radius * radius - (double)distance * distance - (double)cutoutRadius * cutoutRadius) / (2.0 * cutoutRadius * distance), -1, 1 ) ) * 180 / Math.PI);
		StrokeArcPair( center, radius, direction + outerAngle, 360 - 2 * outerAngle,
			cutout, cutoutRadius, direction - innerAngle, -(360 - 2 * innerAngle), sharedEndpoints: true );
	}

	/// <summary>
	/// Draws a heart with two round lobes and a pointed base, using Fill and Stroke.
	/// Size is the full width in drawing pixels; its proportions are fixed and its bounds are centered at center.
	/// </summary>
	public void Heart( Vector2 center, float size )
	{
		if ( !center.IsFinite || !float.IsFinite( size ) || size <= 0 ) return;
		// Unit-heart proportions: lobe centres sit at (±lobeOffset, -lobeOffset) and the tip at (0, tip).
		// These are passed to the shader through the instance so the fill and the stroke share one definition.
		const float lobeRadius = 0.35355339f, lobeOffset = 0.25f, tip = 0.5f;
		float scale = size / (2 * lobeOffset + 2 * lobeRadius);
		var origin = center - new Vector2( 0, (tip - lobeOffset - lobeRadius) * 0.5f * scale );
		float height = (lobeOffset + lobeRadius + tip) * scale;
		var bounds = new Rect( center - new Vector2( size, height ) * 0.5f, new Vector2( size, height ) );
		SdfFill( bounds, new()
		{
			Kind = UICssBoxBatched.ShapeKind.Heart,
			Circle = new Vector4( origin.x - bounds.Left, origin.y - bounds.Top, scale, lobeRadius ),
			Polygon01 = new Vector4( lobeOffset, lobeOffset, tip, 0 )
		} );
		if ( !HasStroke( Stroke ) ) return;
		int count = CurveSegments( lobeRadius * scale, 180 ) + 1;
		var rented = ArrayPool<Vector2>.Shared.Rent( count * 2 + 1 );
		try
		{
			var points = rented.AsSpan( 0, count * 2 + 1 );
			SampleArc( points[..count], origin + new Vector2( lobeOffset, -lobeOffset ) * scale, new Vector2( lobeRadius * scale ), 225, 180 );
			points[count] = origin + new Vector2( 0, tip ) * scale;
			SampleArc( points[(count + 1)..], origin + new Vector2( -lobeOffset, -lobeOffset ) * scale, new Vector2( lobeRadius * scale ), 135, 180 );
			points[^1] = points[0];
			Path.DrawPolyline( ActiveContext, points, Stroke, closed: true );
		}
		finally { ArrayPool<Vector2>.Shared.Return( rented ); }
	}

	/// <summary>
	/// Submits an analytic distance-field fill. Shape parameters are local to bounds; Painter state supplies paint and transforms.
	/// </summary>
	void SdfFill( Rect bounds, UICssBoxBatched.BorderShape shape )
	{
		if ( Fill.IsTransparent || !ValidBounds( bounds ) ) return;
		var desc = Fill.CreateDescriptor( bounds, ActiveContext );
		desc.BorderShapeData = shape;
		Add( ActiveContext, desc );
	}

	/// <summary>
	/// Joins two sampled circular arcs into a closed stroke contour in drawing coordinates.
	/// Curves use the same quarter-pixel sampling tolerance as Pie and Circle; fill stays analytic.
	/// </summary>
	void StrokeArcPair( Vector2 a, float ra, float startA, float sweepA, Vector2 b, float rb, float startB, float sweepB, bool sharedEndpoints = false )
	{
		int countA = CurveSegments( ra, sweepA ) + 1;
		int countB = CurveSegments( rb, sweepB ) + 1;
		var rented = ArrayPool<Vector2>.Shared.Rent( countA + countB );
		try
		{
			var points = rented.AsSpan( 0, countA + countB );
			SampleArc( points[..countA], a, new Vector2( ra ), startA, sweepA );
			SampleArc( points[countA..], b, new Vector2( rb ), startB, sweepB );
			if ( sharedEndpoints ) { points[countA] = points[countA - 1]; points[^1] = points[0]; }
			Path.DrawPolyline( ActiveContext, points, Stroke, closed: true );
		}
		finally { ArrayPool<Vector2>.Shared.Return( rented ); }
	}
}
