using Sandbox.Audio;
using System;

namespace EngineTests;

[TestClass]
public class AudioTest
{
	[TestMethod]
	public void Silence()
	{
		using var buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreNotEqual( 0, buffer.LevelMax );
		buffer.Silence();
		Assert.AreEqual( 0, buffer.LevelMax );
	}

	[TestMethod]
	public void LevelMax()
	{
		using var buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreEqual( buffer.LevelMax, buffer.Buffer.ToArray().Max() );
	}

	[TestMethod]
	public void LevelAvg()
	{
		using var buffer = new MixBuffer();
		buffer.RandomFill();
		Assert.AreEqual( buffer.LevelAvg, buffer.Buffer.ToArray().Average(), 0.001f );
	}

	[TestMethod]
	public void Copy()
	{
		using var buffer = new MixBuffer();
		using var bufferTarget = new MixBuffer();
		bufferTarget.Silence();
		Assert.IsTrue( bufferTarget.LevelAvg == 0 );

		buffer.RandomFill();

		Assert.IsFalse( buffer.LevelAvg == 0 );

		bufferTarget.CopyFrom( buffer );

		Assert.AreEqual( buffer.LevelAvg, bufferTarget.LevelAvg, 0.001f );
	}


	[TestMethod]
	public void MixFrom()
	{
		using var buffer = new MixBuffer();
		using var bufferTarget = new MixBuffer();
		bufferTarget.Silence();
		Assert.IsTrue( bufferTarget.LevelAvg == 0 );

		buffer.RandomFill();

		Assert.IsFalse( buffer.LevelAvg == 0 );

		bufferTarget.MixFrom( buffer, 0.5f );
		Assert.AreEqual( buffer.LevelAvg * 0.5f, bufferTarget.LevelAvg, 0.001f );
	}

	/// <summary>
	/// Records the peak sample it was handed, so a test can assert which audio reached a
	/// mixer's processor chain.
	/// </summary>
	class LevelProbe : AudioProcessor
	{
		public float PeakSeen;
		public int RunCount;

		protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
		{
			RunCount++;

			foreach ( var sample in input )
				PeakSeen = Math.Max( PeakSeen, Math.Abs( sample ) );
		}
	}

	/// <summary>
	/// Built in memory rather than loaded from disk, so the mix graph tests don't have to wait
	/// on an async resource load to produce a sampler.
	/// </summary>
	static SoundFile CreateTestTone()
	{
		const int sampleCount = 44100;

		var samples = new short[sampleCount];

		// Full scale, so the level survives the direct mix gain ramp.
		for ( var i = 0; i < sampleCount; i++ )
			samples[i] = (i / 220) % 2 == 0 ? short.MaxValue : short.MinValue;

		var pcm = System.Runtime.InteropServices.MemoryMarshal.AsBytes( samples.AsSpan() );

		return SoundFile.FromPcm( "mixergraphtest_tone.vsnd", pcm,
			new SoundFile.PcmOptions { Channels = 1, Rate = 44100, Bits = 16, Loop = true } );
	}

	/// <summary>
	/// Plays a tone on a child mixer and returns the probe installed on Master. Master has no
	/// voices of its own, so everything the probe sees arrived from the child.
	/// </summary>
	static LevelProbe ProbeMasterWithChildAudio( Action<Mixer> configureChild )
	{
		var child = Mixer.FindMixerByName( "Game" );
		Assert.IsNotNull( child, "Default mixer layout should contain a Game mixer" );

		configureChild?.Invoke( child );

		var probe = new LevelProbe();
		Mixer.Master.AddProcessor( probe );

		var soundFile = CreateTestTone();

		// CI runners have no audio device, so the native sound system can't create sounds.
		if ( soundFile is null )
		{
			Assert.Inconclusive( "Native sound system can't create sounds on this machine (no audio device?) - skipping mix graph test" );
		}

		var handle = Sound.PlayFile( soundFile );
		Assert.IsTrue( handle.IsValid(), "Sound should be playing" );

		// ListenLocal routes through Listener.Local, so we don't need a scene listener.
		handle.ListenLocal = true;
		handle.TargetMixer = child;

		// The gain interpolator ramps up, so the first buffers can be silent.
		for ( var i = 0; i < 32 && probe.PeakSeen <= 0f; i++ )
		{
			MixingThread.UpdateGlobals();
			MixingThread.MixOneBuffer();
		}

		handle.Stop( 0 );

		return probe;
	}

	/// <summary>
	/// A processor on a parent mixer must process audio from its child mixers, not just voices
	/// played directly on it. Regressed in 4ab8776fdb.
	/// </summary>
	[TestMethod]
	public void ParentMixerProcessesChildAudio()
	{
		Mixer.ResetToDefault();

		try
		{
			var probe = ProbeMasterWithChildAudio( null );

			Assert.AreNotEqual( 0, probe.RunCount,
				"Master's processor never ran - a bus mixer with no voices of its own applies no processing at all" );

			Assert.IsTrue( probe.PeakSeen > 0f,
				"Master's processor only saw silence - child mixer audio bypassed the parent's processor chain" );
		}
		finally
		{
			Mixer.ResetToDefault();
		}
	}

	/// <summary>
	/// A child mixer's own volume still has to reach its parent, now that the parent reads the
	/// per-listener buffers instead of the collapsed output.
	/// </summary>
	[TestMethod]
	public void ChildMixerVolumeCascadesToParent()
	{
		Mixer.ResetToDefault();

		try
		{
			var probe = ProbeMasterWithChildAudio( child => child.Volume = 0f );

			// RunCount rules out the silence below just being "nothing ever played".
			Assert.AreNotEqual( 0, probe.RunCount, "Child audio should still reach Master's processor chain" );

			Assert.AreEqual( 0f, probe.PeakSeen, "A child mixer at zero volume should reach its parent silent" );
		}
		finally
		{
			Mixer.ResetToDefault();
		}
	}
}
