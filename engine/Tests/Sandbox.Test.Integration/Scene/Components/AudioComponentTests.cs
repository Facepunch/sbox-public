using Sandbox.Audio;
using Sandbox.Volumes;
using System.Collections.Generic;

namespace SceneTests.Components;

/// <summary>
/// Pins the scene audio components: SoundPointComponent and SoundBoxComponent
/// playback state and handle overrides, AudioListener registration with the
/// scene, SoundscapeTrigger listener testing, DspVolume activation and the
/// property/serialization surface of Voice and LipSync.
/// </summary>
[TestClass]
public class AudioComponentTest
{
	/// <summary>
	/// Builds an in-code SoundEvent backed by a sound file that ships in
	/// game/core, so playback tests use a real loadable asset.
	/// </summary>
	static SoundEvent CreateTestSoundEvent()
	{
		var soundFile = SoundFile.Load( "sounds/kenney/ui/error_001.vsnd" );

		// CI runners have no audio device, so the native sound system can't precache
		// anything and Load returns null for every sound. Inconclusive (skipped) beats
		// failing - these tests still run on any machine where sound works.
		if ( soundFile is null )
		{
			Assert.Inconclusive( "Native sound system can't precache sounds on this machine (no audio device?) - skipping sound file dependent test" );
		}

		return new SoundEvent
		{
			Volume = 0.65f,
			Distance = 2222f,
			Sounds = new List<SoundFile> { soundFile }
		};
	}

	/// <summary>
	/// Serializes a GameObject to json, makes the id guids unique and
	/// deserializes the json into a fresh disabled GameObject in the same scene.
	/// </summary>
	static GameObject RoundTrip( GameObject source )
	{
		var json = source.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		return copy;
	}

	/// <summary>
	/// A freshly created sound component should have the documented defaults
	/// for play, override, repeat, attenuation, occlusion and reverb settings.
	/// </summary>
	[TestMethod]
	public void BaseSoundDefaults()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );

		Assert.IsNull( comp.SoundEvent );
		Assert.IsTrue( comp.PlayOnStart );
		Assert.IsFalse( comp.StopOnNew );
		Assert.IsFalse( comp.SoundOverride );
		Assert.AreEqual( 1.0f, comp.Volume );
		Assert.AreEqual( 1.0f, comp.Pitch );
		Assert.IsFalse( comp.Force2d );
		Assert.IsFalse( comp.Repeat );
		Assert.AreEqual( 1.0f, comp.MinRepeatTime );
		Assert.AreEqual( 1.0f, comp.MaxRepeatTime );
		Assert.IsFalse( comp.DistanceAttenuationOverride );
		Assert.IsFalse( comp.DistanceAttenuation );
		Assert.AreEqual( 512f, comp.Distance );
		Assert.IsFalse( comp.OcclusionOverride );
		Assert.IsFalse( comp.OcclusionEnabled );
		Assert.IsFalse( comp.ReverbOverride );
		Assert.IsFalse( comp.ReverbEnabled );
		Assert.IsNull( comp.SoundHandleInternal );
	}

	/// <summary>
	/// StartSound creates a live playing handle and StopSound fades it out and
	/// clears the component's reference to it, which also drives the
	/// ITemporaryEffect.IsActive state.
	/// </summary>
	[TestMethod]
	public void SoundPointStartAndStopSound()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.PlayOnStart = false;
		comp.Enabled = true;

		Component.ITemporaryEffect effect = comp;

		// PlayOnStart false - nothing should be playing yet
		Assert.IsNull( comp.SoundHandleInternal );
		Assert.IsFalse( effect.IsActive );

		comp.StartSound();

		var handle = comp.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.IsTrue( handle.IsPlaying );
		Assert.IsTrue( effect.IsActive );

		comp.StopSound();

		// The point component drops its handle, the old handle fades out
		Assert.IsNull( comp.SoundHandleInternal );
		Assert.IsFalse( effect.IsActive );
		Assert.IsTrue( handle.IsValid() );
		Assert.IsTrue( handle.IsFadingOut );
	}

	/// <summary>
	/// With PlayOnStart enabled the sound starts when the component is enabled,
	/// stops when it is disabled, and a re-enable starts a brand new handle.
	/// </summary>
	[TestMethod]
	public void SoundPointPlayOnStartWithEnable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.PlayOnStart = true;
		comp.Enabled = true;

		var first = comp.SoundHandleInternal;
		Assert.IsTrue( first.IsValid() );
		Assert.IsTrue( first.IsPlaying );

		comp.Enabled = false;

		Assert.IsNull( comp.SoundHandleInternal );
		Assert.IsTrue( first.IsFadingOut );

		comp.Enabled = true;

		var second = comp.SoundHandleInternal;
		Assert.IsTrue( second.IsValid() );
		Assert.AreNotSame( first, second );
	}

	/// <summary>
	/// A sound point without a SoundEvent assigned is inert - enabling it and
	/// calling StartSound never produces a handle.
	/// </summary>
	[TestMethod]
	public void SoundPointWithoutSoundEventIsInert()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>();

		Assert.IsNull( comp.SoundHandleInternal );

		comp.StartSound();

		Assert.IsNull( comp.SoundHandleInternal );

		Component.ITemporaryEffect effect = comp;
		Assert.IsFalse( effect.IsActive );
	}

	/// <summary>
	/// Without any component overrides the handle inherits its volume, pitch
	/// and distance settings from the SoundEvent that was played.
	/// </summary>
	[TestMethod]
	public void SoundPointEventSettingsApplyToHandle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.Enabled = true;

		var handle = comp.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.AreEqual( 0.65f, handle.Volume, 0.0001f );
		Assert.AreEqual( 1.0f, handle.Pitch, 0.0001f );
		Assert.AreEqual( 2222f, handle.Distance );
		Assert.IsTrue( handle.DistanceAttenuation );
		Assert.IsTrue( handle.OcclusionEnabled );
		Assert.IsTrue( handle.ReverbEnabled );
	}

	/// <summary>
	/// With SoundOverride enabled the component's Volume and Pitch are pushed
	/// onto the playing handle, replacing the sound event's values.
	/// </summary>
	[TestMethod]
	public void SoundPointSoundOverrideAppliesToHandle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.SoundOverride = true;
		comp.Volume = 0.25f;
		comp.Pitch = 1.75f;
		comp.Enabled = true;

		var handle = comp.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.AreEqual( 0.25f, handle.Volume );
		Assert.AreEqual( 1.75f, handle.Pitch );
		Assert.IsFalse( handle.ListenLocal );
	}

	/// <summary>
	/// The distance attenuation override replaces the sound event's distance
	/// settings on the handle, including turning attenuation off entirely.
	/// </summary>
	[TestMethod]
	public void SoundPointDistanceAttenuationOverrideAppliesToHandle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.DistanceAttenuationOverride = true;
		comp.DistanceAttenuation = false;
		comp.Distance = 1234f;
		comp.Enabled = true;

		var handle = comp.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.IsFalse( handle.DistanceAttenuation );
		Assert.AreEqual( 1234f, handle.Distance );
	}

	/// <summary>
	/// Force2d pins the handle in front of the listener and disables occlusion,
	/// air absorption, distance attenuation and transmission. ListenLocal is
	/// only set when SoundOverride is also enabled - pin that quirk.
	/// </summary>
	[TestMethod]
	public void SoundPointForce2dForcesLocalPlayback()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var force2dOnly = go.Components.Create<SoundPointComponent>( false );
		force2dOnly.SoundEvent = CreateTestSoundEvent();
		force2dOnly.SoundOverride = false;
		force2dOnly.Force2d = true;
		force2dOnly.Enabled = true;

		var handle = force2dOnly.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.AreEqual( Vector3.Forward * 10.0f, handle.Position );
		Assert.IsFalse( handle.OcclusionEnabled );
		Assert.IsFalse( handle.AirAbsorption );
		Assert.IsFalse( handle.DistanceAttenuation );
		Assert.IsFalse( handle.Transmission );
		Assert.IsFalse( handle.ListenLocal, "ListenLocal is only applied when SoundOverride is set" );

		var withOverride = go.Components.Create<SoundPointComponent>( false );
		withOverride.SoundEvent = CreateTestSoundEvent();
		withOverride.SoundOverride = true;
		withOverride.Force2d = true;
		withOverride.Enabled = true;

		var overrideHandle = withOverride.SoundHandleInternal;
		Assert.IsTrue( overrideHandle.IsValid() );
		Assert.IsTrue( overrideHandle.ListenLocal );
	}

	/// <summary>
	/// The occlusion and reverb overrides replace the sound event's enabled
	/// defaults on the playing handle.
	/// </summary>
	[TestMethod]
	public void SoundPointOcclusionAndReverbOverridesApplyToHandle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.OcclusionOverride = true;
		comp.OcclusionEnabled = false;
		comp.ReverbOverride = true;
		comp.ReverbEnabled = false;
		comp.Enabled = true;

		var handle = comp.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.IsFalse( handle.OcclusionEnabled );
		Assert.IsFalse( handle.ReverbEnabled );
	}

	/// <summary>
	/// StartSound on an already playing point sound is a no-op by default, but
	/// with StopOnNew enabled it fades out the old handle and starts a new one.
	/// </summary>
	[TestMethod]
	public void SoundPointStopOnNewReplacesHandle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.PlayOnStart = false;
		comp.Enabled = true;

		comp.StartSound();
		var first = comp.SoundHandleInternal;
		Assert.IsTrue( first.IsValid() );

		// Default StopOnNew false - the playing handle is kept
		comp.StartSound();
		Assert.AreSame( first, comp.SoundHandleInternal );

		comp.StopOnNew = true;
		comp.StartSound();

		var second = comp.SoundHandleInternal;
		Assert.IsTrue( second.IsValid() );
		Assert.AreNotSame( first, second );
		Assert.IsTrue( first.IsFadingOut );
	}

	/// <summary>
	/// The point component's update keeps the playing handle at the
	/// GameObject's world position as it moves.
	/// </summary>
	[TestMethod]
	public void SoundPointUpdateFollowsGameObjectPosition()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.Enabled = true;

		var handle = comp.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.AreEqual( Vector3.Zero, handle.Position );

		go.WorldPosition = new Vector3( 50, 60, 70 );
		scene.GameTick();

		Assert.AreEqual( new Vector3( 50, 60, 70 ), handle.Position );
	}

	/// <summary>
	/// All sound point properties with non-default values survive a json
	/// serialization round trip of the owning GameObject.
	/// </summary>
	[TestMethod]
	public void SoundPointSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundPointComponent>( false );
		comp.TargetMixer = new MixerHandle { Name = "music" };
		comp.PlayOnStart = false;
		comp.StopOnNew = true;
		comp.SoundOverride = true;
		comp.Volume = 0.25f;
		comp.Pitch = 1.75f;
		comp.Force2d = true;
		comp.Repeat = true;
		comp.MinRepeatTime = 2.5f;
		comp.MaxRepeatTime = 7.5f;
		comp.DistanceAttenuationOverride = true;
		comp.DistanceAttenuation = true;
		comp.Distance = 1234f;
		comp.OcclusionOverride = true;
		comp.OcclusionEnabled = true;
		comp.ReverbOverride = true;
		comp.ReverbEnabled = true;

		var copy = RoundTrip( go );
		var loaded = copy.Components.Get<SoundPointComponent>( FindMode.EverythingInSelf );

		Assert.IsNotNull( loaded );
		Assert.AreEqual( "music", loaded.TargetMixer.Name );
		Assert.IsFalse( loaded.PlayOnStart );
		Assert.IsTrue( loaded.StopOnNew );
		Assert.IsTrue( loaded.SoundOverride );
		Assert.AreEqual( 0.25f, loaded.Volume );
		Assert.AreEqual( 1.75f, loaded.Pitch );
		Assert.IsTrue( loaded.Force2d );
		Assert.IsTrue( loaded.Repeat );
		Assert.AreEqual( 2.5f, loaded.MinRepeatTime );
		Assert.AreEqual( 7.5f, loaded.MaxRepeatTime );
		Assert.IsTrue( loaded.DistanceAttenuationOverride );
		Assert.IsTrue( loaded.DistanceAttenuation );
		Assert.AreEqual( 1234f, loaded.Distance );
		Assert.IsTrue( loaded.OcclusionOverride );
		Assert.IsTrue( loaded.OcclusionEnabled );
		Assert.IsTrue( loaded.ReverbOverride );
		Assert.IsTrue( loaded.ReverbEnabled );
	}

	/// <summary>
	/// Enabling a sound box computes its inner bounds from the world position
	/// and the box size, centered on the GameObject.
	/// </summary>
	[TestMethod]
	public void SoundBoxInnerBoundsComputedOnEnable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var comp = go.Components.Create<SoundBoxComponent>( false );
		comp.PlayOnStart = false;
		comp.Scale = new Vector3( 10, 20, 30 );
		comp.Enabled = true;

		Assert.AreEqual( new Vector3( 95, -10, -15 ), comp.Inner.Mins );
		Assert.AreEqual( new Vector3( 105, 10, 15 ), comp.Inner.Maxs );
	}

	/// <summary>
	/// A sound box keeps its handle reference after StopSound - the handle just
	/// fades out - and StartSound while the old handle is still fading does not
	/// start a new sound. This differs from the point component, which clears
	/// its handle on stop.
	/// </summary>
	[TestMethod]
	public void SoundBoxPlaysAndFadesOutOnStop()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundBoxComponent>( false );
		comp.SoundEvent = CreateTestSoundEvent();
		comp.PlayOnStart = true;
		comp.Enabled = true;

		var handle = comp.SoundHandleInternal;
		Assert.IsTrue( handle.IsValid() );
		Assert.IsTrue( handle.IsPlaying );

		comp.StopSound();

		Assert.AreSame( handle, comp.SoundHandleInternal );
		Assert.IsTrue( handle.IsFadingOut );
		Assert.IsTrue( handle.IsPlaying );

		// Still "playing" while fading, so StartSound keeps the same handle
		comp.StartSound();
		Assert.AreSame( handle, comp.SoundHandleInternal );
	}

	/// <summary>
	/// The sound box positions its sound at the closest point on its inner
	/// bounds to the closest listener - falling back to the world origin when
	/// the scene has no listeners.
	/// </summary>
	[TestMethod]
	public void SoundBoxSoundPositionTracksClosestPoint()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var comp = go.Components.Create<SoundBoxComponent>( false );
		comp.PlayOnStart = false;
		comp.Scale = new Vector3( 10, 20, 30 );
		comp.Enabled = true;

		// No listeners - closest point to the default origin
		scene.GameTick();
		Assert.AreEqual( new Vector3( 95, 0, 0 ), comp.SndPos );

		// Add a listener on the other side of the box
		var listenerGo = scene.CreateObject();
		listenerGo.WorldPosition = new Vector3( 200, 0, 0 );
		listenerGo.Components.Create<AudioListener>();

		scene.GameTick();
		Assert.AreEqual( new Vector3( 105, 0, 0 ), comp.SndPos );
	}

	/// <summary>
	/// The sound box extents and base sound properties survive a json
	/// serialization round trip.
	/// </summary>
	[TestMethod]
	public void SoundBoxSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SoundBoxComponent>( false );
		comp.Scale = new Vector3( 11, 22, 33 );
		comp.PlayOnStart = false;
		comp.Repeat = true;
		comp.MinRepeatTime = 3f;
		comp.MaxRepeatTime = 9f;

		var copy = RoundTrip( go );
		var loaded = copy.Components.Get<SoundBoxComponent>( FindMode.EverythingInSelf );

		Assert.IsNotNull( loaded );
		Assert.AreEqual( new Vector3( 11, 22, 33 ), loaded.Scale );
		Assert.IsFalse( loaded.PlayOnStart );
		Assert.IsTrue( loaded.Repeat );
		Assert.AreEqual( 3f, loaded.MinRepeatTime );
		Assert.AreEqual( 9f, loaded.MaxRepeatTime );
	}

	/// <summary>
	/// Enabling an AudioListener registers a listener with the scene at the
	/// GameObject's position, disabling removes and disposes it, and a
	/// re-enable registers a fresh listener instance.
	/// </summary>
	[TestMethod]
	public void AudioListenerRegistersWithScene()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 200, 300 );

		var comp = go.Components.Create<AudioListener>();

		Assert.IsNotNull( scene.Listeners );
		Assert.AreEqual( 1, scene.Listeners.Count );

		var listener = scene.Listeners.Single();
		Assert.IsTrue( listener.IsValid );
		Assert.AreEqual( new Vector3( 100, 200, 300 ), listener.Transform.Position );

		comp.Enabled = false;

		Assert.AreEqual( 0, scene.Listeners.Count );
		Assert.IsFalse( listener.IsValid );

		comp.Enabled = true;

		Assert.AreEqual( 1, scene.Listeners.Count );
		Assert.AreNotSame( listener, scene.Listeners.Single() );
	}

	/// <summary>
	/// The listener transform follows the GameObject immediately when it moves,
	/// driven by the transform changed callback.
	/// </summary>
	[TestMethod]
	public void AudioListenerTracksGameObjectPosition()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<AudioListener>();

		var listener = scene.Listeners.Single();
		Assert.AreEqual( Vector3.Zero, listener.Transform.Position );

		go.WorldPosition = new Vector3( 5, 6, 7 );

		Assert.AreEqual( new Vector3( 5, 6, 7 ), listener.Transform.Position );
	}

	/// <summary>
	/// With multiple AudioListener components active, the scene resolves the
	/// listener closest to a queried point.
	/// </summary>
	[TestMethod]
	public void ClosestListenerLookupPicksNearest()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var nearGo = scene.CreateObject();
		nearGo.WorldPosition = new Vector3( 1000, 0, 0 );
		nearGo.Components.Create<AudioListener>();

		var farGo = scene.CreateObject();
		farGo.WorldPosition = Vector3.Zero;
		farGo.Components.Create<AudioListener>();

		var closest = scene.FindClosestListener( new Vector3( 900, 0, 0 ) );

		Assert.IsNotNull( closest );
		Assert.AreEqual( new Vector3( 1000, 0, 0 ), closest.Position );
	}

	/// <summary>
	/// TestListenerPosition matches everywhere for point triggers, strictly
	/// inside the radius for sphere triggers, and inside the local-space box
	/// (extents of +/- BoxSize) for box triggers - all relative to the
	/// trigger's world transform.
	/// </summary>
	[TestMethod]
	public void SoundscapeTriggerListenerPositionTests()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 1000, 0, 0 );
		var trigger = go.Components.Create<SoundscapeTrigger>();

		trigger.Type = SoundscapeTrigger.TriggerType.Point;
		Assert.IsTrue( trigger.TestListenerPosition( new Vector3( 99999, 0, 0 ) ) );

		trigger.Type = SoundscapeTrigger.TriggerType.Sphere;
		trigger.Radius = 100f;
		Assert.IsTrue( trigger.TestListenerPosition( new Vector3( 1099, 0, 0 ) ) );
		Assert.IsFalse( trigger.TestListenerPosition( new Vector3( 1100, 0, 0 ) ), "the sphere boundary itself is excluded" );
		Assert.IsFalse( trigger.TestListenerPosition( new Vector3( 1101, 0, 0 ) ) );

		trigger.Type = SoundscapeTrigger.TriggerType.Box;
		trigger.BoxSize = new Vector3( 50, 50, 50 );
		Assert.IsTrue( trigger.TestListenerPosition( new Vector3( 1040, 25, -25 ) ) );
		Assert.IsFalse( trigger.TestListenerPosition( new Vector3( 1075, 0, 0 ) ) );
	}

	/// <summary>
	/// Disabling a soundscape trigger stops it - the Playing flag is reset so a
	/// re-entered trigger starts cleanly.
	/// </summary>
	[TestMethod]
	public void SoundscapeTriggerStopsPlayingOnDisable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var trigger = go.Components.Create<SoundscapeTrigger>();

		trigger.Playing = true;
		Assert.IsTrue( trigger.Playing );

		trigger.Enabled = false;

		Assert.IsFalse( trigger.Playing );
	}

	/// <summary>
	/// Soundscape trigger shape, volume and mixer properties survive a json
	/// serialization round trip with non-default values.
	/// </summary>
	[TestMethod]
	public void SoundscapeTriggerSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var trigger = go.Components.Create<SoundscapeTrigger>( false );
		trigger.Type = SoundscapeTrigger.TriggerType.Sphere;
		trigger.Radius = 123f;
		trigger.BoxSize = new Vector3( 11, 22, 33 );
		trigger.Volume = 0.5f;
		trigger.StayActiveOnExit = false;
		trigger.TargetMixer = new MixerHandle { Name = "music" };

		var copy = RoundTrip( go );
		var loaded = copy.Components.Get<SoundscapeTrigger>( FindMode.EverythingInSelf );

		Assert.IsNotNull( loaded );
		Assert.AreEqual( SoundscapeTrigger.TriggerType.Sphere, loaded.Type );
		Assert.AreEqual( 123f, loaded.Radius );
		Assert.AreEqual( new Vector3( 11, 22, 33 ), loaded.BoxSize );
		Assert.AreEqual( 0.5f, loaded.Volume );
		Assert.IsFalse( loaded.StayActiveOnExit );
		Assert.AreEqual( "music", loaded.TargetMixer.Name );
	}

	/// <summary>
	/// A DspVolume defaults to targeting the Game mixer with priority zero, and
	/// IsInfinite reflects the scene volume type.
	/// </summary>
	[TestMethod]
	public void DspVolumeDefaultsAndInfiniteVolume()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var dsp = go.Components.Create<DspVolume>();

		Assert.AreEqual( "Game", dsp.TargetMixer.Name );
		Assert.AreEqual( 0, dsp.Priority );
		Assert.IsNull( dsp.Dsp.Name );
		Assert.AreEqual( SceneVolume.VolumeTypes.Box, dsp.SceneVolume.Type );
		Assert.IsFalse( dsp.IsInfinite );

		dsp.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };

		Assert.IsTrue( dsp.IsInfinite );
	}

	/// <summary>
	/// The Dsp volume game system flags itself active after a tick when the
	/// listener is inside an enabled DspVolume with an effect set, and goes
	/// inactive again once the volume is disabled.
	/// </summary>
	[TestMethod]
	public void DspVolumeSystemActivation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var dsp = go.Components.Create<DspVolume>( false );
		dsp.Dsp = "underwater";
		dsp.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		dsp.Enabled = true;

		scene.GameTick();
		Assert.IsTrue( DspVolumeGameSystem.IsActive );

		dsp.Enabled = false;

		scene.GameTick();
		Assert.IsFalse( DspVolumeGameSystem.IsActive );
	}

	/// <summary>
	/// DspVolume effect, mixer, priority and scene volume shape survive a json
	/// serialization round trip.
	/// </summary>
	[TestMethod]
	public void DspVolumeSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var dsp = go.Components.Create<DspVolume>( false );
		dsp.Dsp = "underwater";
		dsp.TargetMixer = new MixerHandle { Name = "music" };
		dsp.Priority = 7;
		dsp.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Sphere, Sphere = new Sphere( 0, 99 ) };

		var copy = RoundTrip( go );
		var loaded = copy.Components.Get<DspVolume>( FindMode.EverythingInSelf );

		Assert.IsNotNull( loaded );
		Assert.AreEqual( "underwater", loaded.Dsp.Name );
		Assert.AreEqual( "music", loaded.TargetMixer.Name );
		Assert.AreEqual( 7, loaded.Priority );
		Assert.AreEqual( SceneVolume.VolumeTypes.Sphere, loaded.SceneVolume.Type );
		Assert.AreEqual( 99f, loaded.SceneVolume.Sphere.Radius );
	}

	/// <summary>
	/// A never-enabled Voice component exposes its documented property defaults
	/// and accepts setter changes without touching any audio devices.
	/// </summary>
	[TestMethod]
	public void VoiceDefaultsAndPropertySetters()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var voice = go.Components.Create<Voice>( false );

		Assert.AreEqual( 1.0f, voice.Volume );
		Assert.AreEqual( Voice.ActivateMode.AlwaysOn, voice.Mode );
		Assert.AreEqual( "voice", voice.PushToTalkInput );
		Assert.IsTrue( voice.WorldspacePlayback );
		Assert.IsFalse( voice.Loopback );
		Assert.IsTrue( voice.LipSync );
		Assert.AreEqual( 3.0f, voice.MorphScale );
		Assert.AreEqual( 0.1f, voice.MorphSmoothTime );
		Assert.AreEqual( 15_000f, voice.Distance );
		Assert.IsFalse( voice.IsRecording );
		Assert.AreEqual( 0f, voice.Amplitude );
		Assert.AreEqual( 0f, voice.LaughterScore );
		Assert.AreEqual( 0, voice.Visemes.Count );

		voice.Mode = Voice.ActivateMode.Manual;
		voice.Distance = 999f;
		voice.VoiceMixer = new MixerHandle { Name = "voice" };

		Assert.AreEqual( Voice.ActivateMode.Manual, voice.Mode );
		Assert.AreEqual( 999f, voice.Distance );
		Assert.AreEqual( "voice", voice.VoiceMixer.Name );
	}

	/// <summary>
	/// Voice transmitter properties with non-default values survive a json
	/// serialization round trip, without the component ever being enabled.
	/// </summary>
	[TestMethod]
	public void VoiceSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var voice = go.Components.Create<Voice>( false );
		voice.Volume = 0.5f;
		voice.Mode = Voice.ActivateMode.Manual;
		voice.PushToTalkInput = "reload";
		voice.WorldspacePlayback = false;
		voice.Loopback = true;
		voice.LipSync = false;
		voice.MorphScale = 2.5f;
		voice.MorphSmoothTime = 0.4f;
		voice.Distance = 999f;
		voice.VoiceMixer = new MixerHandle { Name = "voice" };

		var copy = RoundTrip( go );
		var loaded = copy.Components.Get<Voice>( FindMode.EverythingInSelf );

		Assert.IsNotNull( loaded );
		Assert.AreEqual( 0.5f, loaded.Volume );
		Assert.AreEqual( Voice.ActivateMode.Manual, loaded.Mode );
		Assert.AreEqual( "reload", loaded.PushToTalkInput );
		Assert.IsFalse( loaded.WorldspacePlayback );
		Assert.IsTrue( loaded.Loopback );
		Assert.IsFalse( loaded.LipSync );
		Assert.AreEqual( 2.5f, loaded.MorphScale );
		Assert.AreEqual( 0.4f, loaded.MorphSmoothTime );
		Assert.AreEqual( 999f, loaded.Distance );
		Assert.AreEqual( "voice", loaded.VoiceMixer.Name );
	}

	/// <summary>
	/// LipSync morph properties round trip through json, and its component
	/// reference to a sibling sound component resolves to the deserialized
	/// sibling instance.
	/// </summary>
	[TestMethod]
	public void LipSyncSerializationRoundTripResolvesComponentReference()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var point = go.Components.Create<SoundPointComponent>( false );
		point.PlayOnStart = false;

		var lip = go.Components.Create<LipSync>( false );
		lip.Sound = point;
		lip.MorphScale = 2.25f;
		lip.MorphSmoothTime = 0.33f;

		var copy = RoundTrip( go );
		copy.Enabled = true;

		var loadedPoint = copy.Components.Get<SoundPointComponent>( FindMode.EverythingInSelf );
		var loadedLip = copy.Components.Get<LipSync>( FindMode.EverythingInSelf );

		Assert.IsNotNull( loadedPoint );
		Assert.IsNotNull( loadedLip );
		Assert.AreEqual( 2.25f, loadedLip.MorphScale );
		Assert.AreEqual( 0.33f, loadedLip.MorphSmoothTime );
		Assert.AreSame( loadedPoint, loadedLip.Sound );
	}
}
