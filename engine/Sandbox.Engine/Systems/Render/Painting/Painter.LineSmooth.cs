using System.Buffers;
using System.Runtime.InteropServices;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Draws an open, smooth curve through the supplied points using the current centered Stroke.
	/// Uses centripetal Catmull-Rom interpolation in drawing pixels; the curve can extend beyond the points' bounds.
	/// Consecutive duplicate points are ignored. Two distinct points draw a straight line; fewer draw nothing.
	/// Non-finite points draw nothing. Fill is ignored. Dashes continue along the whole curve.
	/// </summary>
	public void LineSmooth( ReadOnlySpan<Vector2> points )
	{
		if ( !HasStroke( Stroke ) || points.Length < 2 ) return;
		var rented = ArrayPool<Vector2>.Shared.Rent( points.Length );
		try
		{
			var clean = rented.AsSpan( 0, points.Length );
			int count = 0;
			foreach ( var point in points )
			{
				if ( !point.IsFinite ) return;
				if ( count == 0 || point != clean[count - 1] ) clean[count++] = point;
			}
			if ( count < 2 ) return;
			if ( count == 2 )
			{
				Line( clean[..count] );
				return;
			}

			var curve = new List<Vector2> { clean[0] };
			for ( int i = 0; i < count - 1; i++ )
			{
				var from = clean[i];
				var to = clean[i + 1];
				var interval = SmoothInterval( from, to );
				// Extrapolated endpoint neighbors give a tangent along the first/last chord.
				var control1 = i == 0 ? from * (2f / 3f) + to * (1f / 3f)
					: from + SmoothTangent( clean[i - 1], from, to, interval / 3 );
				var control2 = i + 2 == count ? from * (1f / 3f) + to * (2f / 3f)
					: to - SmoothTangent( from, to, clean[i + 2], interval / 3 );
				if ( !control1.IsFinite || !control2.IsFinite ) return;
				FlattenBezier( curve, from, control1, control2, to );
			}
			// One path preserves paint mapping, dash phase and endpoint caps across all segments.
			Line( CollectionsMarshal.AsSpan( curve ) );
		}
		finally
		{
			ArrayPool<Vector2>.Shared.Return( rented );
		}
	}

	static double SmoothInterval( Vector2 from, Vector2 to )
	{
		double x = (double)to.x - from.x, y = (double)to.y - from.y;
		return Math.Sqrt( Math.Sqrt( x * x + y * y ) );
	}

	static Vector2 SmoothTangent( Vector2 before, Vector2 point, Vector2 after, double scale )
	{
		var previous = SmoothInterval( before, point );
		var next = SmoothInterval( point, after );
		var incoming = next * scale / (previous * (previous + next));
		var outgoing = previous * scale / (next * (previous + next));
		return new Vector2(
			(float)(((double)point.x - before.x) * incoming + ((double)after.x - point.x) * outgoing),
			(float)(((double)point.y - before.y) * incoming + ((double)after.y - point.y) * outgoing) );
	}
}
