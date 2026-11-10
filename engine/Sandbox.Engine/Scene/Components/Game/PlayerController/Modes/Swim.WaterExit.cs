namespace Sandbox.Movement;

public partial class MoveModeSwim
{
	/// <summary>
	/// Highest bank above the water surface that the character can climb out onto.
	/// </summary>
	private const float ExitMaxHeight = 32;

	/// <summary>
	/// How far ahead of the character hull to search for a bank.
	/// </summary>
	private const float ExitReach = 24;

	/// <summary>
	/// Horizontal speed maintained against the bank until the feet clear its edge.
	/// </summary>
	private const float ExitSpeed = 120;

	/// <summary>
	/// Whether the controller currently owns an assisted exit, including its dry portion.
	/// </summary>
	public bool IsExitingWater { get; private set; }

	Rigidbody _exitBody;
	GameObject _landingObject;
	Collider _landingCollider;
	Vector3 _localLanding;
	Vector3 _lastPosition;
	float _exitTime;
	float _retryTime;
	bool _launching;
	bool _suppressSurfaceJump;

	/// <summary>
	/// Landing is enabled during the exit but not during ordinary swimming.
	/// </summary>
	public override bool AllowGrounding => IsExitingWater && Controller.Body.Velocity.Dot( Controller.UpDirection ) <= 0;

	/// <summary>
	/// Preserve the exit trajectory instead of applying ordinary swim acceleration.
	/// </summary>
	public override void AddVelocity()
	{
		if ( !IsExitingWater )
		{
			base.AddVelocity();
		}
	}

	/// <summary>
	/// Detect the bank while swimming, then carry the body toward its moving landing.
	/// Collision response remains entirely in the native physics controller.
	/// </summary>
	public override void PrePhysicsStep()
	{
		if ( IsProxy || !Controller.Body.IsValid() )
			return;

		if ( !IsExitingWater )
		{
			TryBeginExit();
		}

		if ( !IsExitingWater )
		{
			return;
		}

		_exitTime += Time.Delta;
		if ( !_landingObject.IsValid() || _exitTime > 1.5f
			|| (WorldPosition - _lastPosition).Length > 128 )
		{
			CancelExit();
			return;
		}

		_lastPosition = WorldPosition;
		var landing = _landingObject.WorldTransform.PointToWorld( _localLanding );
		var delta = Controller.WithoutVertical( landing - WorldPosition );
		var wish = Controller.WithoutVertical( Controller.WishVelocity );
		if ( wish.Length > 10 && wish.Dot( delta ) < -10 )
		{
			CancelExit();
			return;
		}

		var platform = _landingCollider.IsValid() ? _landingCollider.Rigidbody : null;
		var platformVelocity = platform.IsValid() ? platform.GetVelocityAtPoint( landing ) : Vector3.Zero;
		var velocity = Controller.Body.Velocity;

		// Ease into the assistance and brake near the landing, preserving the
		// swimmer's momentum instead of replacing it in a single physics step.
		var target = delta.Normal * MathF.Min( ExitSpeed, delta.Length * 10 ) + Controller.WithoutVertical( platformVelocity );
		var horizontal = Controller.WithoutVertical( velocity );
		horizontal += (target - horizontal).ClampLength( 600 * Time.Delta );

		var vertical = velocity.Dot( Controller.UpDirection );
		if ( _launching )
		{
			// Recalculate the impulse still needed as we rise. A fixed launch
			// target would add the ramp's travel on top of the validated apex.
			var gravity = MathF.Max( 1, -Scene.PhysicsWorld.Gravity.Dot( Controller.UpDirection ) * Controller.Body.GravityScale );
			var rise = MathF.Max( 0, (landing - WorldPosition).Dot( Controller.UpDirection ) + 18 );
			var launchSpeed = MathF.Sqrt( 2 * gravity * rise );
			if ( vertical < launchSpeed )
			{
				vertical = MathF.Min( launchSpeed, vertical + 2400 * Time.Delta );
			}

			_launching = vertical < launchSpeed;
		}

		Controller.Body.Velocity = Controller.WithVertical( horizontal, vertical );
	}

	/// <summary>
	/// Stop assistance when an obstruction prevents the feet from reaching the top.
	/// </summary>
	public override void PostPhysicsStep()
	{
		if ( !IsExitingWater || !_landingObject.IsValid() )
			return;

		var landing = _landingObject.WorldTransform.PointToWorld( _localLanding );
		if ( _exitTime > 0.1f && Controller.Body.Velocity.Dot( Controller.UpDirection ) <= 0 && (WorldPosition - landing).Dot( Controller.UpDirection ) < -2 )
		{
			CancelExit();
		}
	}

	/// <summary>
	/// Use the same slope limit as the character's walking mode for exit landings.
	/// </summary>
	public override bool IsStandableSurface( in SceneTraceResult result )
	{
		var walk = GetComponent<MoveModeWalk>();
		return result.Normal.Dot( Controller.UpDirection ) >= MathF.Cos( (walk?.GroundAngle ?? 45) * MathF.PI / 180 );
	}

	/// <summary>
	/// Cancel any pending exit after a reset, landing, interruption or obstruction.
	/// </summary>
	public void CancelExit()
	{
		if ( IsExitingWater )
		{
			_retryTime = 0.5f;
			_suppressSurfaceJump = true;
		}

		if ( _exitBody.IsValid() )
		{
			_exitBody.SuppressBuoyancy = false;
		}

		_exitBody = null;
		IsExitingWater = false;
		_launching = false;
		_landingObject = null;
		_landingCollider = null;
	}

	/// <summary>
	/// Release transient references if the component is disabled during an exit.
	/// </summary>
	protected override void OnDisabled()
	{
		CancelExit();
		base.OnDisabled();
	}

	void TryBeginExit()
	{
		if ( _retryTime > 0 || WaterLevel < 0.35f || WaterLevel > 0.9f || Controller.Body.Velocity.Dot( Controller.UpDirection ) < -180 )
			return;

		var wish = Controller.WithoutVertical( Controller.WishVelocity );
		if ( wish.Length < 10 )
			return;

		var direction = wish.Normal;
		var foot = WorldPosition;
		var scale = MathF.Abs( WorldTransform.UniformScale );
		var height = Controller.CurrentHeight * scale;
		var radius = Controller.BodyRadius * MathF.Sqrt( 0.5f ) * scale;
		var wall = Trace( foot, foot + direction * ExitReach );
		if ( !wall.Hit || wall.StartedSolid || IsStandableSurface( wall ) )
			return;

		var waterHeight = foot.Dot( Controller.UpDirection ) + WaterLevel * height;
		var high = Controller.WithVertical( foot, waterHeight + ExitMaxHeight + 2 );
		var across = high + direction * (wall.Distance + radius * 2 + 6);
		var upTrace = Trace( foot, high );
		if ( upTrace.Hit || upTrace.StartedSolid )
			return;

		var acrossTrace = Trace( high, across );
		if ( acrossTrace.Hit || acrossTrace.StartedSolid )
			return;

		var down = Trace( across, Controller.WithVertical( across, waterHeight - 8 ) );
		if ( !down.Hit || down.StartedSolid || !IsStandableSurface( down )
			|| down.EndPosition.Dot( Controller.UpDirection ) > waterHeight + ExitMaxHeight || down.EndPosition.Dot( Controller.UpDirection ) < waterHeight - 4 )
			return;

		// The arc rises above the landing. Validate that extra headroom as well.
		var apex = Controller.WithVertical( foot, down.EndPosition.Dot( Controller.UpDirection ) + 18 );
		var apexAcross = Controller.WithVertical( across, apex.Dot( Controller.UpDirection ) );
		var riseTrace = Trace( foot, apex );
		var arcTrace = Trace( apex, apexAcross );
		if ( riseTrace.Hit || riseTrace.StartedSolid || arcTrace.Hit || arcTrace.StartedSolid )
			return;

		// Check the whole route, not just a ray over the bank or a clear eye position.
		_landingObject = down.GameObject;
		_landingCollider = down.Component as Collider;
		if ( !_landingObject.IsValid() )
			return;

		_localLanding = _landingObject.WorldTransform.PointToLocal( down.EndPosition );
		_lastPosition = foot;
		_exitTime = 0;
		IsExitingWater = true;
		_launching = true;
		_exitBody = Controller.Body;
		_exitBody.SuppressBuoyancy = true;
		_suppressSurfaceJump = true;
		Controller.PreventGrounding( 0.15f );

		UpdateRigidBody( Controller.Body );
	}

	/// <summary>
	/// Sweep both native character shapes. A small skin tolerates the solver's
	/// resting contact penetration without accepting genuinely obstructed routes.
	/// </summary>
	SceneTraceResult Trace( Vector3 from, Vector3 to )
	{
		return Controller.TraceMovement( from, to );
	}
}
