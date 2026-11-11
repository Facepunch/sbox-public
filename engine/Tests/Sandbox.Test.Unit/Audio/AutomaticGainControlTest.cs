using System;
using AutomaticGainControl = Sandbox.VoiceManager.AutomaticGainControl;

namespace AudioTests;

/// <summary>
/// Voice chat's automatic gain control: what it learns from frame levels, and what it does to samples.
/// </summary>
[TestClass]
public class AutomaticGainControlTest
{
	const int Rate = 48000;
	const int FrameSamples = Rate / 50;
	const int FramesPerSecond = Rate / FrameSamples;

	static AutomaticGainControl Create()
	{
		var agc = new AutomaticGainControl( Rate, FrameSamples );
		agc.SetDevice( "test" );
		return agc;
	}

	/// <summary>
	/// Talk for 400ms, pause for 200ms, over and over - the pauses are where it hears the room.
	/// Ends on a pause, so the noise estimate has come back down to the room.
	/// </summary>
	static void Talk( AutomaticGainControl agc, float speech, float seconds, float room = -80 )
	{
		var frames = (int)(seconds * FramesPerSecond);
		for ( int i = 0; i < frames; i++ )
			agc.Learn( i % 30 < 20 ? speech : room );

		for ( int i = 0; i < 10; i++ )
			agc.Learn( room );
	}

	/// <summary>
	/// A frame of sine with this peak
	/// </summary>
	static float[] Sine( float peak )
	{
		var frame = new float[FrameSamples];
		for ( int i = 0; i < frame.Length; i++ )
			frame[i] = peak * MathF.Sin( 2 * MathF.PI * 440 * i / Rate );

		return frame;
	}

	static float Peak( ReadOnlySpan<float> samples )
	{
		float peak = 0;
		foreach ( var s in samples )
			peak = MathF.Max( peak, MathF.Abs( s ) );

		return peak;
	}

	static float Db( float gain ) => 20 * MathF.Log10( gain );

	[TestMethod]
	public void StartsAtUnity()
	{
		Assert.AreEqual( 0, Create().Gain, 0.001f );
	}

	[TestMethod]
	public void QuietSpeechIsBoostedToTarget()
	{
		var agc = Create();
		Talk( agc, -40, 20 );

		Assert.AreEqual( -40, agc.SpeechLevel, 1 );
		Assert.AreEqual( AutomaticGainControl.TargetLevel + 40, agc.Gain, 1 );
	}

	[TestMethod]
	public void LoudSpeechIsTurnedDown()
	{
		var agc = Create();
		Talk( agc, -3, 5 );

		Assert.AreEqual( AutomaticGainControl.MinGain, agc.Gain, 0.001f );
	}

	[TestMethod]
	public void BoostIsCappedByMaxGain()
	{
		var agc = Create();
		Talk( agc, -60, 30 );

		Assert.AreEqual( AutomaticGainControl.MaxGain, agc.Gain, 0.001f );
	}

	[TestMethod]
	public void HearsTheRoomBetweenWords()
	{
		var agc = Create();
		Talk( agc, -40, 20, room: -70 );

		Assert.AreEqual( -70, agc.NoiseLevel, 0.01f );
		Assert.AreEqual( -40, agc.SpeechLevel, 1 );
	}

	[TestMethod]
	public void SteadyNoiseIsNotBoosted()
	{
		var agc = Create();

		// A fan, and nobody talking
		for ( int i = 0; i < 30 * FramesPerSecond; i++ )
			agc.Learn( -45 );

		Assert.AreEqual( -45, agc.NoiseLevel, 0.01f );
		Assert.IsTrue( agc.Gain <= 0, $"Gain {agc.Gain} boosts the noise" );
	}

	[TestMethod]
	public void BoostDoesNotLiftNoiseAboveMaxNoiseLevel()
	{
		var agc = Create();
		Talk( agc, -40, 20, room: -60 );

		// Speech wants +22, but that would lift the room to -38
		Assert.AreEqual( -60, agc.NoiseLevel, 0.01f );
		Assert.AreEqual( AutomaticGainControl.MaxNoiseLevel - agc.NoiseLevel, agc.Gain, 0.01f );
	}

	[TestMethod]
	public void NoiseIsNotLearnedAsSpeech()
	{
		var agc = Create();
		Talk( agc, -30, 10, room: -70 );

		var speech = agc.SpeechLevel;

		// Quiet, but louder than the room - still within the margin, so it's not speech
		for ( int i = 0; i < 5 * FramesPerSecond; i++ )
			agc.Learn( -70 + AutomaticGainControl.NoiseMargin - 1 );

		Assert.AreEqual( speech, agc.SpeechLevel );
	}

	[TestMethod]
	public void DigitalSilenceIsIgnored()
	{
		var agc = Create();
		Talk( agc, -40, 10, room: -70 );

		var noise = agc.NoiseLevel;
		var speech = agc.SpeechLevel;

		for ( int i = 0; i < FramesPerSecond; i++ )
			agc.Learn( VoiceManager.MinLevel );

		Assert.AreEqual( noise, agc.NoiseLevel );
		Assert.AreEqual( speech, agc.SpeechLevel );
	}

	[TestMethod]
	public void SpeechLevelFollowsAtItsRates()
	{
		var agc = Create();

		// Hear a quiet room first, so everything after counts as speech
		for ( int i = 0; i < FramesPerSecond; i++ )
			agc.Learn( -80 );

		for ( int i = 0; i < FramesPerSecond; i++ )
			agc.Learn( -10 );

		Assert.AreEqual( -10, agc.SpeechLevel, 0.001f );

		// A second of quieter speech brings it down by the fall rate, and no further
		for ( int i = 0; i < FramesPerSecond; i++ )
			agc.Learn( -40 );

		Assert.AreEqual( -10 - AutomaticGainControl.SpeechFallRate, agc.SpeechLevel, 0.01f );

		// A tenth of a second of louder speech brings it up by a tenth of the rise rate
		for ( int i = 0; i < FramesPerSecond / 10; i++ )
			agc.Learn( 0 );

		Assert.AreEqual( -10 - AutomaticGainControl.SpeechFallRate + AutomaticGainControl.SpeechRiseRate / 10, agc.SpeechLevel, 0.01f );
	}

	[TestMethod]
	public void SameDeviceKeepsWhatItLearned()
	{
		var agc = Create();
		Talk( agc, -40, 20 );

		var gain = agc.Gain;
		agc.SetDevice( "test" );

		Assert.AreEqual( gain, agc.Gain );
	}

	[TestMethod]
	public void NewDeviceStartsOver()
	{
		var agc = Create();
		Talk( agc, -40, 20 );

		agc.SetDevice( "other" );

		Assert.AreEqual( 0, agc.Gain, 0.001f );
		Assert.AreEqual( VoiceManager.MinLevel, agc.NoiseLevel );
	}

	/// <summary>
	/// A copy of the frame with gain applied, leaving the frame itself alone
	/// </summary>
	static float[] Apply( AutomaticGainControl agc, float[] frame, bool enabled = true )
	{
		var output = (float[])frame.Clone();
		agc.Apply( output, enabled );
		return output;
	}

	[TestMethod]
	public void ApplyStaysUnderCeiling()
	{
		var agc = Create();
		Talk( agc, -40, 20 );

		var frame = Sine( 0.99f );
		for ( int i = 0; i < 100; i++ )
		{
			var peak = Peak( Apply( agc, frame ) );
			Assert.IsTrue( peak <= AutomaticGainControl.Ceiling + 1e-5f, $"Frame {i} peaked at {peak}" );
		}
	}

	[TestMethod]
	public void GainRisesGradually()
	{
		var agc = Create();
		Talk( agc, -40, 20 );

		var frame = Sine( 0.01f );
		var output = Apply( agc, frame );

		// The last sample of the ramp is where it got to
		var reached = Db( output[^1] / frame[^1] );
		var allowed = AutomaticGainControl.GainRiseRate / FramesPerSecond;

		Assert.AreEqual( allowed, reached, 0.01f );

		// And it gets all the way there in the end
		for ( int i = 0; i < 2 * FramesPerSecond; i++ )
			output = Apply( agc, frame );

		Assert.AreEqual( agc.Gain, Db( output[^1] / frame[^1] ), 0.01f );
	}

	[TestMethod]
	public void LoudFramesAreLimitedToTheCeiling()
	{
		var agc = Create();
		Talk( agc, -40, 20 );

		// +22 dB would take this well over, so it's turned down to just reach the ceiling
		var frame = Sine( 0.5f );
		float[] output = null;
		for ( int i = 0; i < 2 * FramesPerSecond; i++ )
			output = Apply( agc, frame );

		Assert.AreEqual( AutomaticGainControl.Ceiling, Peak( output ), 0.001f );
	}

	[TestMethod]
	public void DisablingEasesBackToUntouched()
	{
		var agc = Create();
		Talk( agc, -40, 20 );

		var frame = Sine( 0.01f );
		for ( int i = 0; i < 2 * FramesPerSecond; i++ )
			Apply( agc, frame );

		// One frame to ramp down, ending at unity
		var output = Apply( agc, frame, false );
		Assert.AreEqual( frame[^1], output[^1], 1e-6f );

		var start = FrameSamples / 8;
		Assert.IsTrue( Peak( output.AsSpan( 0, start ) ) > Peak( frame.AsSpan( 0, start ) ) * 1.5f, "The ramp down should start from the boosted gain" );

		// Then it's left alone
		output = Apply( agc, frame, false );
		CollectionAssert.AreEqual( frame, output );
	}
}
