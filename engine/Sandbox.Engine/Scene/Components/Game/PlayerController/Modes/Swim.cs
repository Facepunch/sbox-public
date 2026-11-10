namespace Sandbox.Movement;

/// <summary>
/// Swimming in water volumes, including assisted exits onto nearby banks.
/// </summary>
[Icon( "scuba_diving" ), Group( "Movement" ), Title( "MoveMode - Swim" )]
public partial class MoveModeSwim : MoveMode
{
	/// <summary>
	/// Selection priority while sufficiently submerged. An active exit adds one.
	/// </summary>
	[Property]
	public int Priority { get; set; } = 10;

	/// <summary>
	/// Submerged fraction required for ordinary swimming.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float SwimLevel { get; set; } = 0.7f;

	/// <summary>
	/// Fraction of the character submerged in a water-tagged trigger.
	/// </summary>
	public float WaterLevel { get; private set; }

	/// <summary>
	/// Disable gravity and apply drag while swimming. Exits use an undamped gravity arc.
	/// </summary>
	public override void UpdateRigidBody( Rigidbody body )
	{
		body.Gravity = IsExitingWater;
		body.LinearDamping = IsExitingWater ? 0 : 3.3f;
		body.AngularDamping = 1;
	}

	/// <summary>
	/// Keep control until an exit lands, even after the character crosses the waterline.
	/// </summary>
	public override int Score( PlayerController controller )
	{
		if ( IsExitingWater )
		{
			if ( controller.IsOnGround )
			{
				CancelExit();
			}
			else
			{
				return Priority + 1;
			}
		}

		return WaterLevel > SwimLevel ? Priority : -100;
	}

	/// <summary>
	/// Mark the controller as swimming when this mode is selected.
	/// </summary>
	public override void OnModeBegin()
	{
		Controller.IsSwimming = true;
	}

	/// <summary>
	/// Preserve open-water jumping, without adding another kick after a ledge exit.
	/// </summary>
	public override void OnModeEnd( MoveMode next )
	{
		Controller.IsSwimming = false;
		if ( !Controller.IsProxy && !_suppressSurfaceJump && !IsExitingWater
			&& next is MoveModeWalk && Input.Down( "Jump" ) )
		{
			Controller.Jump( Controller.UpDirection * Controller.JumpSpeed );
		}

		CancelExit();
	}

	/// <summary>
	/// Sample water even when another movement mode is active.
	/// </summary>
	protected override void OnFixedUpdate()
	{
		UpdateWaterLevel();
		_retryTime = MathF.Max( 0, _retryTime - Time.Delta );

		// Avoid a second kick from the exit, but allow later surface jumps even
		// when a failed attempt leaves the swimmer in shallow water.
		if ( !IsExitingWater && (WaterLevel > 0.9f || _retryTime <= 0) )
		{
			_suppressSurfaceJump = false;
		}
	}

	void UpdateWaterLevel()
	{
		if ( Controller?.Body == null )
			return;

		var wt = WorldTransform;
		Vector3 head = wt.PointToWorld( new Vector3( 0, 0, Controller.CurrentHeight ) );
		Vector3 foot = wt.Position;

		float waterLevel = 0;

		foreach ( var touch in Controller.Body.Touching )
		{
			if ( !touch.Tags.Contains( "water" ) )
				continue;

			float level;
			var water = WaterBody.FromCollider( touch );
			if ( water.IsValid() )
				level = water.SubmergedFraction( foot, head );
			else
				level = Vector3.InverseLerp( touch.FindClosestPoint( head ), foot, head, true );
			level = (level * 100).CeilToInt() / 100.0f;

			if ( level > waterLevel )
				waterLevel = level;
		}

		if ( WaterLevel != waterLevel )
		{
			WaterLevel = waterLevel;
		}
	}

	/// <summary>
	/// Rotate directional swimming input before adding Jump along the character's up
	/// direction, so looking up or down cannot tilt the swim-up button.
	/// </summary>
	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		input = eyes * input;
		if ( Input.Down( "Jump" ) )
		{
			input += Controller.UpDirection;
		}

		return base.UpdateMove( Rotation.Identity, input );
	}
}
