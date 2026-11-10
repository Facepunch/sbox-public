using System;
using Sandbox.Audio;
using Sandbox.Volumes;

namespace SceneTests.Components;

/// <summary>
/// Verifies independent global and listener-specific DSP volume selection and native mixer routing.
/// </summary>
[TestClass, DoNotParallelize]
public class DspListenerVolumeTests
{
	/// <summary>
	/// Targeted groups test their own listener positions and retain the existing global effect.
	/// Changing target identity does not reuse the previous listener's processor.
	/// </summary>
	[TestMethod]
	public void GlobalAndTargetedGroupsSelectIndependently()
	{
		var mixer = Mixer.Master.AddChild();
		mixer.Name = "DspListenerVolumeTest";
		var scene = new Scene();
		using var scope = scene.Push();
		using var above = new Listener( scene );
		try
		{
			var submerged = new Listener( scene );
			scene.Listener = submerged;
			submerged.Transform = new Transform( Vector3.Down * 32 );
			above.Transform = new Transform( Vector3.Up * 32 );
			var global = CreateVolume( scene, mixer, null );
			global.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
			global.Priority = 100;
			var water = CreateVolume( scene, mixer, submerged );
			var other = CreateVolume( scene, mixer, above );
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 2, mixer.ProcessorCount, "A targeted water group must stack with the global room group." );
			Assert.IsTrue( DspVolumeGameSystem.IsActive, "A global volume retains the room reverb override." );
			Assert.AreEqual( 1, mixer.GetProcessors().OfType<DspProcessor>().Count( x => x.TargetListener is null ) );
			Assert.AreEqual( 1, mixer.GetProcessors().OfType<DspProcessor>().Count( x => x.TargetListener == submerged ) );
			Assert.AreEqual( 0, mixer.GetProcessors().OfType<DspProcessor>().Count( x => x.TargetListener == above ) );

			above.Transform = new Transform( Vector3.Down * 16 );
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 3, mixer.ProcessorCount, "The same preset must have separate processors for different listener targets." );
			var quiet = CreateVolume( scene, mixer, above );
			quiet.Dsp = default;
			quiet.Priority = 1;
			for ( int i = 0; i < 60; i++ )
				scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 2, mixer.ProcessorCount, "Higher priority targeted silence must affect only its own listener group." );

			water.TargetListener = above;
			for ( int i = 0; i < 60; i++ )
				scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 1, mixer.ProcessorCount, "Changing target must fade the previous listener's processor." );
		}
		finally
		{
			scene.Destroy();
			mixer.Destroy();
		}
		Assert.AreEqual( 0, mixer.ProcessorCount, "Scene disposal must remove processors from their original mixer." );
	}

	/// <summary>
	/// A native PCM signal remains unchanged for an above-water listener while the submerged target is filtered.
	/// This exercises the actual per-listener mixer processor chain rather than just its routing metadata.
	/// </summary>
	[TestMethod]
	public void TargetedDspFiltersOnlyTheSubmergedListener()
	{
		var samples = new short[44100];
		for ( int i = 0; i < samples.Length; i++ )
			samples[i] = (i / 4) % 2 == 0 ? short.MaxValue : short.MinValue;
		var pcm = System.Runtime.InteropServices.MemoryMarshal.AsBytes( samples.AsSpan() );
		var file = SoundFile.FromPcm( "dsplistener_test.vsnd", pcm,
			new SoundFile.PcmOptions { Channels = 1, Rate = 44100, Bits = 16, Loop = true } );
		if ( file is null || Application.IsHeadless )
			Assert.Inconclusive( "Requires the native sound system to create and mix PCM audio." );

		var mixer = Mixer.Master.AddChild();
		mixer.Name = "DspListenerPcmTest";
		var scene = new Scene();
		using var scope = scene.Push();
		using var above = new Listener( scene );
		SoundHandle sound = null;
		try
		{
			var submerged = new Listener( scene );
			scene.Listener = submerged;
			submerged.Transform = new Transform( Vector3.Down * 32 );
			above.Transform = new Transform( Vector3.Up * 32 );
			var before = new EnergyProbe();
			mixer.AddProcessor( before );
			var volume = CreateVolume( scene, mixer, submerged );
			volume.Dsp = "lowpass.facing_away";
			scene.GameTick( 1.0 / 50 );
			Assert.IsFalse( DspVolumeGameSystem.IsActive, "A targeted effect must not disable room reverb for other listeners." );
			var dsp = mixer.GetProcessors().OfType<DspProcessor>().Single();
			dsp.Mix = 1;
			Assert.AreSame( submerged, dsp.TargetListener );
			var after = new EnergyProbe();
			mixer.AddProcessor( after );

			sound = Sound.PlayFile( file );
			Assert.IsTrue( sound.IsValid() );
			sound.TargetMixer = mixer;
			sound.SpacialBlend = 0;
			sound.DistanceAttenuation = false;
			sound.OcclusionEnabled = false;
			sound.AirAbsorption = false;
			sound.ReverbEnabled = false;
			for ( int i = 0; i < 32; i++ )
			{
				MixingThread.UpdateGlobals();
				MixingThread.MixOneBuffer();
			}

			Assert.IsTrue( before.Above > 0 && before.Below > 0, "Both real scene listeners must receive the tone." );
			Assert.AreEqual( 1.0, after.Above / before.Above, 0.01, "Targeted DSP must leave the other listener's samples unchanged." );
			Assert.IsTrue( after.Below / before.Below < 0.5, $"The targeted low-pass must attenuate its listener's high frequency tone: {after.Below / before.Below}." );
		}
		finally
		{
			sound?.Dispose();
			scene.Destroy();
			foreach ( var processor in mixer.GetProcessors() )
				mixer.RemoveProcessor( processor );
			mixer.Destroy();
		}
	}

	/// <summary>
	/// Replacing a named mixer creates routing on the replacement while retaining the original owner for cleanup.
	/// </summary>
	[TestMethod]
	public void MixerReplacementRetainsActualProcessorOwnership()
	{
		var first = Mixer.Master.AddChild();
		first.Name = "DspListenerReplacementTest";
		Mixer second = null;
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var volume = CreateVolume( scene, first, null );
			volume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
			volume.TargetMixer = new MixerHandle { Name = first.Name };
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 1, first.ProcessorCount );
			first.Name = "DspListenerReplacementOld";
			second = Mixer.Master.AddChild();
			second.Name = "DspListenerReplacementTest";
			scene.GameTick( 1.0 / 50 );
			Assert.AreEqual( 1, second.ProcessorCount, "The resolved mixer instance participates in processor identity." );
		}
		finally
		{
			scene.Destroy();
			second?.Destroy();
			first.Destroy();
		}
		Assert.AreEqual( 0, first.ProcessorCount );
		Assert.AreEqual( 0, second.ProcessorCount );
	}

	static DspVolume CreateVolume( Scene scene, Mixer mixer, Listener listener )
	{
		var volume = scene.CreateObject().Components.Create<DspVolume>();
		volume.Dsp = "water.small";
		volume.TargetMixer = mixer;
		volume.TargetListener = listener;
		volume.SceneVolume = new SceneVolume
		{
			Type = SceneVolume.VolumeTypes.Box,
			Box = new BBox( new Vector3( -100, -100, -100 ), new Vector3( 100, 100, 0 ) )
		};
		return volume;
	}

	sealed class EnergyProbe : AudioProcessor
	{
		public double Above;
		public double Below;

		protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
		{
			double energy = 0;
			foreach ( float sample in input )
				energy += sample * sample;
			if ( Listener.Position.z > 0 )
				Above += energy;
			else
				Below += energy;
		}
	}
}
