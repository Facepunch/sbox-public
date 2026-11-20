namespace Sandbox;

public sealed class BuilderPipe
{
	/// <summary>Where the pipe's values sit in the Bend shape's parameters.</summary>
	public const int ThicknessSlot = 0, RadiusSlot = 1, EndSlot = 2, NormalSlot = 5;

	/// <summary>End normal components are stored scaled up by this, so parameter rounding keeps them precise.</summary>
	public const float NormalScale = 1024f;

	public const int Columns = 16;

	const int Samples = 64;
	const int MinRows = 4;
	const int MaxRows = 32;
	const float DegreesPerRow = 10f;
	const float MaxDegreesPerRow = 20f;

	public Vector3 End { get; }
	public Vector3 EndNormal { get; }
	public float Radius { get; }
	public float Thickness { get; }
	public int Rows { get; }

	readonly Vector3[] _points;
	readonly Vector3[] _normals;
	readonly Vector3[] _binormals;

	BuilderPipe( Vector3 end, Vector3 endNormal, float radius, float thickness )
	{
		End = end;
		EndNormal = endNormal;
		Radius = radius;
		Thickness = thickness;
		Rows = RowCount();
		if ( Rows == 0 ) return;

		_points = new Vector3[Rows + 1];
		_normals = new Vector3[Rows + 1];
		_binormals = new Vector3[Rows + 1];

		var normal = Vector3.Up;
		var tangent = Vector3.Forward;
		for ( int i = 0; i <= Rows; i++ )
		{
			var t = (float)i / Rows;
			var point = Bezier( t );
			var nextTangent = Derivative( t ).Normal;

			if ( i > 0 )
			{
				var step = point - _points[i - 1];
				var stepLength = step.LengthSquared;
				if ( stepLength > 0.000001f )
				{
					var reflectedNormal = normal - step * (2 / stepLength * Vector3.Dot( step, normal ));
					var reflectedTangent = tangent - step * (2 / stepLength * Vector3.Dot( step, tangent ));
					var correction = nextTangent - reflectedTangent;
					var correctionLength = correction.LengthSquared;
					normal = correctionLength > 0.000001f
						? reflectedNormal - correction * (2 / correctionLength * Vector3.Dot( correction, reflectedNormal ))
						: reflectedNormal;
				}
			}

			tangent = nextTangent;
			normal = (normal - tangent * Vector3.Dot( normal, tangent )).Normal;
			_points[i] = point;
			_normals[i] = normal;
			_binormals[i] = Vector3.Cross( tangent, normal );
		}
	}

	public static bool TryCreate( Vector3 end, Vector3 endNormal, float radius, float thickness, out BuilderPipe pipe )
	{
		pipe = null;

		if ( end.IsNaN || end.IsInfinity || endNormal.IsNaN || endNormal.IsInfinity ) return false;
		if ( !float.IsFinite( radius ) || !float.IsFinite( thickness ) || radius < 0.5f || thickness <= 0 ) return false;
		if ( endNormal.IsNearZeroLength || end.Length < radius * 2 ) return false;

		var candidate = new BuilderPipe( end, endNormal.Normal, radius, MathF.Min( thickness, radius * 0.95f ) );
		if ( candidate.Rows == 0 || !candidate.IsClean() || candidate.Touches() ) return false;

		pipe = candidate;
		return true;
	}

	public static bool TryFromKey( BuilderShapeKey key, out BuilderPipe pipe )
	{
		var end = new Vector3( key.Parameter( EndSlot ), key.Parameter( EndSlot + 1 ), key.Parameter( EndSlot + 2 ) );
		var normal = new Vector3( key.Parameter( NormalSlot ), key.Parameter( NormalSlot + 1 ), key.Parameter( NormalSlot + 2 ) ) / NormalScale;
		return TryCreate( end, normal, key.Parameter( RadiusSlot ), key.Parameter( ThicknessSlot ), out pipe );
	}

	public static BuilderShapeKey? Validate( BuilderShapeKey key )
	{
		if ( !TryFromKey( key, out var pipe ) ) return null;

		var size = pipe.Bounds.Size;
		if ( size.x > BuilderShapeKey.MaxSize || size.y > BuilderShapeKey.MaxSize || size.z > BuilderShapeKey.MaxSize ) return null;

		return key
			.WithParameter( ThicknessSlot, pipe.Thickness )
			.WithSize( size.ComponentMax( new Vector3( BuilderShapeKey.MinSize ) ) );
	}

	public Vector3 Point( int row, float around, float radius )
	{
		row = Math.Clamp( row, 0, Rows );
		return _points[row] + (_normals[row] * MathF.Cos( around ) + _binormals[row] * MathF.Sin( around )) * radius;
	}

	public BuilderFace StartCap => Cap( 0, false );
	public BuilderFace EndCap => Cap( Rows, true );

	BuilderFace Cap( int row, bool forward )
	{
		var a = forward ? _normals[row] : _binormals[row];
		var b = forward ? _binormals[row] : _normals[row];
		var corner = _points[row] - (a + b) * Radius;
		return new BuilderFace( corner, corner + a * (Radius * 2), corner + (a + b) * (Radius * 2), corner + b * (Radius * 2) );
	}

	public BBox Bounds
	{
		get
		{
			var min = new Vector3( float.MaxValue );
			var max = new Vector3( float.MinValue );
			for ( int row = 0; row <= Rows; row++ )
			for ( int u = 0; u < Columns; u++ )
			{
				var p = Point( row, MathF.Tau * u / Columns, Radius );
				min = min.ComponentMin( p );
				max = max.ComponentMax( p );
			}

			return new BBox( min, max );
		}
	}

	float Reach => End.Length * 0.5f;

	Vector3 Bezier( float t )
	{
		var a = Vector3.Forward * Reach;
		var b = End + EndNormal * Reach;
		var s = 1 - t;
		return a * (3 * s * s * t) + b * (3 * s * t * t) + End * (t * t * t);
	}

	Vector3 Derivative( float t )
	{
		var a = Vector3.Forward * Reach;
		var b = End + EndNormal * Reach;
		var s = 1 - t;
		return a * (3 * s * s) + (b - a) * (6 * s * t) + (End - b) * (3 * t * t);
	}

	int RowCount()
	{
		var peak = 0f;
		var previous = Derivative( 0 ).Normal;
		for ( int i = 1; i <= Samples; i++ )
		{
			var tangent = Derivative( (float)i / Samples ).Normal;
			peak = MathF.Max( peak, MathF.Acos( Math.Clamp( Vector3.Dot( previous, tangent ), -1f, 1f ) ) );
			previous = tangent;
		}

		var degreesPerT = peak * Samples * 180f / MathF.PI;
		if ( degreesPerT / MaxRows > MaxDegreesPerRow ) return 0;

		return Math.Clamp( (int)MathF.Ceiling( degreesPerT / DegreesPerRow ), MinRows, MaxRows );
	}

	/// <summary>
	/// Whether two parts of the tube that are further apart along the path than half a turn of its own size come
	/// closer than its diameter.
	/// </summary>
	bool Touches()
	{
		Span<Vector3> points = stackalloc Vector3[Samples + 1];
		Span<float> distances = stackalloc float[Samples + 1];
		for ( int i = 0; i <= Samples; i++ )
		{
			points[i] = Bezier( (float)i / Samples );
			distances[i] = i == 0 ? 0 : distances[i - 1] + points[i].Distance( points[i - 1] );
		}

		var apart = MathF.PI * Radius;
		var clearance = Radius * 2 * 0.99f;
		for ( int i = 0; i < Samples; i++ )
		for ( int j = i + 2; j < Samples; j++ )
		{
			if ( distances[j] - distances[i + 1] <= apart ) continue;
			if ( SegmentDistance( points[i], points[i + 1], points[j], points[j + 1] ) < clearance ) return true;
		}

		return false;
	}

	static float SegmentDistance( Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2 )
	{
		var d1 = q1 - p1;
		var d2 = q2 - p2;
		var r = p1 - p2;
		var a = d1.LengthSquared;
		var e = d2.LengthSquared;
		var f = Vector3.Dot( d2, r );

		float s, t;
		if ( a <= 0.000001f && e <= 0.000001f ) return r.Length;

		if ( a <= 0.000001f )
		{
			s = 0;
			t = Math.Clamp( f / e, 0, 1 );
		}
		else
		{
			var c = Vector3.Dot( d1, r );
			if ( e <= 0.000001f )
			{
				t = 0;
				s = Math.Clamp( -c / a, 0, 1 );
			}
			else
			{
				var b = Vector3.Dot( d1, d2 );
				var denominator = a * e - b * b;
				s = denominator > 0.000001f ? Math.Clamp( (b * f - c * e) / denominator, 0, 1 ) : 0;
				t = (b * s + f) / e;

				if ( t < 0 )
				{
					t = 0;
					s = Math.Clamp( -c / a, 0, 1 );
				}
				else if ( t > 1 )
				{
					t = 1;
					s = Math.Clamp( (b - c) / a, 0, 1 );
				}
			}
		}

		return (p1 + d1 * s).Distance( p2 + d2 * t );
	}

	bool IsClean()
	{
		var previousPoint = Bezier( 0 );
		var previousTangent = Derivative( 0 );
		if ( previousTangent.IsNearZeroLength ) return false;

		for ( int i = 1; i <= Samples; i++ )
		{
			var t = (float)i / Samples;
			var point = Bezier( t );
			var tangent = Derivative( t );
			if ( tangent.IsNearZeroLength || Vector3.Dot( tangent, previousTangent ) <= 0 ) return false;

			var turn = MathF.Acos( Math.Clamp( Vector3.Dot( tangent.Normal, previousTangent.Normal ), -1f, 1f ) );
			if ( turn > 0.0001f && point.Distance( previousPoint ) / turn <= Radius * 1.05f ) return false;

			previousPoint = point;
			previousTangent = tangent;
		}

		return true;
	}
}
