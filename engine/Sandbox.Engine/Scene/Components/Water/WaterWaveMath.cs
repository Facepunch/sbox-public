namespace Sandbox;

/// <summary>
/// Gerstner surface evaluation shared by gameplay queries and the wave regression tests.
/// Keep the band weights, phase offsets and compression limit in water/waves.hlsl in sync.
/// </summary>
internal static class WaterWaveMath
{
	static readonly float[] Weights = [0.5f, 0.27f, 0.15f, 0.08f];

	/// <summary>
	/// Build a normalized band with independent along-heading and across-heading scales.
	/// Both the shader and gameplay consume this direction, wavenumber and angular frequency.
	/// </summary>
	internal static Vector4 BuildBand( float length, float width, float heading, float ratio, float angle )
	{
		float spread = angle * MathF.PI / 180;
		float along = MathF.Cos( spread ) * MathF.Tau / MathF.Max( 4, length * ratio );
		float across = MathF.Sin( spread ) * MathF.Tau / MathF.Max( 4, width * ratio );
		float direction = heading * MathF.PI / 180;
		var (headingSin, headingCos) = MathF.SinCos( direction );
		float x = along * headingCos - across * headingSin;
		float y = along * headingSin + across * headingCos;
		float k = MathF.Sqrt( x * x + y * y );

		// Keep unit directions so the shared compression budget still prevents folding.
		return new Vector4( x / k, y / k, k, MathF.Sqrt( 386.09f * k ) );
	}

	/// <summary>
	/// Evaluate the unfiltered rendered surface and its horizontal Jacobian at a rest point.
	/// </summary>
	internal static Vector3 Evaluate( Vector2 rest, Vector4[] waves, float height, float steepness,
		float time, out Vector2 derivative, out Vector3 jacobian )
	{
		var offset = Vector3.Zero;
		derivative = Vector2.Zero;
		jacobian = new Vector3( 1, 0, 1 );
		if ( height <= 0 )
			return offset;

		float compressionBudget = Math.Clamp( steepness, 0, 1 ) * 0.99f;

		for ( int i = 0; i < waves.Length; i++ )
		{
			var w = waves[i];
			float amplitude = height * Weights[i];
			// Allocate compression by band weight so chop remains visible on low, long waves.
			float horizontal = compressionBudget * Weights[i] / MathF.Max( w.z, 0.0001f );
			float phase = w.z * (w.x * rest.x + w.y * rest.y) - w.w * time + i * 1.7f;
			var (s, c) = MathF.SinCos( phase );
			offset += new Vector3( w.x * horizontal * c, w.y * horizontal * c, amplitude * s );
			derivative += new Vector2( w.x, w.y ) * (w.z * amplitude * c);
			float compression = w.z * horizontal * s;
			jacobian -= new Vector3( w.x * w.x, w.x * w.y, w.y * w.y ) * compression;
		}

		return offset;
	}

	/// <summary>
	/// Reject rays near the wave envelope when necessary crest or trough phase windows cannot overlap.
	/// Returning true only indicates a possible crossing; the normal trace still locates the first actual hit.
	/// Position error widens the intervals for rounding when interpolating and transforming world coordinates.
	/// </summary>
	internal static bool MayIntersectEnvelope( Vector3 start, Vector3 end, float first, float last,
		Vector4[] waves, float height, float steepness, float time, Vector3 positionError = default )
	{
		float z0 = start.z + (end.z - start.z) * first;
		float z1 = start.z + (end.z - start.z) * last;
		float sign, deficit;
		if ( MathF.Min( z0, z1 ) > 0 )
		{
			sign = 1;
			deficit = height - MathF.Min( z0, z1 );
		}
		else if ( MathF.Max( z0, z1 ) < 0 )
		{
			sign = -1;
			deficit = height + MathF.Max( z0, z1 );
		}
		else
			return true;

		// Widen the height envelope for the float amplitudes and sampled positions.
		deficit += MathF.Max( 1, height ) * 0.000001f + positionError.z;

		// Away from the outer envelope the necessary phase windows are too broad to be useful.
		// The existing trace handles those rays directly, with identical accuracy.
		if ( deficit >= height * Weights[3] )
			return true;
		if ( deficit < 0 )
			return false;

		Span<double> phase = stackalloc double[4];
		Span<double> frequency = stackalloc double[4];
		Span<double> radius = stackalloc double[4];
		Span<double> cosineBound = stackalloc double[4];
		for ( int i = 0; i < 4; i++ )
		{
			double threshold = Math.Clamp( 1 - deficit / (height * Weights[i]), 0, 1 );
			radius[i] = Math.Acos( threshold );
			cosineBound[i] = Math.Sqrt( Math.Max( 0, 1 - threshold * threshold ) );
		}

		double compression = Math.Clamp( steepness, 0, 1 ) * 0.99;
		for ( int i = 0; i < 4; i++ )
		{
			var w = waves[i];
			for ( int j = 0; j < 4; j++ )
			{
				var v = waves[j];
				// Near an envelope hit every cosine is small, which bounds horizontal Gerstner displacement.
				radius[i] += w.z * compression * Weights[j] / Math.Max( v.z, 0.0001 )
					* Math.Abs( w.x * v.x + w.y * v.y ) * cosineBound[j];
			}
			phase[i] = w.z * ((double)w.x * start.x + (double)w.y * start.y)
				- w.w * (double)time + i * 1.7 - sign * Math.PI / 2;
			frequency[i] = w.z * ((double)w.x * (end.x - start.x) + (double)w.y * (end.y - start.y));
			// Bound float rounding before cancellation between spatial and temporal phase terms.
			double magnitude = w.z * (Math.Abs( w.x ) * Math.Max( Math.Abs( start.x ), Math.Abs( end.x ) )
				+ Math.Abs( w.y ) * Math.Max( Math.Abs( start.y ), Math.Abs( end.y ) )) + Math.Abs( w.w * (double)time );
			radius[i] += (magnitude + 1) * 0.000001
				+ w.z * (Math.Abs( w.x ) * positionError.x + Math.Abs( w.y ) * positionError.y);
			if ( !double.IsFinite( radius[i] ) )
				return true;
			if ( frequency[i] < 0 )
			{
				frequency[i] = -frequency[i];
				phase[i] = -phase[i];
			}
		}

		double cursor = first;
		while ( cursor <= last )
		{
			bool advanced = false;
			for ( int i = 0; i < 4; i++ )
			{
				if ( radius[i] >= Math.PI )
					continue;
				if ( frequency[i] == 0 )
				{
					double wrapped = Math.IEEERemainder( phase[i], Math.Tau );
					if ( Math.Abs( wrapped ) > radius[i] )
						return false;
					continue;
				}
				double cycle = Math.Ceiling( (phase[i] + frequency[i] * cursor - radius[i]) / Math.Tau );
				double entry = (cycle * Math.Tau - radius[i] - phase[i]) / frequency[i];
				if ( entry <= cursor )
					continue;
				cursor = entry;
				advanced = true;
				break;
			}
			if ( !advanced )
				return true;
		}
		return false;
	}

	/// <summary>
	/// Invert the symmetric horizontal Jacobian for both Newton steps and surface gradients.
	/// </summary>
	internal static Vector2 Inverse( Vector3 jacobian, Vector2 value )
	{
		float determinant = MathF.Max( jacobian.x * jacobian.z - jacobian.y * jacobian.y, 0.000001f );
		return new Vector2( jacobian.z * value.x - jacobian.y * value.y,
			jacobian.x * value.y - jacobian.y * value.x ) / determinant;
	}

	/// <summary>
	/// Find the rest point beneath a displaced XY query, then return its height and slope.
	/// Backtracking keeps Newton's method convergent around strongly compressed crests.
	/// Camera-dependent mesh filtering is deliberately excluded.
	/// </summary>
	internal static float Sample( Vector2 point, Vector4[] waves, float height, float steepness,
		float time, out Vector2 slope )
	{
		var rest = point;
		var surface = Evaluate( rest, waves, height, steepness, time, out var derivative, out var jacobian );

		for ( int iteration = 0; iteration < 16; iteration++ )
		{
			var error = rest + new Vector2( surface.x, surface.y ) - point;
			float errorSquared = error.LengthSquared;
			if ( errorSquared < 0.0000000001f )
				break;

			var step = Inverse( jacobian, error );
			bool advanced = false;
			for ( int attempt = 0; attempt < 10; attempt++ )
			{
				var candidate = rest - step;
				var candidateOffset = Evaluate( candidate, waves, height, steepness, time,
					out var candidateDerivative, out var candidateJacobian );
				var candidateError = candidate + new Vector2( candidateOffset.x, candidateOffset.y ) - point;
				if ( candidateError.LengthSquared < errorSquared )
				{
					rest = candidate;
					surface = candidateOffset;
					derivative = candidateDerivative;
					jacobian = candidateJacobian;
					advanced = true;
					break;
				}

				step *= 0.5f;
			}

			// At float precision, retrying the same rejected step cannot improve the answer.
			if ( !advanced )
				break;
		}

		slope = Inverse( jacobian, derivative );
		return surface.z;
	}
}
