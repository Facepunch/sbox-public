using Sandbox.Audio;
using System;

namespace AudioTests;

/// <summary>
/// Measures the frequency response of the filters behind LowPassProcessor and HighPassProcessor.
/// The old versions were a comb filter and a plain gain, which a single tone would not catch.
/// </summary>
[TestClass]
public class BiquadTest
{
	const float SampleRate = 44100;

	/// <summary>
	/// Output RMS over input RMS for a sine at this frequency, once the filter has settled.
	/// Fed in mix sized buffers so state has to carry across calls.
	/// </summary>
	static float Gain( Biquad filter, float frequency )
	{
		const int length = 512 * 64;

		var samples = new float[length];
		for ( int i = 0; i < length; i++ )
			samples[i] = MathF.Sin( 2 * MathF.PI * frequency * i / SampleRate );

		float z1 = 0, z2 = 0;
		for ( int i = 0; i < length; i += 512 )
			filter.Process( samples.AsSpan( i, 512 ), ref z1, ref z2 );

		double output = 0;
		for ( int i = length / 2; i < length; i++ )
			output += samples[i] * samples[i];

		return (float)Math.Sqrt( output / (length / 2) / 0.5 );
	}

	[TestMethod]
	public void LowPassResponse()
	{
		var filter = Biquad.LowPass( 1000, 0.707f, SampleRate );

		Assert.AreEqual( 1.0f, Gain( filter, 50 ), 0.01f );
		Assert.AreEqual( 0.707f, Gain( filter, 1000 ), 0.02f );
		Assert.IsTrue( Gain( filter, 4000 ) < 0.07f );
		Assert.IsTrue( Gain( filter, 11025 ) < 0.01f );
		Assert.IsTrue( Gain( filter, 16000 ) < 0.01f );
	}

	[TestMethod]
	public void HighPassResponse()
	{
		var filter = Biquad.HighPass( 1000, 0.707f, SampleRate );

		Assert.IsTrue( Gain( filter, 50 ) < 0.01f );
		Assert.IsTrue( Gain( filter, 250 ) < 0.07f );
		Assert.AreEqual( 0.707f, Gain( filter, 1000 ), 0.02f );
		Assert.AreEqual( 1.0f, Gain( filter, 8000 ), 0.01f );
		Assert.AreEqual( 1.0f, Gain( filter, 16000 ), 0.01f );
	}

	[TestMethod]
	public void ResonanceBoostsCutoff()
	{
		var filter = Biquad.LowPass( 1000, 4, SampleRate );

		Assert.AreEqual( 4.0f, Gain( filter, 1000 ), 0.1f );
	}

	[TestMethod]
	public void SilenceDecaysToZero()
	{
		var filter = Biquad.LowPass( 1000, 0.707f, SampleRate );
		var samples = new float[512];
		samples[0] = 1;

		float z1 = 0, z2 = 0;
		for ( int i = 0; i < 64; i++ )
		{
			filter.Process( samples, ref z1, ref z2 );
			Array.Clear( samples );
		}

		Assert.AreEqual( 0f, z1 );
		Assert.AreEqual( 0f, z2 );
	}
}
