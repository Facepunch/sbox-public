using System;
using System.Collections.Generic;
using System.IO;
using AutomaticGainControl = Sandbox.VoiceManager.AutomaticGainControl;

namespace AudioTests;

/// <summary>
/// Voice chat's automatic gain control, fed real microphone recordings. Each one is a list of 20ms frame
/// levels in Data/Voice, measured the way AutomaticGainControl.Measure does, so the recordings themselves
/// don't need to be checked in. The background ones are just the room (quiet, typing, and with a headset
/// on), the rest are talking at different volumes in that room.
/// </summary>
[TestClass]
public class AutomaticGainControlRecordingTest
{
	const int Rate = 48000;
	const int FrameSamples = Rate / 50;
	const int FramesPerSecond = Rate / FrameSamples;

	/// <summary>
	/// How far from <see cref="AutomaticGainControl.TargetLevel"/> speech is allowed to end up, in decibels
	/// </summary>
	const float SpeechTolerance = 6.0f;

	static float[] Load( string name )
	{
		var path = Path.Combine( AppContext.BaseDirectory, "Data", "Voice", $"{name}.txt" );

		return File.ReadLines( path )
			.Where( x => !string.IsNullOrWhiteSpace( x ) && !x.StartsWith( '#' ) )
			.Select( x => float.Parse( x, System.Globalization.CultureInfo.InvariantCulture ) )
			.ToArray();
	}

	static AutomaticGainControl Create()
	{
		var agc = new AutomaticGainControl( Rate, FrameSamples );
		agc.SetDevice( "test" );
		return agc;
	}

	/// <summary>
	/// Learn from every frame, as VoiceManager does, and return the level each
	/// one would be sent at
	/// </summary>
	static float[] Play( AutomaticGainControl agc, float[] levels )
	{
		var sent = new float[levels.Length];
		for ( int i = 0; i < levels.Length; i++ )
		{
			agc.Learn( levels[i] );
			sent[i] = levels[i] + agc.Gain;
		}

		return sent;
	}

	static float Percentile( IEnumerable<float> values, float fraction )
	{
		var sorted = values.OrderBy( x => x ).ToArray();
		return sorted[(int)(fraction * (sorted.Length - 1))];
	}

	/// <summary>
	/// The room for a few seconds, then the recording a few times over with a second's pause between, as
	/// if someone opened their mic and started talking. Returns how loud the louder half of the last pass
	/// was sent - the voiced part, leaving out the pauses between words.
	/// </summary>
	static float SentSpeechLevel( string recording, out AutomaticGainControl agc )
	{
		agc = Create();

		var room = Load( "background" );
		Play( agc, room );

		var speech = Load( recording );
		var pause = room[..FramesPerSecond];
		float[] sent = null;
		for ( int pass = 0; pass < 3; pass++ )
		{
			if ( pass > 0 ) Play( agc, pause );
			sent = Play( agc, speech );
		}

		var voiced = Percentile( speech, 0.5f );
		return Percentile( sent.Where( ( _, i ) => speech[i] >= voiced ), 0.5f );
	}

	[TestMethod]
	[DataRow( "speech-normal" )]
	[DataRow( "speech-loud" )]
	public void SpeechEndsUpNearTarget( string recording )
	{
		var level = SentSpeechLevel( recording, out var agc );

		Assert.AreEqual( AutomaticGainControl.TargetLevel, level, SpeechTolerance,
			$"{recording} sent at {level:0.0} dB with {agc.Gain:0.0} dB of gain (speech {agc.SpeechLevel:0.0}, noise {agc.NoiseLevel:0.0})" );
	}

	/// <summary>
	/// Too quiet to reach the target without boosting more than <see cref="AutomaticGainControl.MaxGain"/>,
	/// so they should get all of it, and come out quieter than talking. A whisper sounding like a whisper is fine.
	/// </summary>
	[TestMethod]
	[DataRow( "speech-whisper" )]
	[DataRow( "speech-far" )]
	public void QuietSpeechGetsTheMostBoost( string recording )
	{
		var level = SentSpeechLevel( recording, out var agc );

		Assert.AreEqual( AutomaticGainControl.MaxGain, agc.Gain, 0.5f,
			$"{recording} only got {agc.Gain:0.0} dB of gain (speech {agc.SpeechLevel:0.0}, noise {agc.NoiseLevel:0.0})" );

		Assert.IsTrue( level <= AutomaticGainControl.TargetLevel,
			$"{recording} sent at {level:0.0} dB, louder than the target" );
	}

	/// <summary>
	/// The default <see cref="Preferences.VoiceThreshold"/> should keep the room out, typing included, while
	/// letting talking through. Like VoiceManager's gate, it stays open for <see cref="VoiceManager.GateHoldTime"/>
	/// after the last frame over the threshold, so quiet syllables between loud ones still get sent.
	/// </summary>
	[TestMethod]
	[DataRow( "background", 0.0f, 0.05f )]
	[DataRow( "background-wearing-headset", 0.0f, 0.05f )]
	[DataRow( "background-keyboard", 0.0f, 0.05f )]
	[DataRow( "speech-whisper", 0.5f, 1.0f )]
	[DataRow( "speech-far", 0.5f, 1.0f )]
	[DataRow( "speech-normal", 0.5f, 1.0f )]
	[DataRow( "speech-loud", 0.5f, 1.0f )]
	public void DefaultThresholdLetsSpeechThrough( string recording, float least, float most )
	{
		var levels = Load( recording );
		var holdFrames = (int)MathF.Round( VoiceManager.GateHoldTime * FramesPerSecond );

		var lastLoud = int.MinValue / 2;
		var open = 0;
		for ( int i = 0; i < levels.Length; i++ )
		{
			if ( levels[i] > Preferences.VoiceThreshold )
				lastLoud = i;

			if ( i - lastLoud <= holdFrames )
				open++;
		}

		var fraction = open / (float)levels.Length;

		Assert.IsTrue( fraction >= least && fraction <= most,
			$"The gate is open for {fraction:P0} of {recording} at {Preferences.VoiceThreshold} dB, expected {least:P0} to {most:P0}" );
	}

	[TestMethod]
	public void VolumesEndUpClose()
	{
		var levels = new[] { "speech-whisper", "speech-far", "speech-normal", "speech-loud" }
			.Select( x => SentSpeechLevel( x, out _ ) )
			.ToArray();

		var spread = levels.Max() - levels.Min();

		Assert.IsTrue( spread <= 2 * SpeechTolerance, $"Sent levels {string.Join( ", ", levels.Select( x => x.ToString( "0.0" ) ) )} are {spread:0.0} dB apart" );
	}

	[TestMethod]
	[DataRow( "background" )]
	[DataRow( "background-keyboard" )]
	[DataRow( "background-wearing-headset" )]
	public void RoomIsNotLearnedAsSpeech( string recording )
	{
		var agc = Create();
		var before = agc.SpeechLevel;

		var room = Load( recording );
		for ( int pass = 0; pass < 5; pass++ )
			Play( agc, room );

		Assert.AreEqual( before, agc.SpeechLevel, 3.0f, $"Speech level moved to {agc.SpeechLevel:0.0} dB from {recording} alone (noise {agc.NoiseLevel:0.0})" );
	}

	[TestMethod]
	[DataRow( "background" )]
	[DataRow( "background-keyboard" )]
	[DataRow( "background-wearing-headset" )]
	public void RoomAloneIsNotBoosted( string recording )
	{
		var agc = Create();

		var room = Load( recording );
		for ( int pass = 0; pass < 5; pass++ )
			Play( agc, room );

		Assert.IsTrue( agc.Gain <= 1.0f, $"{agc.Gain:0.0} dB of gain from {recording} alone (speech {agc.SpeechLevel:0.0}, noise {agc.NoiseLevel:0.0})" );
	}

	[TestMethod]
	[DataRow( "background" )]
	[DataRow( "background-keyboard" )]
	[DataRow( "background-wearing-headset" )]
	public void NoiseEstimateFindsTheRoom( string recording )
	{
		var agc = Create();

		var room = Load( recording );
		for ( int pass = 0; pass < 5; pass++ )
			Play( agc, room );

		var median = Percentile( room, 0.5f );

		Assert.AreEqual( median, agc.NoiseLevel, 6.0f, $"Noise estimate {agc.NoiseLevel:0.0} dB, {recording}'s median is {median:0.0} dB" );
	}
}
