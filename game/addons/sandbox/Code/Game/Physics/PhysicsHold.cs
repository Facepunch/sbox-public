using Sandbox.Physics;

/// <summary>
/// Moves a rigidbody towards a target transform through a control joint, the way the physgun holds things.
/// Only the instance that simulates the body can move it, so run this there
/// </summary>
public sealed class PhysicsHold
{
	const float MaxLinearAcceleration = 700000.0f;

	Sandbox.Physics.ControlJoint _joint;
	PhysicsBody _body;
	Vector3 _massCenterOffset;

	/// <summary>
	/// True while there's a joint holding a body.
	/// </summary>
	public bool IsHolding => _joint is not null;

	/// <summary>
	/// Starts holding <paramref name="body"/> by <paramref name="grabPoint"/>, in world space. Does nothing if we're already holding.
	/// Lower <paramref name="damping"/> follows the target more tightly, 1 is critically damped.
	/// </summary>
	public void Hold( Rigidbody body, Vector3 grabPoint, float damping = 4.0f )
	{
		if ( IsHolding ) return;

		var physicsBody = body.PhysicsBody;
		_body ??= new PhysicsBody( physicsBody.World ) { BodyType = PhysicsBodyType.Keyframed, AutoSleep = false };

		var bodyTransform = body.WorldTransform.WithScale( 1.0f );
		_massCenterOffset = physicsBody.LocalMassCenter - bodyTransform.PointToLocal( grabPoint );

		var point1 = new PhysicsPoint( _body );
		var point2 = new PhysicsPoint( physicsBody, physicsBody.LocalMassCenter );
		var maxForce = physicsBody.Mass * MaxLinearAcceleration;

		var inertia = physicsBody.Inertia;
		var maxTorque = MaxLinearAcceleration * MathF.Sqrt( physicsBody.Mass * MathF.Max( inertia.x, MathF.Max( inertia.y, inertia.z ) ) );

		_joint = PhysicsJoint.CreateControl( point1, point2 );
		_joint.LinearSpring = new PhysicsSpring( 32, damping, maxForce );
		_joint.AngularSpring = new PhysicsSpring( 64, damping, maxTorque );
	}

	public void Move( Transform target )
	{
		if ( _body is null ) return;
		_body.Transform = target.WithPosition( target.PointToWorld( _massCenterOffset ) );
	}

	public void Release()
	{
		_joint?.Remove();
		_joint = null;

		_body?.Remove();
		_body = null;
	}

	public static Rotation Stabilize( Rotation rotation )
	{
		var forward = rotation.Forward.WithZ( 0 );
		var up = rotation.Up.WithZ( 0 );

		// looking up the top of the view points backwards, looking down it points forwards
		var heading = forward.LengthSquared >= up.LengthSquared ? forward : (rotation.Forward.z > 0 ? -up : up);
		return Rotation.FromYaw( MathX.RadianToDegree( MathF.Atan2( heading.y, heading.x ) ) );
	}

	public static float ClampGrabDistance( Rigidbody body, Vector3 point, Transform eye, float distance, float min = 50.0f )
	{
		distance = MathF.Max( 0.0f, distance );
		var closest = body.FindClosestPoint( eye.Position );
		var along = distance + Vector3.Dot( closest - point, eye.Rotation.Forward );
		return along < min ? distance + (min - along) : distance;
	}

	public static void GetConnectedBodies( GameObject source, HashSet<Rigidbody> result )
	{
		foreach ( var rb in source.Root.Components.GetAll<Rigidbody>() )
		{
			if ( !result.Add( rb ) ) continue;

			foreach ( var joint in rb.Joints )
			{
				if ( joint.Object1 != null ) GetConnectedBodies( joint.Object1, result );
				if ( joint.Object2 != null ) GetConnectedBodies( joint.Object2, result );
			}
		}
	}
}

/// <summary>
/// Rotating a held object with the mouse and movement keys while use is held, snapping to 45 degrees when running.
/// </summary>
public sealed class HoldRotation
{
	public static float KeyboardRotateSpeed => 240.0f;

	Rotation _spin;
	Rotation _snap;
	bool _isSnapping;

	public void Reset( Rotation offset )
	{
		_spin = offset;
		_snap = offset;
	}

	public void UpdateSnapping()
	{
		var isSnapping = Input.Down( "run" ) || Input.Down( "walk" );
		if ( !isSnapping && _isSnapping ) _spin = _snap;
		_isSnapping = isSnapping;
	}

	public Rotation Rotate( Rotation frame, Vector3 move, bool pitchWithKeys )
	{
		var look = Input.AnalogLook * -1;
		var snapAngle = Input.Down( "walk" ) ? 15.0f : 45.0f;

		if ( _isSnapping )
		{
			if ( MathF.Abs( look.yaw ) > MathF.Abs( look.pitch ) ) look.pitch = 0;
			else look.yaw = 0;
		}

		_spin = Rotation.From( look ) * _spin;
		var spinRotation = _spin;

		if ( pitchWithKeys && MathF.Abs( move.x ) > 0.001f )
			spinRotation = Rotation.From( new Angles( move.x * KeyboardRotateSpeed * Time.Delta, 0f, 0f ) ) * spinRotation;

		if ( MathF.Abs( move.y ) > 0.001f )
		{
			var worldRotation = frame * spinRotation;
			worldRotation = Rotation.FromYaw( move.y * KeyboardRotateSpeed * Time.Delta ) * worldRotation;
			spinRotation = frame.Inverse * worldRotation;
		}

		_spin = spinRotation;

		if ( _isSnapping )
		{
			spinRotation = frame * spinRotation;

			var angles = spinRotation.Angles();
			spinRotation = angles.SnapToGrid( snapAngle );

			spinRotation = frame.Inverse * spinRotation;
		}

		// save snap rotation so it can be applied after snap has finished
		_snap = spinRotation;
		return spinRotation;
	}
}
