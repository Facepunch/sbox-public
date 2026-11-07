using Sandbox.Audio;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;

namespace EngineTests;

/// <summary>
/// What the mixing thread spends per buffer on voices in reverberant rooms, through the real mixer.
/// </summary>
[TestClass, DoNotParallelize]
public class AudioMixBenchmark
{
	static readonly ReverbSnapshot Hall = new() { DecayTimeLow = 2.6f, DecayTimeMid = 2.2f, DecayTimeHigh = 1.4f, Mix = 0.6f, CriticalDistanceMeters = 4f, MfpMeters = 10f };
	static readonly ReverbSnapshot Office = new() { DecayTimeLow = 0.9f, DecayTimeMid = 0.7f, DecayTimeHigh = 0.5f, Mix = 0.6f, CriticalDistanceMeters = 2f, MfpMeters = 3f };

	static SoundFile Noise( string name, float seconds, bool loop )
	{
		var rng = new Random( 1 );
		var samples = new short[(int)(44100 * seconds)];
		for ( int i = 0; i < samples.Length; i++ )
			samples[i] = (short)(rng.Next( -12000, 12000 ) * (loop ? 1f : 1f - i / (float)samples.Length));
		var pcm = System.Runtime.InteropServices.MemoryMarshal.AsBytes( samples.AsSpan() );
		return SoundFile.FromPcm( name, pcm, new SoundFile.PcmOptions { Channels = 1, Rate = 44100, Bits = 16, Loop = loop } );
	}

	static SoundHandle Play( SoundFile file, Mixer mixer, int index, float distance = 400 )
	{
		var h = Sound.PlayFile( file );
		h.TargetMixer = mixer;
		h.Position = new Vector3( distance + index * 13 % 300, (index % 7) * 60 - 180, 0 );
		h.OcclusionEnabled = false;
		h.SourceRoom = index % 2 == 0 ? Hall : Office;
		return h;
	}

	[TestMethod, TestCategory( "Slow" )]
	public void ReverbVoices()
	{
		var loop = Noise( "mixbench_loop.vsnd", 1f, true );
		var shot = Noise( "mixbench_shot.vsnd", 0.25f, false );
		if ( loop is null || shot is null || Application.IsHeadless )
			Assert.Inconclusive( "Requires the native sound system to create and mix PCM audio." );

		var mixer = Mixer.Master.AddChild();
		mixer.Name = "MixBenchmark";
		mixer.MaxVoices = 256;
		var scene = new Scene();
		using var scope = scene.Push();
		using var listener = new Listener( scene );
		scene.Listener = listener;
		var handles = new List<SoundHandle>();

		try
		{
			// Looping voices in two rooms.
			foreach ( var count in new[] { 8, 32, 64 } )
			{
				while ( handles.Count < count ) handles.Add( Play( loop, mixer, handles.Count ) );
				Console.WriteLine( $"{count,3} looping voices: {Measure( 400, null ):F0} us per buffer" );
			}

			foreach ( var h in handles ) h.Dispose();
			handles.Clear();

			// Distant voices, past where the HRTF switches to nearest neighbour.
			while ( handles.Count < 32 ) handles.Add( Play( loop, mixer, handles.Count, 2500 ) );
			Console.WriteLine( $" 32 distant voices: {Measure( 400, null ):F0} us per buffer" );

			foreach ( var h in handles ) h.Dispose();
			handles.Clear();

			// Rapid one shots, a new 0.25 s shot every 4 buffers (~22 a second), paced in real time so reverb tails last as long
			// as they would in game.
			int buffers = 0;
			double us = Measure( 600, () =>
			{
				if ( buffers++ % 4 == 0 ) handles.Add( Play( shot, mixer, buffers ) );
				handles.RemoveAll( h => !h.IsValid() );
			}, paced: true );
			Console.WriteLine( $"one shots, ~22/s: {us:F0} us per buffer, {handles.Count} voices alive at the end" );
		}
		finally
		{
			foreach ( var h in handles ) h.Dispose();
			scene.Destroy();
			mixer.Destroy();
		}
	}

	static double Measure( int buffers, Action perBuffer, bool paced = false )
	{
		double bufferSeconds = AudioEngine.MixBufferSize / (double)AudioEngine.SamplingRate;
		for ( int i = 0; i < 50; i++ )
		{
			perBuffer?.Invoke();
			MixingThread.UpdateGlobals();
			MixingThread.MixOneBuffer();
		}

		var clock = Stopwatch.StartNew();
		double mixing = 0;
		for ( int i = 0; i < buffers; i++ )
		{
			perBuffer?.Invoke();
			MixingThread.UpdateGlobals();
			long start = Stopwatch.GetTimestamp();
			MixingThread.MixOneBuffer();
			mixing += Stopwatch.GetElapsedTime( start ).TotalMicroseconds;

			if ( paced )
			{
				while ( clock.Elapsed.TotalSeconds < (i + 1) * bufferSeconds ) Thread.Yield();
			}
		}

		return mixing / buffers;
	}
}
