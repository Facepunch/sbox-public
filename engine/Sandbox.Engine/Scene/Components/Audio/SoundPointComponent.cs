namespace Sandbox;

/// <summary>
/// Plays a sound at a point in the world.
/// </summary>
[Expose]
[Category( "Audio" )]
[Title( "Sound Point" )]
[Icon( "volume_up" )]
[EditorHandle( Icon = "volume_up" )]
[Tint( EditorTint.Green )]
public sealed class SoundPointComponent : BaseSoundComponent, Component.ITemporaryEffect
{
	protected override void OnEnabled()
	{
		StopSound();

		if ( PlayOnStart )
		{
			StartSound();
		}
	}

	TimeUntil TimeUntilRepeat;

	public override void StartSound()
	{
		var source = GameObject;

		if ( StopOnNew )
		{
			SoundHandle?.Stop( 0.1f );
			SoundHandle = default;
		}

		if ( SoundHandle?.IsPlaying ?? false )
			return;

		if ( SoundEvent is null )
			return;

		SoundHandle = Sound.Play( SoundEvent, source.WorldPosition );
		ApplyOverrides( SoundHandle );

		TimeUntilRepeat = Random.Shared.Float( MinRepeatTime, MaxRepeatTime );
	}

	public override void StopSound()
	{
		SoundHandle?.Stop( 0.1f );
		SoundHandle = default;
		TimeUntilRepeat = 0;
	}

	protected override void OnUpdate()
	{
		if ( SoundHandle.IsValid() )
		{
			SoundHandle.Position = WorldPosition;
			ApplyOverrides( SoundHandle );
		}

		if ( Repeat && TimeUntilRepeat <= 0.0f )
		{
			StartSound();
		}
	}

	protected override void OnDisabled()
	{
		StopSound();
	}

	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( !Gizmo.IsSelected ) return;
		if ( !DistanceAttenuationOverride && !SoundEvent.IsValid() ) return;
		if ( !(DistanceAttenuationOverride ? DistanceAttenuation : SoundEvent.DistanceAttenuation) ) return;

		var distance = DistanceAttenuationOverride ? Distance : SoundEvent.Distance;

		using ( Gizmo.Scope( "sound_component" ) )
		{
			Gizmo.Draw.Color = Gizmo.Colors.Green;
			Gizmo.Transform = Gizmo.Transform.WithRotation( Rotation.Identity );
			Gizmo.Draw.SolidRing( 0, distance, distance + 1f, 0, 360, 24 );
			Gizmo.Transform = Gizmo.Transform.WithRotation( Gizmo.Transform.Rotation * new Angles( 90, 0, 0 ) );
			Gizmo.Draw.SolidRing( 0, distance, distance + 1f, 0, 360, 24 );
			Gizmo.Transform = Gizmo.Transform.WithRotation( Gizmo.Transform.Rotation * new Angles( 0, 90, 0 ) );
			Gizmo.Draw.SolidRing( 0, distance, distance + 1f, 0, 360, 24 );
		}
	}

	/// <summary>
	/// Return true if the sound is playing
	/// </summary>
	bool Component.ITemporaryEffect.IsActive
	{
		get
		{
			if ( !SoundHandle.IsValid() ) return false;
			return SoundHandle.IsPlaying;
		}
	}
}
