namespace Sandbox;

/// <summary>
/// Allows collision constrained movement without the need for a rigidbody. This is not affected by forces and will only move when you call the Move() method.
/// </summary>
[Expose]
[Title( "Character Controller" )]
[Category( "Physics" )]
[Icon( "directions_walk" )]
[EditorHandle( "materials/gizmo/charactercontroller.png" )]
public class CharacterController : Component
{
	[Range( 0, 200 )]
	[Property] public float Radius { get; set; } = 16.0f;

	[Range( 0, 200 )]
	[Property] public float Height { get; set; } = 64.0f;

	[Range( 0, 50 )]
	[Property] public float StepHeight { get; set; } = 18.0f;

	[Range( 0, 90 )]
	[Property] public float GroundAngle { get; set; } = 45.0f;

	[Range( 0, 64 )]
	[Property] public float Acceleration { get; set; } = 10.0f;

	/// <summary>
	/// When jumping into walls, should we bounce off or just stop dead?
	/// </summary>
	[Range( 0, 1 )]
	[Property] public float Bounciness { get; set; } = 0.3f;

	/// <summary>
	/// If enabled, determine what to collide with using current project's collision rules for the <see cref="GameObject.Tags"/>
	/// of the containing <see cref="GameObject"/>.
	/// </summary>
	[Property, Group( "Collision" ), Title( "Use Project Collision Rules" )] public bool UseCollisionRules { get; set; } = false;

	[Property, Group( "Collision" ), HideIf( nameof( UseCollisionRules ), true )]
	public TagSet IgnoreLayers { get; set; } = new();

	public BBox BoundingBox => new BBox( new Vector3( -Radius, -Radius, 0 ), new Vector3( Radius, Radius, Height ) );

	[Sync]
	public Vector3 Velocity { get; set; }

	[Sync]
	public bool IsOnGround { get; set; }

	public GameObject GroundObject { get; set; }
	public Collider GroundCollider { get; set; }

	protected override void DrawGizmos()
	{
		Gizmo.Draw.LineBBox( BoundingBox );
	}

	/// <summary>
	/// Add acceleration to the current velocity. 
	/// No need to scale by time delta - it will be done inside.
	/// </summary>
	public void Accelerate( Vector3 vector )
	{
		Velocity = Velocity.WithAcceleration( vector, Acceleration * Time.Delta );
	}

	/// <summary>
	/// Apply an amount of friction to the current velocity.
	/// No need to scale by time delta - it will be done inside.
	/// </summary>
	public void ApplyFriction( float frictionAmount, float stopSpeed = 140.0f )
	{
		var speed = Velocity.Length;
		if ( speed < 0.01f ) return;

		// Bleed off some speed, but if we have less than the bleed
		//  threshold, bleed the threshold amount.
		float control = (speed < stopSpeed) ? stopSpeed : speed;

		// Add the amount to the drop amount.
		var drop = control * Time.Delta * frictionAmount;

		// scale the velocity
		float newspeed = speed - drop;
		if ( newspeed < 0 ) newspeed = 0;
		if ( newspeed == speed ) return;

		newspeed /= speed;
		Velocity *= newspeed;
	}

	SceneTrace BuildTrace( Vector3 from, Vector3 to ) => BuildTrace( Scene.Trace.Ray( from, to ) );

	SceneTrace BuildTrace( SceneTrace source )
	{
		var trace = source.Size( BoundingBox ).IgnoreGameObjectHierarchy( GameObject );

		return UseCollisionRules ? trace.WithCollisionRules( Tags ) : trace.WithoutTags( IgnoreLayers );
	}

	/// <summary>
	/// Trace the controller's current position to the specified delta
	/// </summary>
	public SceneTraceResult TraceDirection( Vector3 direction )
	{
		return BuildTrace( GameObject.WorldPosition, GameObject.WorldPosition + direction ).Run();
	}

	void Move( bool step )
	{
		if ( step && IsOnGround )
		{
			Velocity = Velocity.WithZ( 0 );
		}

		if ( Velocity.Length < 0.001f )
		{
			Velocity = Vector3.Zero;
			return;
		}

		var pos = GameObject.WorldPosition;

		var mover = new CharacterControllerHelper( BuildTrace( pos, pos ), pos, Velocity );
		mover.Bounce = Bounciness;
		mover.MaxStandableAngle = GroundAngle;

		if ( step && IsOnGround )
		{
			mover.TryMoveWithStep( Time.Delta, StepHeight );
		}
		else
		{
			mover.TryMove( Time.Delta );
		}

		WorldPosition = mover.Position;
		Velocity = mover.Velocity;
	}

	void CategorizePosition()
	{
		var Position = WorldPosition;
		var point = Position + Vector3.Down * 2;
		var vBumpOrigin = Position;
		var wasOnGround = IsOnGround;

		// We're flying upwards too fast, never land on ground
		if ( !IsOnGround && Velocity.z > 40.0f )
		{
			ClearGround();
			return;
		}

		//
		// trace down one step height if we're already on the ground "step down". If not, search for floor right below us
		// because if we do StepHeight we'll snap that many units to the ground
		//
		point.z -= wasOnGround ? StepHeight : 0.1f;


		var pm = BuildTrace( vBumpOrigin, point ).Run();

		//
		// we didn't hit - or the ground is too steep to be ground
		//
		if ( !pm.Hit || Vector3.GetAngle( Vector3.Up, pm.Normal ) > GroundAngle )
		{
			ClearGround();
			return;
		}

		//
		// we are on ground
		//
		IsOnGround = true;
		GroundObject = pm.GameObject;
		GroundCollider = pm.Shape?.Collider;

		//
		// move to this ground position, if we moved, and hit
		//
		if ( wasOnGround && !pm.StartedSolid && pm.Fraction > 0.0f && pm.Fraction < 1.0f )
		{
			WorldPosition = pm.EndPosition;
		}
	}

	/// <summary>
	/// Disconnect from ground and punch our velocity. This is useful if you want the player to jump or something.
	/// </summary>
	public void Punch( in Vector3 amount )
	{
		ClearGround();
		Velocity += amount;
	}

	void ClearGround()
	{
		IsOnGround = false;
		GroundObject = default;
		GroundCollider = default;
	}

	/// <summary>
	/// Move a character, with this velocity
	/// </summary>
	public void Move()
	{
		if ( TryUnstuck() )
			return;

		if ( IsOnGround )
		{
			Move( true );
		}
		else
		{
			Move( false );
		}

		CategorizePosition();
	}

	/// <summary>
	/// Move from our current position to this target position, but using tracing an sliding.
	/// This is good for different control modes like ladders and stuff.
	/// </summary>
	public void MoveTo( Vector3 targetPosition, bool useStep )
	{
		if ( TryUnstuck() )
			return;

		var pos = WorldPosition;
		var delta = targetPosition - pos;

		var mover = new CharacterControllerHelper( BuildTrace( pos, pos ), pos, delta );
		mover.MaxStandableAngle = GroundAngle;

		if ( useStep )
		{
			mover.TryMoveWithStep( 1.0f, StepHeight );
		}
		else
		{
			mover.TryMove( 1.0f );
		}

		WorldPosition = mover.Position;
	}

	int _stuckTries;

	bool TryUnstuck()
	{
		var result = BuildTrace( WorldPosition, WorldPosition ).Run();

		// Not stuck, we cool
		if ( !result.StartedSolid )
		{
			_stuckTries = 0;
			return false;
		}

		//using ( Gizmo.Scope( "unstuck", WorldTransform ) )
		//{
		//	Gizmo.Draw.Color = Gizmo.Colors.Red;
		//	Gizmo.Draw.LineBBox( BoundingBox );
		//}

		int AttemptsPerTick = 20;

		for ( int i = 0; i < AttemptsPerTick; i++ )
		{
			var pos = WorldPosition + Vector3.Random.Normal * (((float)_stuckTries) / 2.0f);

			// First try the up direction for moving platforms
			if ( i == 0 )
			{
				pos = WorldPosition + Vector3.Up * 2;
			}

			result = BuildTrace( pos, pos ).Run();

			if ( !result.StartedSolid )
			{
				//Log.Info( $"unstuck after {_stuckTries} tries ({_stuckTries * AttemptsPerTick} tests)" );
				WorldPosition = pos;
				return false;
			}
		}

		_stuckTries++;

		return true;
	}

}
