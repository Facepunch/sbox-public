namespace Sandbox.Movement;

/// <summary>
/// The character is climbing up a ladder
/// </summary>
[Icon( "hiking" ), Group( "Movement" ), Title( "MoveMode - Ladder" )]
public partial class MoveModeLadder : MoveMode
{
	/// <summary>
	/// Movement selection priority. Defaults above swimming so ladders can be climbed out of water.
	/// </summary>
	[Property]
	public int Priority { get; set; } = 15;

	/// <summary>
	/// Multiplier for the ladder movement speed.
	/// </summary>
	[Property, Range( 0, 2 )]
	public float Speed { get; set; } = 1;

	/// <summary>
	/// A list of tags we can climb up - when they're on triggers
	/// </summary>
	[Property]
	public TagSet ClimbableTags { get; set; }

	/// <summary>
	/// The GameObject we're climbing. This will usually be a ladder trigger.
	/// </summary>
	public GameObject ClimbingObject { get; set; }

	/// <summary>
	/// When climbing, this is the rotation of the wall/ladder you're climbing, where
	/// Forward is the direction to look at the ladder, and Up is the direction to climb.
	/// </summary>
	public Rotation ClimbingRotation { get; set; }

	Collider climbingCollider;
	bool waitingToLeaveLadders;


	public MoveModeLadder()
	{
		ClimbableTags = new TagSet();
		ClimbableTags.Add( "ladder" );
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		body.Gravity = false;
		body.LinearDamping = 20.0f;
		body.AngularDamping = 1.0f;
	}

	public override int Score( PlayerController controller )
	{
		if ( ClimbingObject.IsValid() ) return Priority;
		return -100;
	}

	public override void OnModeBegin()
	{
		Controller.IsClimbing = true;
		Controller.Body.Velocity = 0;
	}

	public override void OnModeEnd( MoveMode next )
	{
		Controller.IsClimbing = false;
		Controller.Body.Velocity = Controller.Body.Velocity.ClampLength( Controller.RunSpeed );
	}

	public override void OnJump()
	{
		waitingToLeaveLadders = true;
		ClimbingObject = default;
		climbingCollider = default;
	}

	public override void PostPhysicsStep()
	{
		UpdatePositionOnLadder();
	}


	void UpdatePositionOnLadder()
	{
		if ( !ClimbingObject.IsValid() ) return;

		var pos = Controller.WorldPosition;

		// work out ideal position
		var ladderPos = climbingCollider.IsValid()
			? climbingCollider.WorldTransform.PointToWorld( climbingCollider.LocalBounds.Center )
			: ClimbingObject.WorldPosition;
		var ladderUp = ClimbingRotation.Up;

		Line ladderLine = new Line( ladderPos - ladderUp * 1000, ladderPos + ladderUp * 1000 );

		var idealPos = ladderLine.ClosestPoint( pos );

		// Get just the left/right
		var delta = (idealPos - pos);
		delta = delta.SubtractDirection( ClimbingRotation.Forward );

		if ( delta.Length > 0.01f )
		{
			Controller.Body.Velocity = Controller.Body.Velocity.AddClamped( delta * 5.0f, delta.Length * 10.0f );
		}
	}

	protected override void OnFixedUpdate()
	{
		ScanForLadders();
	}

	void ScanForLadders()
	{
		if ( Controller?.Body == null )
			return;

		if ( waitingToLeaveLadders )
		{
			if ( Controller.Body.Touching.Any( x => x.Tags.HasAny( ClimbableTags ) ) )
				return;

			waitingToLeaveLadders = false;
		}

		var wt = WorldTransform;
		Vector3 head = wt.PointToWorld( new Vector3( 0, 0, Controller.CurrentHeight ) );
		Vector3 foot = wt.Position;

		GameObject ladderObject = default;
		Collider ladderCollider = default;

		foreach ( var touch in Controller.Body.Touching )
		{
			if ( !touch.Tags.HasAny( ClimbableTags ) )
				continue;

			// already on it, no need to do any checks
			if ( ClimbingObject == touch.GameObject )
			{
				ladderObject = touch.GameObject;
				ladderCollider = touch;
				if ( climbingCollider == touch )
					break;
				continue;
			}

			if ( ladderObject.IsValid() )
				continue;

			// Don't start climbing this ladder if it's below us, and we're not already climbing it

			var ladderSurface = touch.FindClosestPoint( head );
			var level = Vector3.InverseLerp( ladderSurface, foot, head, true );


			if ( ClimbingObject != touch.GameObject && level < 0.5f )
				continue;

			ladderObject = touch.GameObject;
			ladderCollider = touch;

		}

		if ( ladderObject == ClimbingObject && ladderCollider == climbingCollider )
			return;

		ClimbingObject = ladderObject;
		climbingCollider = ladderCollider;

		if ( ClimbingObject.IsValid() )
		{
			// The thin horizontal axis of the trigger is its face normal. Its GameObject's
			// forward axis can run across the rungs, and the collider center may be offset.
			var bounds = climbingCollider.LocalBounds;
			var size = bounds.Size * climbingCollider.WorldScale;
			var rotation = climbingCollider.WorldRotation;
			var forward = MathF.Abs( size.x ) <= MathF.Abs( size.y ) ? rotation.Forward : rotation.Left;
			var center = climbingCollider.WorldTransform.PointToWorld( bounds.Center );

			if ( (center - WorldPosition).Dot( forward ) < 0 )
			{
				forward = -forward;
			}

			ClimbingRotation = Rotation.LookAt( forward, rotation.Up );
		}
	}

	/// <summary>
	/// Climb vertically from the supplied movement input, reversing when looking down.
	/// </summary>
	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		var wishVelocity = ClimbingRotation.Up * input.x;
		if ( eyes.Pitch() > 50f )
		{
			wishVelocity *= -1f;
		}

		wishVelocity *= 700.0f * Speed * (Controller.IsDucking ? 0.5f : 1f);

		if ( Input.Down( "jump" ) )
		{
			// Jump away from ladder
			Controller.Jump( ClimbingRotation.Backward * 200 );
		}

		return wishVelocity;
	}

	/// <summary>
	/// Drive the existing movement blend from actual ladder-space velocity, so blocked
	/// movement holds the idle pose even while the rendered body is still turning.
	/// </summary>
	protected override void OnUpdateAnimatorVelocity( SkinnedModelRenderer renderer )
	{
		base.OnUpdateAnimatorVelocity( renderer );

		var velocity = Controller.Velocity;
		if ( velocity.IsNearlyZero( 1f ) )
		{
			velocity = Vector3.Zero;
		}

		var forward = ClimbingRotation.Forward.Dot( velocity );
		var right = ClimbingRotation.Right.Dot( velocity );
		var up = ClimbingRotation.Up.Dot( velocity );

		renderer.Set( "move_x", forward );
		renderer.Set( "move_y", right );
		renderer.Set( "move_z", up );
		renderer.Set( "move_groundspeed", new Vector2( forward, right ).Length );
	}

	protected override void OnRotateRenderBody( SkinnedModelRenderer renderer )
	{
		// TODO: frame rate dependent

		renderer.WorldRotation = Rotation.Lerp( renderer.WorldRotation, ClimbingRotation, Time.Delta * 5.0f );
	}
}
