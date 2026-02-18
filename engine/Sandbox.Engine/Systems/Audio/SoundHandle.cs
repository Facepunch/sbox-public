using Sandbox.Audio;
using System.Collections.Concurrent;
using System.IO;

namespace Sandbox;

/// <summary>
/// A handle to a sound that is currently playing. You can use this to control the sound's position, volume, pitch etc.
/// </summary>
[Expose]
public partial class SoundHandle : IValid, IDisposable
{
	static ConcurrentQueue<SoundHandle> removalQueue = new();
	static ConcurrentQueue<SoundHandle> addQueue = new();
	static HashSet<SoundHandle> active = new();

	CSfxTable _sfx;

	internal AudioSampler sampler;

	int _ticks;
	Transform _transform = Transform.Zero;


	static SoundHandle _empty;

	/// <summary>
	/// RealTime that this sound was created
	/// </summary>
	internal float _CreatedTime;

	/// <summary>
	/// An empty, do nothing sound, that we can return to avoid NREs
	/// </summary>
	internal static SoundHandle Empty
	{
		get
		{
			if ( _empty is null )
			{
				_empty = new SoundHandle();
			}

			return _empty;
		}
	}


	/// <summary>
	/// Position of the sound.
	/// </summary>
	public Vector3 Position
	{
		get => _transform.Position;
		set => _transform.Position = value;
	}

	/// <summary>
	/// The direction the sound is facing
	/// </summary>
	public Rotation Rotation
	{
		get => _transform.Rotation;
		set => _transform.Rotation = value;
	}

	/// <summary>
	/// This sound's transform
	/// </summary>
	public Transform Transform
	{
		get => _transform;
		set => _transform = value;
	}

	/// <summary>
	/// Volume of the sound.
	/// </summary>
	public float Volume { get; set; } = 1.0f;

	/// <summary>
	/// A debug name to help identify the sound
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// How 3d the sound should be. 0 means no 3d, 1 means fully
	/// </summary>
	[Range( 0, 1 )]
	public float SpacialBlend { get; set; } = 1.0f;

	/// <summary>
	/// How many units the sound can be heard from.
	/// </summary>
	public float Distance { get; set; } = 15_000f;

	/// <summary>
	/// The falloff curve for the sound.
	/// </summary>
	public Curve Falloff { get; set; } = new Curve( new( 0, 1, 0, -1.8f ), new( 0.05f, 0.22f, 3.5f, -3.5f ), new( 0.2f, 0.04f, 0.16f, -0.16f ), new( 1, 0 ) );

	/// <summary>
	/// The fadeout curve for when the sound stops.
	/// </summary>
	public Curve Fadeout { get; set; } = new Curve( new( 0, 1 ), new( 1, 0 ) );

	/// <summary>
	/// The fadein curve for when the sound starts.
	/// </summary>
	public Curve Fadein { get; set; } = new Curve( new( 0, 0 ), new( 1, 1 ) );


	[Obsolete( "This is not used anymore" )]
	public float Decibels { get; set; } = 70.0f;

	/// <summary>
	/// Pitch of the sound.
	/// </summary>
	public float Pitch { get; set; } = 1.0f;

	/// <summary>
	/// Whether the sound is currently playing or not.
	/// </summary>
	public bool IsPlaying => !IsStopped;

	/// <summary>
	/// Whether the sound is currently paused or not.
	/// </summary>
	public bool Paused { get; set; } = false;

	/// <summary>
	/// Sound is done
	/// </summary>
	public bool Finished { get; set; }

	/// <summary>
	/// Enable the sound reflecting off surfaces
	/// </summary>
	[System.Obsolete]
	public bool Reflections { get; set; }

	/// <summary>
	/// Allow this sound to be occluded by geometry etc
	/// </summary>
	public bool Occlusion { get; set; } = true;

	/// <summary>
	/// The radius of this sound's occlusion, allow for partial occlusion
	/// </summary>
	public float OcclusionRadius { get; set; } = 32.0f;

	/// <summary>
	/// Should the sound fade out over distance
	/// </summary>
	public bool DistanceAttenuation { get; set; } = true;

	/// <summary>
	/// Should the sound get absorbed by air, so it sounds different at distance
	/// </summary>
	public bool AirAbsorption { get; set; } = true;

	/// <summary>
	/// Should the sound transmit through walls, doors etc
	/// </summary>
	public bool Transmission { get; set; } = true;

	/// <summary>
	/// Which mixer do we want to write to
	/// </summary>
	public Mixer TargetMixer { get; set; }

	/// <summary>
	/// Marks this sound as voice/speech audio (e.g., from VoiceComponent).
	/// Voice sounds use cheaper HRTF interpolation since they don't benefit from bilinear filtering.
	/// </summary>
	internal bool IsVoice { get; set; }

	/// <summary>
	/// How many samples per second?
	/// </summary>
	public int SampleRate { get; private init; }

	/// <summary>
	/// Keep playing silently for a second or two, to finish reverb effect
	/// </summary>
	internal RealTimeUntil TimeUntilFinished { get; set; }

	/// <summary>
	/// Keep playing until faded out
	/// </summary>
	internal RealTimeUntil TimeUntilFaded { get; set; }

	/// <summary>
	/// Time remaining until the fade-in completes
	/// </summary>
	internal RealTimeUntil TimeUntilFadedIn { get; set; }

	/// <summary>
	/// Have we started fading out?
	/// </summary>
	internal bool IsFadingOut { get; set; }

	/// <summary>
	/// Are we currently fading in?
	/// </summary>
	internal bool IsFadingIn { get; set; }

	/// <summary>
	/// True if the sound has been stopped
	/// </summary>
	public bool IsStopped
	{
		get
		{
			if ( !IsValid ) return true;
			return false;
		}
	}

	[Obsolete( "Use Time instead" )]
	public float ElapsedTime => Time;

	/// <summary>
	/// The current time of the playing sound in seconds.
	/// Note: for some formats seeking may be expensive, and some may not support it at all.
	/// </summary>
	public float Time
	{
		get
		{
			if ( IsStopped ) return 0.0f;
			if ( sampler is null ) return 0.0f;

			return SampleRate > 0 ? sampler.SamplePosition / (float)SampleRate : 0.0f;
		}
		set
		{
			if ( sampler is null ) return;

			sampler.SamplePosition = (int)(value * SampleRate);
		}
	}

	public void Stop( float fadeTime = 0.0f )
	{
		if ( Finished || IsFadingOut ) return;

		if ( fadeTime > 0.0f )
		{
			TimeUntilFaded = fadeTime;
			IsFadingOut = true;

			return;
		}

		Finished = true;
	}

	/// <summary>
	/// Place the listener at 0,0,0 facing 1,0,0.
	/// </summary>
	public bool ListenLocal { get; set; }

	/// <summary>
	/// If true, then this sound won't be played unless voice_loopback is 1. The assumption is that it's the 
	/// local user's voice. Amplitude and visme data will still be available!
	/// </summary>
	public bool Loopback { get; set; }

	/// <summary>
	/// Measure of audio loudness.
	/// </summary>
	public float Amplitude { get; set; }

	bool _destroyed = false;

	internal readonly Scene Scene;

	internal SoundHandle( CSfxTable soundHandle )
	{
		_sfx = soundHandle;
		Scene = Game.ActiveScene;
		SampleRate = _sfx.GetSound().m_rate();
		TryCreateMixer();
		addQueue.Enqueue( this );
		_CreatedTime = RealTime.Now;
	}

	// an empty soundhandle
	internal SoundHandle()
	{
		SampleRate = 48000;
		_destroyed = true;
		_CreatedTime = RealTime.Now;
	}

	~SoundHandle()
	{
		Dispose();
	}

	/// <summary>
	/// Return true if this has no mixer specified, so will use the default mixer
	/// </summary>
	/// <returns></returns>
	internal bool WantsDefaultMixer() => TargetMixer is null;

	/// <summary>
	/// Return true if we want to play on this mixer. Will return true if we have no
	/// mixer specified, and the provided mixer is the default.
	/// </summary>
	internal bool IsTargettingMixer( Mixer mixer )
	{
		if ( _destroyed ) return false;
		if ( WantsDefaultMixer() && Mixer.Default == mixer ) return true;
		if ( TargetMixer is null ) return false;
		if ( string.IsNullOrEmpty( mixer.Name ) ) return false;

		// Compare names instead of mixers, because they may have deserialized etc
		return TargetMixer.Name == mixer.Name;
	}

	/// <summary>
	/// Gets the effective mixer this sound will play on.
	/// Returns the TargetMixer if set, otherwise the default mixer.
	/// </summary>
	internal Mixer GetEffectiveMixer()
	{
		if ( _destroyed ) return null;
		return TargetMixer ?? Mixer.Default;
	}

	/// <summary>
	/// Returns true if this sound is ready to be mixed (has sampler, not finished, valid).
	/// Used by both mixer and occlusion system to determine if sound should be processed.
	/// </summary>
	internal bool CanBeMixed()
	{
		if ( !IsValid ) return false;
		if ( sampler is null ) return false;
		if ( Finished ) return false;
		return true;
	}

	public bool IsValid => !_destroyed;

	void TryCreateMixer()
	{
		if ( sampler is not null )
			return;

		var ptr = _sfx.CreateMixer();
		if ( ptr.IsNull )
		{
			// Did we fail because the resource failed to load? Just mark complete or get stuck in hell (feedback is provide on load failure)
			if ( _sfx.FailedResourceLoad() )
			{
				Finished = true;
			}

			return;
		}

		sampler = new AudioSampler( ptr );
	}

	public void Dispose()
	{
		lock ( this )
		{
			if ( _destroyed )
				return;

			GC.SuppressFinalize( this );
			_destroyed = true;

			_sfx = default;

			DisposeSources();

			MainThread.QueueDispose( sampler );
			sampler = null;

			removalQueue.Enqueue( this );

			if ( LipSync.Enabled )
				LipSync.DisableLipSync();
		}
	}

	/// <summary>
	/// This is called on the main thread for all active voices
	/// </summary>
	void TickInternal()
	{
		if ( _destroyed )
			return;

		if ( Finished )
		{
			Dispose();
			return;
		}

		UpdateFollower();
		TryCreateMixer();
		UpdateSources();

		_ticks++;
	}

	/// <summary>
	/// Called to push changes to a sound immediately, rather than waiting for the next tick.
	/// You should call this if you make changes to a sound.
	/// </summary>
	[System.Obsolete( "This no longer needs to exist" )]
	public void Update()
	{

	}

	/// <summary>
	/// Before we're added to the active list, we need to get some stuff straight
	/// </summary>
	void OnActive()
	{

	}

	static void TickQueues()
	{
		while ( addQueue.TryDequeue( out var h ) )
		{
			h.OnActive();
			active.Add( h );
		}

		while ( removalQueue.TryDequeue( out var h ) )
		{
			active.Remove( h );
		}
	}

	static void TickVoices()
	{
		foreach ( var handle in active )
		{
			if ( !handle.IsValid ) continue;

			handle.TickInternal();
		}
	}

	internal static void TickAll()
	{
		lock ( active )
		{
			TickQueues();
			TickVoices();
		}
	}

	internal static void StopAll( float fade, Mixer mixer = null )
	{
		lock ( active )
		{
			TickQueues();

			var handles = mixer is null ? active : active.Where( x => x.TargetMixer == mixer );
			foreach ( var handle in handles )
			{
				if ( !handle.IsValid() ) continue;
				handle.Stop( fade );
			}
		}
	}

	internal static void Shutdown()
	{
		lock ( active )
		{
			foreach ( var handle in active )
			{
				if ( !handle.IsValid() ) continue;
				handle.Dispose();
			}
		}
	}

	internal static void StopAllWithParent( GameObject parent, float fade )
	{
		lock ( active )
		{
			TickQueues();

			foreach ( var handle in active.Where( x => x.Parent == parent ) )
			{
				if ( !handle.IsValid() ) continue;
				handle.Stop( fade );
			}
		}
	}

	internal static void StopAll( CSfxTable sfx )
	{
		lock ( active )
		{
			foreach ( var handle in active.Where( x => x._sfx == sfx ) )
			{
				if ( !handle.IsValid() ) continue;
				handle.Stop();
			}
		}
	}

	internal static void FlushCreatedSounds()
	{
		lock ( active )
		{
			TickQueues();
		}
	}

	public static void GetActive( List<SoundHandle> handles )
	{
		lock ( active )
		{
			foreach ( var handle in active )
			{
				if ( !handle.IsValid() ) continue;
				if ( handle._ticks == 0 ) continue;
				if ( handle.Paused ) continue;

				handles.Add( handle );
			}
		}
	}

	[ConCmd( "list_sound_handles", Help = "Prints a summary of active sound handles to the console. Use \"list_sound_handles 2\" to see more info." )]
	private static void LogActiveHandles( int level = 1 )
	{
		var handles = new List<SoundHandle>();
		GetActive( handles );

		using var writer = new StringWriter();
		var mixerGroups = handles
			.GroupBy( x => x.TargetMixer )
			.OrderBy( x => x.Key?.Name );

		writer.WriteLine( $"Total active sound handles: {handles.Count}" );
		writer.WriteLine();

		foreach ( var mixerGroup in mixerGroups )
		{
			writer.WriteLine( $"Mixer \"{mixerGroup.Key?.Name ?? "Default"}\": {mixerGroup.Count()}" );

			var soundGroups = mixerGroup.GroupBy( x => x.Name );

			foreach ( var soundGroup in soundGroups )
			{
				writer.WriteLine( $"  Sound \"{soundGroup.Key}\": {soundGroup.Count()}" );

				if ( level < 2 ) continue;

				foreach ( var soundHandle in soundGroup )
				{
					writer.WriteLine( $"    Handle {soundHandle._sfx.self:x}:" );

					writer.WriteLine( $"      {nameof( IsPlaying )}: {soundHandle.IsPlaying}" );
					writer.WriteLine( $"      {nameof( Time )}: {soundHandle.Time}" );
					writer.WriteLine( $"      {nameof( Volume )}: {soundHandle.Volume}" );
					writer.WriteLine( $"      {nameof( Distance )}: {soundHandle.Distance}" );
					writer.WriteLine( $"      {nameof( ListenLocal )}: {soundHandle.ListenLocal}" );
					writer.WriteLine( $"      {nameof( Position )}: {soundHandle.Position}" );
				}
			}

			writer.WriteLine();
		}

		Log.Info( writer.ToString() );
	}
}
