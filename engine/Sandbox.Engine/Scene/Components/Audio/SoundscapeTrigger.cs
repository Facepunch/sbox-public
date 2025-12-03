using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Plays a soundscape when the listener enters the trigger area.
/// </summary>
[Expose]
[Title( "Soundscape Trigger" )]
[Category( "Rendering" )]
[Icon( "surround_sound" )]
[EditorHandle( "materials/gizmo/soundscape.png" )]
[Tint( EditorTint.Green )]
public class SoundscapeTrigger : Component
{
	public enum TriggerType
	{
		/// <summary>
		/// Can be heard from anywhere.
		/// </summary>
		[Icon( "zoom_out_map" )]
		Point,
		/// <summary>
		/// Can be heard within a radius.
		/// </summary>
		[Icon( "radio_button_unchecked" )]
		Sphere,
		/// <summary>
		/// Can be heard within the bounds of a box.
		/// </summary>
		[Icon( "check_box_outline_blank" )]
		Box
	}

	/// <summary>
	/// Determines when/where the soundscape can be heard.
	/// </summary>
	[Property] public TriggerType Type { get; set; }

	[Property] public Soundscape Soundscape { get; set; }

	MixerHandle _targetMixer;

	/// <summary>
	/// The mixer that the soundscape will play on.
	/// </summary>
	[Property]
	public MixerHandle TargetMixer
	{
		get => _targetMixer;
		set
		{
			if ( value == _targetMixer ) return;
			foreach ( var entry in activeEntries )
			{
				entry.TargetMixer = value;
			}
			_targetMixer = value;
		}
	}

	/// <summary>
	/// When true the soundscape will keep playing after exiting the area, and will
	/// only stop playing once another soundscape takes over.
	/// </summary>
	[Property] public bool StayActiveOnExit { get; set; } = true;

	float _volume = 1.0f;
	[Property]
	public float Volume
	{
		get => _volume;
		set
		{
			if ( value == _volume ) return;
			foreach ( var entry in activeEntries )
			{
				entry.Volume = value;
			}
			_volume = value;
		}
	}

	/// <summary>
	/// The radius of the Soundscape when <see cref="Type"/> is set to <see cref="TriggerType.Sphere"/>.
	/// </summary>
	[Property]
	[ShowIf( nameof( Type ), TriggerType.Sphere )]
	public float Radius { get; set; } = 500.0f;

	Vector3 _scale = 50;

	/// <summary>
	/// The size of the Soundscape when <see cref="Type"/> is set to <see cref="TriggerType.Box"/>.
	/// </summary>
	[Property]
	[ShowIf( nameof( Type ), TriggerType.Box )]
	public Vector3 BoxSize
	{
		get => _scale;
		set
		{
			if ( _scale == value ) return;

			_scale = value;
		}
	}

	protected override void DrawGizmos()
	{
		if ( Type == TriggerType.Point )
		{
			// nothing
		}
		else if ( Type == TriggerType.Sphere )
		{
			if ( Gizmo.IsSelected )
			{
				Gizmo.Draw.Color = Playing ? Gizmo.Colors.Active : Gizmo.Colors.Blue;
				Gizmo.Draw.LineSphere( 0, Radius );
			}
		}
		else if ( Type == TriggerType.Box )
		{
			if ( Gizmo.IsSelected )
			{
				Gizmo.Draw.Color = Playing ? Gizmo.Colors.Active : Gizmo.Colors.Blue;
				Gizmo.Draw.LineBBox( new BBox( -BoxSize, BoxSize ) );
			}
		}

	}


	public bool Playing { get; internal set; }

	bool wasPlaying;
	readonly List<PlayingSound> activeEntries = new();
	readonly List<PlayingSound> removalList = new();

	protected override void OnUpdate()
	{
		if ( Playing && !wasPlaying && Soundscape is not null )
		{
			StartSoundscape( Soundscape );
		}

		wasPlaying = Playing;

		if ( activeEntries.Count == 0 && removalList.Count == 0 )
			return;

		UpdateEntries( Sound.Listener );
	}

	protected override void OnDisabled()
	{
		Stop();
	}

	protected override void OnDestroy()
	{
		Stop();
	}

	private void Stop()
	{
		foreach ( var entry in activeEntries )
		{
			entry.Dispose();
		}

		activeEntries.Clear();
		removalList.Clear();

		Playing = false;
		wasPlaying = false;
	}

	void UpdateEntries( Transform head )
	{
		foreach ( var e in activeEntries )
		{
			e.Frame( head );

			if ( !Playing )
				e.Finished = true;

			if ( e.IsDead )
				removalList.Add( e );
		}

		foreach ( var e in removalList )
		{
			e.Dispose();
			activeEntries.Remove( e );
		}
	}

	/// <summary>
	/// Return true if they should hear this soundscape when in this position
	/// </summary>
	public bool TestListenerPosition( Vector3 position )
	{
		if ( Type == TriggerType.Sphere )
		{
			return (position - WorldPosition).LengthSquared < (Radius * Radius);
		}
		else if ( Type == TriggerType.Box )
		{
			return new BBox( -BoxSize, BoxSize ).Contains( WorldTransform.PointToLocal( position ) );
		}

		return true;
	}

	/// <summary>
	/// Load and start this soundscape..
	/// </summary>
	void StartSoundscape( Soundscape scape )
	{
		foreach ( var e in activeEntries )
		{
			e.Finished = true;
		}

		foreach ( var loop in scape.LoopedSounds )
			StartLoopedSound( loop, scape.MasterVolume.GetValue(), Volume );

		foreach ( var loop in scape.StingSounds )
			StartStingSound( loop, scape.MasterVolume.GetValue(), Volume );

	}

	void StartLoopedSound( Soundscape.LoopedSound sound, float internalVolume, float volume )
	{
		if ( sound?.SoundFile == null )
			return;

		foreach ( var entry in activeEntries.OfType<LoopedSoundEntry>() )
		{
			if ( entry.TryUpdateFrom( sound, internalVolume, volume ) )
				return;
		}

		var e = new LoopedSoundEntry( sound, internalVolume, volume );
		e.TargetMixer = TargetMixer;
		activeEntries.Add( e );
	}

	void StartStingSound( Soundscape.StingSound sound, float internalVolume, float volume )
	{
		if ( sound.SoundFile == null )
			return;

		for ( int i = 0; i < sound.InstanceCount; i++ )
		{
			var e = new StingSoundEntry( sound, internalVolume, volume );
			e.TargetMixer = TargetMixer;
			activeEntries.Add( e );
		}
	}

	class PlayingSound : System.IDisposable
	{
		public MixerHandle TargetMixer;
		public float Volume = 1.0f;

		protected SoundHandle handle;
		protected float internalVolume = 1.0f;

		/// <summary>
		/// True if this sound has finished, can be removed
		/// </summary>
		internal virtual bool IsDead => !handle.IsValid() || (!handle.IsPlaying && Finished);

		/// <summary>
		/// Gets set when it's time to fade this out
		/// </summary>
		public bool Finished { get; set; }

		public virtual void Frame( in Transform head ) { }

		public virtual void Dispose()
		{
			handle?.Stop( 0.1f );
			handle = default;
		}
	}

	sealed class LoopedSoundEntry : PlayingSound
	{
		/// <summary>
		/// We store the current volume so we can seamlessly fade in and out
		/// </summary>
		public float currentVolume = 0.0f;

		/// <summary>
		/// Consider us dead if the soundscape system thinks we're finished and our volume is low
		/// </summary>
		internal override bool IsDead => currentVolume <= 0.001f && Finished;

		Soundscape.LoopedSound source;
		float sourceVolume;
		float soundVelocity = 0.0f;

		public LoopedSoundEntry( Soundscape.LoopedSound sound, float internalVolume, float volume )
		{
			currentVolume = 0.0f;
			this.internalVolume = internalVolume;
			Volume = volume;

			handle = Sound.PlayFile( sound.SoundFile );

			UpdateFrom( sound );
		}

		public override void Frame( in Transform head )
		{
			if ( source?.SoundFile?.IsValid() == false )
			{
				Finished = true;
				return;
			}

			var targetVolume = sourceVolume * internalVolume * Volume;
			if ( Finished ) targetVolume = 0.0f;

			currentVolume = MathX.SmoothDamp( currentVolume, targetVolume, ref soundVelocity, 5.0f, Time.Delta );
			handle.Volume = currentVolume;
			handle.Position = head.Position;
			handle.TargetMixer = TargetMixer.GetOrDefault();
		}

		public override string ToString() => $"Looped - Finished:{Finished} volume:{currentVolume:n0.00} - {source}";

		/// <summary>
		/// If we're using the same sound file as this incoming sound, and we're on our way out.. then
		/// let it replace us instead. This is much nicer.
		/// </summary>
		public bool TryUpdateFrom( Soundscape.LoopedSound sound, float internalVolume, float volume )
		{
			if ( !Finished ) return false;
			if ( sound.SoundFile != source.SoundFile ) return false;

			this.internalVolume = internalVolume;
			Volume = volume;
			UpdateFrom( sound );
			return true;
		}

		void UpdateFrom( Soundscape.LoopedSound sound )
		{
			source = sound;
			sourceVolume = sound.Volume.GetValue();
			Finished = false;
		}
	}

	sealed class StingSoundEntry : PlayingSound
	{
		readonly Soundscape.StingSound source;
		TimeUntil timeUntilNextShot;

		internal override bool IsDead => Finished;

		public StingSoundEntry( Soundscape.StingSound sound, float internalVolume, float volume )
		{
			source = sound;
			timeUntilNextShot = sound.RepeatTime.GetValue();
			this.internalVolume = internalVolume;
			Volume = volume;
		}

		public override void Frame( in Transform head )
		{
			if ( Finished )
				return;

			if ( timeUntilNextShot > 0 )
				return;

			if ( source?.SoundFile?.IsValid() == false )
			{
				Finished = true;
				return;
			}

			timeUntilNextShot = source.RepeatTime.GetValue();

			handle?.Stop( 0.1f );
			handle = Sound.Play( source.SoundFile.ResourcePath );
			handle.TargetMixer = TargetMixer.GetOrDefault();

			// we'll make this shape more configurable, but right now bias x/y rather than up and down
			var randomOffset = new Vector3( Game.Random.Float( -10, 10 ), Game.Random.Float( -10, 10 ), Game.Random.Float( -1, 1 ) );
			randomOffset = randomOffset.Normal * source.Distance.GetValue();

			handle.Position = head.Position + randomOffset;

			handle.Volume *= internalVolume * Volume;
		}

	}
}

