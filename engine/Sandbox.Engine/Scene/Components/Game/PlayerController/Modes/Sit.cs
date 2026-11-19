namespace Sandbox.Movement;

/// <summary>
/// The character is sitting
/// </summary>
[Icon( "airline_seat_recline_normal" ), Group( "Movement" ), Title( "MoveMode - Sit" )]
public sealed class SitMoveMode : MoveMode, PlayerController.IEvents
{
	/// <summary>
	/// Score this move mode highly if we're parented to a chair
	/// </summary>
	public override int Score( PlayerController controller )
	{
		if ( controller.GetComponentInParent<ISitTarget>() is ISitTarget chair )
		{
			return 10000;
		}

		return -1;
	}

	/// <summary>
	/// Update the animator while sitting in a chair
	/// </summary>
	public override void UpdateAnimator( SkinnedModelRenderer renderer )
	{
		if ( renderer.GetComponentInParent<ISitTarget>() is not ISitTarget chair )
			return;

		OnUpdateAnimatorVelocity( renderer );
		SetAnimatorParameter( renderer, "move_rotationspeed", 0f );

		chair.UpdatePlayerAnimator( Controller, renderer );
		if ( renderer.Animation.Graph is not null )
			OnUpdateAnimationGraph( renderer );
	}

	/// <summary>
	/// Entering the chair, disable body and collider
	/// </summary>
	public override void OnModeBegin()
	{
		base.OnModeBegin();

		Controller.Body.Enabled = false;
		Controller.ColliderObject.Enabled = false;
		Controller.EyeAngles = default;
	}

	/// <summary>
	/// Leaving the chair, re-enable body and collider
	/// </summary>
	public override void OnModeEnd( MoveMode next )
	{
		Controller.Body.Enabled = true;
		Controller.ColliderObject.Enabled = true;

		Controller.WorldRotation = Rotation.LookAt( Controller.EyeTransform.Forward.WithZ( 0 ), Vector3.Up );

		base.OnModeEnd( next );
	}

	/// <summary>
	/// Move is always zero while sitting
	/// </summary>
	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		return 0;
	}

	/// <summary>
	/// Get the eye transform from the chair we're sitting in
	/// </summary>
	public override Transform CalculateEyeTransform()
	{
		if ( GetComponentInParent<ISitTarget>() is not ISitTarget chair )
		{
			return base.CalculateEyeTransform();
		}

		return chair.CalculateEyeTransform( Controller );
	}

	/// <summary>
	/// Player pressed "use" but failed to press it. So we'll interpret that as wanting to leave the chair.
	/// </summary>
	void PlayerController.IEvents.FailPressing()
	{
		if ( GetComponentInParent<ISitTarget>() is not ISitTarget chair )
			return;

		chair.AskToLeave( Controller );
	}
}

/// <summary>
/// A component that can be sat in by a player. If the player is parented to an object with this component, they will be sitting in it.
/// </summary>
public interface ISitTarget
{
	/// <summary>
	/// Here you can set any animator parameters needed for sitting in this chair
	/// </summary>
	void UpdatePlayerAnimator( PlayerController controller, SkinnedModelRenderer renderer );

	/// <summary>
	/// Get the transform representing the eye position when seated. This is the first person
	/// eye position, not the third person camera position.
	/// </summary>
	Transform CalculateEyeTransform( PlayerController controller );

	/// <summary>
	/// Player wants to leave the chair
	/// </summary>
	void AskToLeave( PlayerController controller );
}
