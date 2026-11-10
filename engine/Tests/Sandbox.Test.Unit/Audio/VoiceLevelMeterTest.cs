using Sandbox;
using System;
using LevelMeter = Sandbox.VoiceManager.LevelMeter;

namespace AudioTests;

/// <summary>
/// How voice chat measures each frame, for the gate and for automatic gain control
/// </summary>
[TestClass]
public class VoiceLevelMeterTest
{
	const int Rate = 48000;
	const int FrameSamples = Rate / 50;
	const int FramesPerSecond = Rate / FrameSamples;

	static float Db( float gain ) => 20 * MathF.Log10( gain );

	/// <summary>
	/// The level a steady tone measures once the filter has settled
	/// </summary>
	static float MeasureTone( LevelMeter meter, float frequency, float peak )
	{
		var frame = new float[FrameSamples];
		float level = 0;

		for ( int f = 0; f < FramesPerSecond; f++ )
		{
			for ( int i = 0; i < frame.Length; i++ )
				frame[i] = peak * MathF.Sin( 2 * MathF.PI * frequency * (f * FrameSamples + i) / Rate );

			level = meter.Measure( frame );
		}

		return level;
	}

	static float MeasureTone( float frequency, float peak ) => MeasureTone( new LevelMeter( Rate, FrameSamples ), frequency, peak );

	[TestMethod]
	public void HearsVoice()
	{
		// A sine's RMS is 3 dB under its peak
		Assert.AreEqual( Db( 0.1f ) - 3.01f, MeasureTone( 1000, 0.1f ), 0.1f );
	}

	[TestMethod]
	public void IgnoresRumble()
	{
		var rumble = MeasureTone( 30, 0.1f );
		var voice = MeasureTone( 1000, 0.1f );

		Assert.IsTrue( voice - rumble > 18, $"30 Hz measured {rumble:0.0} dB, only {voice - rumble:0.0} dB under 1 kHz" );
	}

	[TestMethod]
	public void SilenceIsMinLevel()
	{
		var meter = new LevelMeter( Rate, FrameSamples );
		Assert.AreEqual( VoiceManager.MinLevel, meter.Measure( new float[FrameSamples] ) );
	}

	[TestMethod]
	public void DoesNotChangeTheFrame()
	{
		var frame = new float[FrameSamples];
		for ( int i = 0; i < frame.Length; i++ )
			frame[i] = 0.5f * MathF.Sin( 2 * MathF.PI * 440 * i / Rate );

		var copy = (float[])frame.Clone();
		new LevelMeter( Rate, FrameSamples ).Measure( frame );

		CollectionAssert.AreEqual( copy, frame );
	}

	[TestMethod]
	public void ResetForgetsTheFilter()
	{
		var meter = new LevelMeter( Rate, FrameSamples );
		MeasureTone( meter, 30, 0.9f );

		// Straight after loud rumble, the filter is still ringing
		var ringing = meter.Measure( new float[FrameSamples] );
		Assert.IsTrue( ringing > VoiceManager.MinLevel, "Expected the filter to still be ringing" );

		meter.Reset();
		Assert.AreEqual( VoiceManager.MinLevel, meter.Measure( new float[FrameSamples] ) );
	}

	[TestMethod]
	public void FrameTooLongThrows()
	{
		var meter = new LevelMeter( Rate, FrameSamples );
		Assert.ThrowsException<ArgumentException>( () => meter.Measure( new float[FrameSamples + 1] ) );
	}
}
