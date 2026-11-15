namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// Draw debug overlay on footsteps
	/// </summary>
	public bool DebugFootsteps;

	TimeSince _timeSinceStep;

	private void OnFootstepEvent( SceneModel.FootstepEvent e )
	{
		if ( !IsOnGround ) return;
		if ( !EnableFootstepSounds ) return;
		if ( _timeSinceStep < 0.2f ) return;

		_timeSinceStep = 0;

		float volume = e.Volume * WishVelocity.Length.Remap( 0, 400, 0, 1 );
		if ( volume <= 0.1f ) return;

		PlayFootstepSound( e.Transform.Position, volume, e.FootId );
	}

	/// <summary>
	/// Play a footstep sound at the given world position. Will only play if the player has a GroundSurface.
	/// </summary>
	public void PlayFootstepSound( Vector3 worldPosition, float volume, int foot )
	{
		if ( !GroundSurface.IsValid() ) return;

		var soundEvent = foot == 0 ? GroundSurface.SoundCollection.FootLeft : GroundSurface.SoundCollection.FootRight;
		if ( soundEvent is null )
		{
			if ( DebugFootsteps )
			{
				DebugOverlay.Sphere( new Sphere( worldPosition, volume ), duration: 10, color: Color.Orange, overlay: true );
			}

			return;
		}

		var handle = GameObject.PlaySound( soundEvent, 0 );
		if ( !handle.IsValid() ) return;

		handle.FollowParent = IsFirstPersonViewer;
		if ( IsFirstPersonViewer )
		{
			handle.SpacialBlend = 0.0f;
		}
		handle.TargetMixer = FootstepMixer.GetOrDefault();
		handle.Volume *= volume * FootstepVolume;

		if ( DebugFootsteps )
		{
			DebugOverlay.Sphere( new Sphere( worldPosition, volume ), duration: 10, overlay: true );
			DebugOverlay.Text( worldPosition, $"{soundEvent.ResourceName}", size: 14, flags: TextFlag.LeftTop, duration: 10, overlay: true );
		}
	}
}
