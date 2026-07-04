using NativeEngine;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Editor;

/// <summary>
/// A viseme (mouth shape) active over a span of time.
/// </summary>
public struct VisemeFrame
{
	/// <summary>
	/// Viseme index, 0-14. See <see cref="LipSyncGenerator.Label"/>.
	/// </summary>
	[JsonPropertyName( "viseme" )]
	public int Viseme { get; set; }

	/// <summary>
	/// Start time in seconds.
	/// </summary>
	[JsonPropertyName( "start" )]
	public float StartTime { get; set; }

	/// <summary>
	/// End time in seconds.
	/// </summary>
	[JsonPropertyName( "end" )]
	public float EndTime { get; set; }
}

/// <summary>
/// Generates face lipsync visemes from audio.
/// </summary>
public static class LipSyncGenerator
{
	// The 15 visemes in OVRLipSync order. MorphNames match the viseme morphs on a model.
	static readonly string[] MorphNames =
	{
		"viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH", "viseme_DD",
		"viseme_KK", "viseme_CH", "viseme_SS", "viseme_NN", "viseme_RR",
		"viseme_AA", "viseme_E", "viseme_I", "viseme_O", "viseme_U",
	};

	static readonly string[] Labels =
	{
		"sil", "PP", "FF", "TH", "DD", "KK", "CH", "SS", "NN", "RR", "AA", "E", "I", "O", "U",
	};

	// Samples of audio fed to the analyzer per step. Smaller = finer timing resolution.
	const int HopSize = 512;

	// A viseme must be at least this strong to count as active rather than silence.
	const float ActiveThreshold = 0.3f;

	/// <summary>
	/// Number of visemes.
	/// </summary>
	public static int Count => MorphNames.Length;

	/// <summary>
	/// Model morph name for a viseme, e.g. "viseme_PP".
	/// </summary>
	public static string MorphName( int viseme ) => MorphNames[viseme];

	/// <summary>
	/// Short display label for a viseme, e.g. "PP".
	/// </summary>
	public static string Label( int viseme ) => Labels[viseme];

	/// <summary>
	/// Analyze audio and return the visemes it produces over time, with silence dropped.
	/// <paramref name="samples"/> is a mono stream that spans <paramref name="duration"/> seconds,
	/// so times are scaled to the duration and line up with the sound editor timeline.
	/// </summary>
	public static List<VisemeFrame> Generate( short[] samples, float duration )
	{
		var frames = new List<VisemeFrame>();

		if ( samples is null || samples.Length == 0 || duration <= 0 )
			return frames;

		if ( !OperatingSystem.IsWindows() )
			return frames;

		// Rate the buffer plays back at, derived from its own length rather than trusting metadata.
		var rate = Math.Max( 1, (int)MathF.Round( samples.Length / duration ) );

		if ( OVRLipSyncGlobal.ovrLipSync_CreateContextEx( out var context, OVRLipSync.ContextProvider.Enhanced_with_Laughter, rate, true ) != OVRLipSync.Result.Success )
			return frames;

		try
		{
			Merge( Analyze( context, samples, duration ), frames );
		}
		finally
		{
			OVRLipSyncGlobal.ovrLipSync_DestroyContext( context );
		}

		return frames;
	}

	// Feed the whole buffer through the analyzer in chunks, recording the dominant viseme each step.
	static List<(float Time, int Viseme)> Analyze( uint context, short[] samples, float duration )
	{
		var result = new List<(float, int)>();
		var visemes = new float[Count];

		var visemesHandle = GCHandle.Alloc( visemes, GCHandleType.Pinned );
		var samplesHandle = GCHandle.Alloc( samples, GCHandleType.Pinned );

		try
		{
			var pVisemes = visemesHandle.AddrOfPinnedObject();
			var pSamples = samplesHandle.AddrOfPinnedObject();

			for ( int offset = 0; offset < samples.Length; offset += HopSize )
			{
				int count = Math.Min( HopSize, samples.Length - offset );

				var frame = new OVRLipSync.Frame
				{
					Visemes = pVisemes,
					VisemesLength = (uint)visemes.Length,
				};

				var r = OVRLipSyncGlobal.ovrLipSync_ProcessFrameEx(
					context,
					pSamples + offset * sizeof( short ),
					count,
					OVRLipSync.AudioDataType.S16_Mono,
					ref frame );

				if ( r != OVRLipSync.Result.Success )
					continue;

				// Position through the buffer scaled to the clip duration, so it lines up with the
				// timeline exactly. Shift back by the frame delay to fill the otherwise-empty start.
				var audioTime = (offset + count) / (float)samples.Length * duration;
				var time = MathF.Max( 0, audioTime - frame.FrameDelay / 1000f );

				result.Add( (time, Dominant( visemes )) );
			}
		}
		finally
		{
			visemesHandle.Free();
			samplesHandle.Free();
		}

		return result;
	}

	// Loudest non-silent viseme, or silence (0) if nothing is strong enough.
	static int Dominant( float[] visemes )
	{
		int best = 0;
		float bestWeight = 0.0f;

		for ( int v = 1; v < visemes.Length; v++ )
		{
			if ( visemes[v] > bestWeight )
			{
				bestWeight = visemes[v];
				best = v;
			}
		}

		return bestWeight >= ActiveThreshold ? best : 0;
	}

	// Merge runs of the same viseme into frames, dropping silence.
	static void Merge( List<(float Time, int Viseme)> samples, List<VisemeFrame> frames )
	{
		if ( samples.Count == 0 )
			return;

		int runViseme = samples[0].Viseme;
		float runStart = samples[0].Time;

		// One past the end acts as a virtual sample that closes the final run.
		for ( int i = 1; i <= samples.Count; i++ )
		{
			var viseme = i < samples.Count ? samples[i].Viseme : -1;
			var time = i < samples.Count ? samples[i].Time : samples[^1].Time;

			if ( viseme == runViseme )
				continue;

			if ( runViseme > 0 && time > runStart )
			{
				frames.Add( new VisemeFrame
				{
					Viseme = runViseme,
					StartTime = runStart,
					EndTime = time,
				} );
			}

			runViseme = viseme;
			runStart = time;
		}
	}
}
