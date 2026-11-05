using Sandbox.Audio;
using System;

namespace AudioTests;

[TestClass]
public class MasterLimiterTest
{
	const int Delay = MasterLimiter.Lookahead - 1;

	static float[][] Run( Func<int, float> signal, int frames )
	{
		var limiter = new MasterLimiter();
		var output = new[] { new float[frames], new float[frames] };
		for ( int start = 0; start < frames; start += 512 )
		{
			var buffer = new[] { new float[512], new float[512] };
			for ( int i = 0; i < 512; i++ ) buffer[0][i] = buffer[1][i] = signal( start + i );
			limiter.Process( buffer, 512 );
			for ( int i = 0; i < 512; i++ ) { output[0][start + i] = buffer[0][i]; output[1][start + i] = buffer[1][i]; }
		}
		return output;
	}

	static float Sine( int i, float amplitude ) => amplitude * MathF.Sin( i * 2f * MathF.PI * 220f / 44100f );

	[TestMethod]
	public void QuietPassesUntouched()
	{
		var output = Run( i => Sine( i, 0.5f ), 44544 );
		for ( int i = Delay; i < 44544; i++ )
			Assert.AreEqual( Sine( i - Delay, 0.5f ), output[0][i], 1e-5f );
	}

	[TestMethod]
	public void LoudNeverPassesCeilingAndRampsSmoothly()
	{
		// Silence, then a hit twice over full scale, then quiet again.
		static float Signal( int i ) => i < 10000 ? Sine( i, 0.3f ) : i < 20000 ? Sine( i, 2f ) : Sine( i, 0.3f );
		var output = Run( Signal, 44544 );

		float peak = 0f;
		for ( int i = 0; i < 44544; i++ ) peak = MathF.Max( peak, MathF.Abs( output[0][i] ) );
		Assert.IsTrue( peak <= MasterLimiter.Ceiling + 1e-4f, $"peak {peak}" );

		// Gain is output over input, it never jumps by more than one lookahead step.
		float last = 1f;
		int lastIndex = Delay;
		for ( int i = Delay; i < 44544; i++ )
		{
			float input = Signal( i - Delay );
			if ( MathF.Abs( input ) < 0.05f ) continue;
			float gain = output[0][i] / input;
			Assert.IsTrue( MathF.Abs( gain - last ) <= (i - lastIndex) / (float)MasterLimiter.Lookahead + 1e-3f, $"gain jump at {i}: {last} -> {gain}" );
			last = gain;
			lastIndex = i;
		}

		// Back to full level half a second after the hit.
		int after = 20000 + Delay + 22050;
		Assert.AreEqual( Sine( after - Delay, 0.3f ), output[0][after], 0.01f );
	}
}
