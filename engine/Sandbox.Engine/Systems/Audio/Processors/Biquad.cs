namespace Sandbox.Audio;

/// <summary>
/// Second order IIR filter, coefficients from the RBJ audio EQ cookbook.
/// </summary>
internal readonly struct Biquad
{
	readonly float b0, b1, b2, a1, a2;

	Biquad( double b0, double b1, double b2, double a0, double a1, double a2 )
	{
		this.b0 = (float)(b0 / a0);
		this.b1 = (float)(b1 / a0);
		this.b2 = (float)(b2 / a0);
		this.a1 = (float)(a1 / a0);
		this.a2 = (float)(a2 / a0);
	}

	public static Biquad LowPass( float frequency, float q, float sampleRate )
	{
		Angle( frequency, q, sampleRate, out var cos, out var alpha );
		return new Biquad( (1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha );
	}

	public static Biquad HighPass( float frequency, float q, float sampleRate )
	{
		Angle( frequency, q, sampleRate, out var cos, out var alpha );
		return new Biquad( (1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha );
	}

	static void Angle( float frequency, float q, float sampleRate, out double cos, out double alpha )
	{
		var f = Math.Clamp( frequency, 10.0f, sampleRate * 0.49f );
		var w0 = 2.0 * Math.PI * f / sampleRate;

		cos = Math.Cos( w0 );
		alpha = Math.Sin( w0 ) / (2.0 * Math.Max( q, 0.1f ));
	}

	/// <summary>
	/// Filter in place. z1 and z2 carry the transposed direct form II state between buffers.
	/// </summary>
	public void Process( Span<float> samples, ref float z1Ref, ref float z2Ref )
	{
		float z1 = z1Ref, z2 = z2Ref;

		for ( int i = 0; i < samples.Length; i++ )
		{
			var x = samples[i];
			var y = b0 * x + z1;
			z1 = b1 * x - a1 * y + z2;
			z2 = b2 * x - a2 * y;
			samples[i] = y;
		}

		// Decaying state on silence goes denormal, which is very slow
		if ( MathF.Abs( z1 ) < 1e-20f ) z1 = 0;
		if ( MathF.Abs( z2 ) < 1e-20f ) z2 = 0;

		z1Ref = z1;
		z2Ref = z2;
	}
}
