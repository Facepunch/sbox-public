using System.Numerics;
using System.Runtime.Intrinsics;

namespace Nav;

// The eight-wide passes of avoidance and separation, and all of them: delete this file, the lines in
// Avoid and Separate that call them and the two sums of Avoidance and of Separation, and every
// neighbour takes the single path, which is the reference. They are here for the jam, where an agent
// has hundreds of neighbours in range.
public sealed partial class NavCrowd
{
	// The one switch. Off, results differ in the last bits (the sums add up in another order).
	internal bool EightWide = Vector256.IsHardwareAccelerated;

	// Takes the slots of the cells within reach of the bucket eight at a time with the weights of the
	// single path, zero where its conditions fail. The last eight of a run may reach past its end, into
	// the next run or the padding at the end of the frame: those lanes count as out of range. All the
	// runs of one lookup go through here, so that the lane sums stay in registers.
	void AvoidEight( ref Avoidance scan, int bucket, int reach )
	{
		Vector256<float> x = Vector256.Create( scan.X ), y = Vector256.Create( scan.Y ), z = Vector256.Create( scan.Z ), top = Vector256.Create( scan.Top );
		Vector256<float> radius = Vector256.Create( scan.Radius ), look = Vector256.Create( scan.Look ), horizon = Vector256.Create( scan.Horizon );
		Vector256<float> wishX = Vector256.Create( scan.WishX ), wishY = Vector256.Create( scan.WishY ), still = Vector256.Create( scan.StillSquared );
		Vector256<float> perStep = Vector256.Create( scan.PerStep ), spacing = Vector256.Create( scan.Spacing ), tiny = Vector256.Create( scan.Tiny ), least = Vector256.Create( scan.Least );
		Vector256<float> half = Vector256.Create( 0.5f ), one = Vector256.Create( 1f ), two = Vector256.Create( 2f ), four = Vector256.Create( 4f );
		var lanes = Vector256.Create( 0, 1, 2, 3, 4, 5, 6, 7 );
		var self = Vector256.Create( scan.Slot ) - lanes;
		Vector256<float> sumX = Vector256<float>.Zero, sumY = Vector256<float>.Zero;
		for ( int n = 0; Cells( bucket, reach, n, out int first, out int last ); n++ )
		{
			for ( int slot = first; slot < last; slot += 8 )
			{
				var dx = x - Eight( frame.X, slot );
				var dy = y - Eight( frame.Y, slot );
				var squared = dx * dx + dy * dy;
				var sum = radius + Eight( frame.Radius, slot );
				var range = sum * two + look;
				var inRange = Vector256.LessThanOrEqual( squared, range * range ) & Vector256.GreaterThan( top, Eight( frame.Z, slot ) ) & Vector256.GreaterThan( Eight( frame.Top, slot ), z )
					& ~Vector256.Equals( self, Vector256.Create( slot ) ).AsSingle() & Vector256.LessThan( lanes, Vector256.Create( last - slot ) ).AsSingle();
				uint bits = inRange.ExtractMostSignificantBits();
				if ( bits == 0 ) continue;
				scan.Neighbours += BitOperations.PopCount( bits );

				// Neighbours on the very same spot have no direction to push in: the single path decides.
				var distance = Vector256.Sqrt( squared );
				var apart = inRange & Vector256.GreaterThan( distance, tiny );
				for ( uint same = bits & ~apart.ExtractMostSignificantBits(); same != 0; same &= same - 1 ) Avoid( ref scan, slot + BitOperations.TrailingZeroCount( same ) );

				var relativeX = wishX - Eight( frame.VelocityX, slot );
				var relativeY = wishY - Eight( frame.VelocityY, slot );
				var towards = -(relativeX * dx + relativeY * dy);
				var relativeSquared = relativeX * relativeX + relativeY * relativeY;
				var approach = Vector256.GreaterThan( relativeSquared, still ) & (towards / relativeSquared);
				var missX = dx + relativeX * approach;
				var missY = dy + relativeY * approach;
				var closing = towards / distance;
				var gap = distance - sum;
				var overlap = Vector256.LessThan( distance, sum );
				var near = Vector256.LessThan( squared, sum * sum * four );
				var predicted = Vector256.GreaterThan( towards, Vector256<float>.Zero ) & Vector256.LessThan( missX * missX + missY * missY, sum * sum ) & Vector256.LessThan( gap, closing * horizon );

				// Along the direction away from the neighbour: the push out of an overlap, the spacing
				// and the sidestep. Across it, to the passing side: the sidestep.
				var passing = predicted & ((closing - gap / horizon) * half);
				var along = (overlap & (-gap * perStep)) + passing + (near & ((one - gap / Vector256.Max( sum, least )) * spacing));
				var normalX = dx / distance;
				var normalY = dy / distance;
				sumX += apart & (normalX * along - normalY * passing);
				sumY += apart & (normalY * along + normalX * passing);
			}
		}
		scan.SumX = sumX;
		scan.SumY = sumY;
	}

	// The eight-wide pass of separation, as AvoidEight: the slots of the cells next to the bucket's
	// eight at a time, with the weights of the single path; neighbours on the very same spot go through
	// that.
	void SeparateEight( ref Separation scan, int bucket )
	{
		Vector256<float> x = Vector256.Create( scan.X ), y = Vector256.Create( scan.Y ), z = Vector256.Create( scan.Z ), top = Vector256.Create( scan.Top );
		Vector256<float> radius = Vector256.Create( scan.Radius ), tiny = Vector256.Create( scan.Tiny );
		var lanes = Vector256.Create( 0, 1, 2, 3, 4, 5, 6, 7 );
		var self = Vector256.Create( scan.Slot ) - lanes;
		Vector256<float> sumX = Vector256<float>.Zero, sumY = Vector256<float>.Zero;
		for ( int n = 0; Cells( bucket, 1, n, out int first, out int last ); n++ )
		{
			for ( int slot = first; slot < last; slot += 8 )
			{
				var dx = x - Eight( frame.X, slot );
				var dy = y - Eight( frame.Y, slot );
				var squared = dx * dx + dy * dy;
				var sum = radius + Eight( frame.Radius, slot );
				var overlap = Vector256.LessThan( squared, sum * sum ) & Vector256.GreaterThan( top, Eight( frame.Z, slot ) ) & Vector256.GreaterThan( Eight( frame.Top, slot ), z )
					& ~Vector256.Equals( self, Vector256.Create( slot ) ).AsSingle() & Vector256.LessThan( lanes, Vector256.Create( last - slot ) ).AsSingle();
				uint bits = overlap.ExtractMostSignificantBits();
				if ( bits == 0 ) continue;
				var distance = Vector256.Sqrt( squared );
				var apart = overlap & Vector256.GreaterThan( distance, tiny );
				uint spread = apart.ExtractMostSignificantBits();
				for ( uint same = bits & ~spread; same != 0; same &= same - 1 ) Separate( ref scan, slot + BitOperations.TrailingZeroCount( same ) );
				scan.Count += BitOperations.PopCount( spread );
				var depth = (sum - distance) * Eight( frame.Part, slot );
				sumX += apart & (dx / distance * depth);
				sumY += apart & (dy / distance * depth);
			}
		}
		scan.SumX = sumX;
		scan.SumY = sumY;
	}

	static Vector256<float> Eight( float[] values, int at ) => Vector256.Create<float>( values.AsSpan( at, 8 ) );
}
