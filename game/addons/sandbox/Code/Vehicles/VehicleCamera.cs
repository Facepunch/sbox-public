/// <summary>
/// A chase camera for whoever's driving, in third person. It sits behind the car and swings round after it, a little
/// late - more so in a slide, where it looks the way the car's going. It hangs back as the car accelerates and closes
/// in under braking, the field of view opens up with speed and boost, it shakes a little when it's going fast, and it
/// settles after jumps. The mouse orbits it and it stays where you put it, until you've been driving a while without
/// touching the mouse - then it eases back behind the car.
/// </summary>
[Title( "Vehicle Camera" ), Category( "Vehicles" ), Icon( "videocam" )]
public sealed class VehicleCamera : Component, ICameraModifier
{
	/// <summary>
	/// How far behind the car the camera sits, inches.
	/// </summary>
	[Property] public float Distance { get; set; } = 230f;

	/// <summary>
	/// How far above the car's origin the camera sits, inches.
	/// </summary>
	[Property] public float Height { get; set; } = 75f;

	/// <summary>
	/// The point the camera looks at: this far above the car's origin, inches...
	/// </summary>
	[Property] public float LookHeight { get; set; } = 40f;

	/// <summary>
	/// ...and this far ahead of it.
	/// </summary>
	[Property] public float LookAhead { get; set; } = 40f;

	/// <summary>
	/// How quickly the camera swings round behind the car - higher is tighter.
	/// </summary>
	[Property, Group( "Follow" )] public float FollowRate { get; set; } = 4f;

	/// <summary>
	/// How far towards the way the car's actually going the camera looks in a slide, 0 to 1.
	/// </summary>
	[Property, Group( "Follow" ), Range( 0, 1 )] public float SlideFollow { get; set; } = 0.5f;

	/// <summary>
	/// How much of the car's pitch on a slope the camera takes, 0 to 1.
	/// </summary>
	[Property, Group( "Follow" ), Range( 0, 1 )] public float SlopeFollow { get; set; } = 0.5f;

	/// <summary>
	/// Inches the camera hangs back per inch per second squared of acceleration (closes in under braking).
	/// </summary>
	[Property, Group( "Speed" )] public float AccelerationLag { get; set; } = 0.06f;

	/// <summary>
	/// Inches further back at top speed.
	/// </summary>
	[Property, Group( "Speed" )] public float SpeedDistance { get; set; } = 40f;

	/// <summary>
	/// Degrees of field of view added at top speed, and on top of that while boosting.
	/// </summary>
	[Property, Group( "Speed" )] public float SpeedFov { get; set; } = 8f;

	/// <inheritdoc cref="SpeedFov"/>
	[Property, Group( "Speed" )] public float BoostFov { get; set; } = 6f;

	/// <summary>
	/// How much the camera shakes at top speed, inches.
	/// </summary>
	[Property, Group( "Speed" )] public float SpeedShake { get; set; } = 0.8f;

	/// <summary>
	/// Seconds of driving without touching the mouse before the camera eases back behind the car.
	/// </summary>
	[Property, Group( "Look" )] public float LookResetDelay { get; set; } = 3f;

	/// <summary>
	/// Miles per hour the car has to be doing for the camera to ease back behind it. Slower, it stays where you put it.
	/// </summary>
	[Property, Group( "Look" )] public float LookResetSpeed { get; set; } = 10f;

	int ICameraModifier.CameraOrder => 100;

	private FourWheelVehicle _vehicle;
	private bool _active;
	private float _cameraScale = 1f;

	private float _yaw;
	private float _pitch;
	private Angles _look;
	private TimeSince _timeSinceLook;

	private Vector3 _lastPosition;
	private Vector3 _velocity;
	private float _lastSpeed;
	private float _acceleration;

	// springs: the camera's height on the car (it settles after a jump), and how far it hangs back
	private float _height, _heightSpeed;
	private float _lag, _lagSpeed;
	private float _fov;
	private float _blocked;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		// other cameras (reflections, render targets) compose too - leave them be, without losing our place
		if ( Scene.Camera != camera )
			return;

		if ( !IsDriving() )
		{
			_active = false;
			return;
		}

		var dt = MathF.Max( Time.Delta, 1e-4f );
		var tx = WorldTransform;
		var rotation = tx.Rotation;
		var scale = MathF.Max( MathF.Abs( _vehicle.WorldScale.z ), 0.001f );
		var speedScale = _vehicle.FunctionalScale;

		// the car's velocity from how it moved, so it's the same for everyone (the body's only simulated on the host)
		if ( !_active )
			Snap( tx, scale );

		// Keep the existing orbit while resizing an occupied vehicle.
		if ( scale != _cameraScale )
		{
			var ratio = scale / _cameraScale;
			_height = tx.Position.z + (_height - tx.Position.z) * ratio;
			_heightSpeed *= ratio;
			_blocked *= ratio;
			_cameraScale = scale;
		}

		var velocity = (tx.Position - _lastPosition) / dt;
		_lastPosition = tx.Position;
		_velocity = _velocity.LerpTo( velocity, Ease( 15f, dt ) );

		var speed = _vehicle.Speed;
		_acceleration = _acceleration.LerpTo( (speed - _lastSpeed) / dt, Ease( 6f, dt ) );
		_lastSpeed = speed;

		var topSpeed = MathF.Max( 1f, _vehicle.Engine.MaxSpeed * 17.6f * speedScale );
		var speedFraction = (MathF.Abs( speed ) / topSpeed).Clamp( 0f, 1.5f );

		// Swing round after the car. Going forwards in a slide, look part way to where it's actually going.
		var carYaw = rotation.Angles().yaw;
		var targetYaw = carYaw;
		var flat = _velocity.WithZ( 0 );
		if ( speed > 100f * speedScale && flat.Length > 100f * speedScale )
		{
			var slip = AngleDelta( carYaw, Rotation.LookAt( flat ).Angles().yaw ).Clamp( -60f, 60f );
			targetYaw += slip * SlideFollow;
		}

		_yaw += AngleDelta( _yaw, targetYaw ) * Ease( FollowRate * (1f + speedFraction), dt );

		// a little of the slope, without the car's roll
		var carPitch = rotation.Angles().pitch.Clamp( -30f, 30f );
		_pitch = _pitch.LerpTo( carPitch * SlopeFollow, Ease( 6f, dt ) );

		// Look around with the mouse. It stays put until you've been driving a while without touching it.
		var input = Input.AnalogLook;
		if ( input.yaw != 0 || input.pitch != 0 )
		{
			_look.yaw += input.yaw;
			_look.pitch = (_look.pitch + input.pitch).Clamp( -40f, 70f );
			_timeSinceLook = 0;
		}

		if ( _timeSinceLook > LookResetDelay && MathF.Abs( speed ) > LookResetSpeed * 17.6f * speedScale )
		{
			var back = Ease( 1.5f, dt );
			_look.yaw -= AngleDelta( 0, _look.yaw ) * back;
			_look.pitch -= _look.pitch * back;
		}

		// keep it a half turn either way, however many times you've spun round
		_look.yaw = AngleDelta( 0, _look.yaw );

		DriveDriverLook();

		// the camera rides a spring on the car's height, so it dips and settles after a landing
		// - moving with it, so it keeps up on a hill and only lags when the car's climb changes suddenly
		Spring( ref _height, ref _heightSpeed, tx.Position.z, 10f, dt, _velocity.z );

		// hangs back while accelerating, closes in under braking
		var lag = (_acceleration / speedScale * AccelerationLag).Clamp( -50f, 70f ) + SpeedDistance * MathF.Min( speedFraction, 1f );
		Spring( ref _lag, ref _lagSpeed, lag, 4f, dt );

		var orbit = Rotation.From( _pitch + _look.pitch, _yaw + _look.yaw, 0 );
		var pivot = tx.Position.WithZ( _height ) + Vector3.Up * (LookHeight * scale);
		var target = pivot + Rotation.FromYaw( _yaw ) * Vector3.Forward * (LookAhead * scale);
		var distance = (Distance + _lag) * scale;
		var desired = pivot + orbit.Backward * distance + Vector3.Up * ((Height - LookHeight) * scale);

		// don't go through walls - snap in, ease back out
		var tr = Scene.Trace.Sphere( 8f * scale, pivot, desired ).WithTag( "world" ).IgnoreGameObjectHierarchy( GameObject.Root ).Run();
		var clear = tr.Hit ? tr.Distance : (desired - pivot).Length;
		_blocked = clear < _blocked ? clear : _blocked.LerpTo( clear, Ease( 3f, dt ) );
		var position = pivot + (desired - pivot).Normal * _blocked;

		// a little shake when it's flying
		var shake = SpeedShake * scale * speedFraction * speedFraction * GamePreferences.Screenshake / 0.3f;
		if ( shake > 0.01f * scale )
		{
			var t = Time.Now * 9f;
			position += new Vector3( Noise( t, 0 ), Noise( t, 10 ), Noise( t, 20 ) ) * shake;
		}

		// the field of view opens up with speed and boost
		var fov = SpeedFov * MathF.Min( speedFraction, 1f ) * MathF.Min( speedFraction, 1f ) + (_vehicle.Boosting ? BoostFov : 0f);
		_fov = _fov.LerpTo( fov, Ease( 3f, dt ) );

		view.Position = position;
		view.Rotation = Rotation.LookAt( target - position, Vector3.Up );
		view.FieldOfView += _fov;
		view.ZNear *= scale;
	}

	/// <summary>
	/// The local player is in the driver's seat, looking from behind.
	/// </summary>
	private bool IsDriving()
	{
		_vehicle ??= GetComponent<FourWheelVehicle>();
		var driver = _vehicle?.Seat?.GetOccupant();
		return driver.IsValid() && !driver.IsProxy && driver.ThirdPerson;
	}

	private void DriveDriverLook()
	{
		var seat = _vehicle?.Seat;
		var driver = seat?.GetOccupant();
		if ( !driver.IsValid() ) return;

		driver.EyeAngles = new Angles(
			_look.pitch.Clamp( seat.PitchRange.x, seat.PitchRange.y ),
			_look.yaw.Clamp( seat.YawRange.x, seat.YawRange.y ),
			0 );
	}

	/// <summary>
	/// Straight behind the car, no lag - getting in, or switching to third person.
	/// </summary>
	private void Snap( Transform tx, float scale )
	{
		_active = true;
		_cameraScale = scale;
		_yaw = tx.Rotation.Angles().yaw;
		_pitch = 0;
		_look = Angles.Zero;
		_timeSinceLook = 0;
		_lastPosition = tx.Position;
		_velocity = Vector3.Zero;
		_lastSpeed = _vehicle.Speed;
		_acceleration = 0;
		_height = tx.Position.z;
		_heightSpeed = 0;
		_lag = _lagSpeed = 0;
		_blocked = Distance * scale;
	}

	/// <summary>
	/// A critically damped spring towards a target moving at a given speed, responding at about the given rate.
	/// </summary>
	private static void Spring( ref float value, ref float speed, float target, float rate, float dt, float targetSpeed = 0f )
	{
		speed += ((target - value) * rate * rate + (targetSpeed - speed) * 2f * rate) * dt;
		value += speed * dt;
	}

	private static float Ease( float rate, float dt ) => 1f - MathF.Exp( -rate * dt );

	private static float AngleDelta( float from, float to ) => ((to - from) % 360f + 540f) % 360f - 180f;

	private static float Noise( float t, float seed ) => MathF.Sin( t + seed ) * 0.6f + MathF.Sin( t * 2.3f + seed * 1.7f ) * 0.4f;
}
