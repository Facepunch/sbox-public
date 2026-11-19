/// <summary>
/// The wheels. They aren't bodies: each finds the ground below where it rests, and everything a wheel does - spring,
/// damper, anti-roll bar, drive, brake and grip - is a push on the body where it meets the ground.
///
/// The maths is in meters and Newtons. Lengths and velocities come in as inches and are converted, forces are Newtons
/// until they become impulses on the body.
/// </summary>
public sealed partial class FourWheelVehicle
{
	// Contacts steeper than 60 degrees are obstacles, not suspension support.
	private const float MinimumGroundDot = 0.5f;

	private sealed class Wheel
	{
		public VehicleAxleParams Axle;

		/// <summary>
		/// Where the wheel centre rests in body space. The suspension runs straight down the body from here.
		/// </summary>
		public Vector3 HardPoint;

		public float Scale = 1f;
		public float MassScale = 1f;
		public float Mass => Axle.WheelMass * MassScale;
		public float Radius => Axle.WheelRadius * Scale;
		public float Width => Axle.TyreWidth * Scale;
		public float SpringLength => Axle.SpringAdditionalLength * Scale;

		/// <summary>
		/// Steering angle, radians, positive to the right.
		/// </summary>
		public float SteerAngle;

		/// <summary>
		/// Drive or brake torque, Newton meters.
		/// </summary>
		public float Torque;

		/// <summary>
		/// Held still by the handbrake.
		/// </summary>
		public bool Locked;

		/// <summary>
		/// Friction of the tyre the wheel has on (normal, braking or power slide).
		/// </summary>
		public float TyreFriction;

		/// <summary>
		/// How far below the hard point's radius-above the ground is, inches: the wheel centre sits at
		/// <c>HardPoint + down * (GroundDistance - Radius)</c>. Spring length plus radius in the air.
		/// </summary>
		public float GroundDistance;

		/// <summary>
		/// What the wheel is pushing the body up with, Newtons.
		/// </summary>
		public float Load;

		/// <summary>
		/// Rolling speed, radians a second.
		/// </summary>
		public float AngularVelocity;

		public bool InContact;
		public PhysicsBody GroundBody;

		/// <summary>
		/// The hard point and the suspension's direction in the world this tick.
		/// </summary>
		public Vector3 Top;
		public Vector3 Down;

		/// <summary>
		/// Where the tyre touches the ground, or would at full extension.
		/// </summary>
		public Vector3 Contact;
		public Vector3 GroundNormal;

		/// <summary>
		/// The spring pushes harder on a slope, to hold the car up as well as into it.
		/// </summary>
		public float SlopeFactor;
		public float StabilizerForce;
		public float Friction;

		/// <summary>
		/// The axle and the way the wheel rolls, both flat on the ground.
		/// </summary>
		public Vector3 ProjectedAxle;
		public Vector3 ProjectedForward;

		/// <summary>
		/// Whether the grip forces hit the friction limit, and how fast the tyre slides over the ground once they
		/// have (in/s) - for the skid sound and the debug readout.
		/// </summary>
		public bool AtGripLimit;
		public float SlideSpeed;

		/// <summary>
		/// The axle in body space, steered: straight it points left.
		/// </summary>
		public Vector3 AxleBody => Rotation.FromAxis( Vector3.Up, -SteerAngle.RadianToDegree() ) * Vector3.Left;
	}

	private readonly Wheel[] _wheels = new Wheel[WheelCount];
	private static Wheel CreateWheel( VehicleAxleParams axle, Vector3 hardPoint, float scale, float massScale )
	{
		return new Wheel
		{
			Axle = axle,
			HardPoint = hardPoint,
			Scale = scale,
			MassScale = massScale,
			TyreFriction = axle.Friction,
			GroundDistance = (axle.SpringAdditionalLength + axle.WheelRadius) * scale,
		};
	}

	/// <summary>
	/// The car carries its wheels' mass, and their inertia as point masses at the hard points - without it, it's light
	/// and far too easy to spin. The wheels' weight goes straight to the ground at the contact patches
	/// (<see cref="SimulateSpring"/>), so the springs still only hold the body up.
	/// </summary>
	private void AddWheelMass()
	{
		if ( _totalWheelMass <= 0 ) return;

		var physics = _body.PhysicsBody;
		var inertia = physics.Inertia;
		var axes = physics.InertiaRotation;
		var center = physics.LocalMassCenter;

		// the body's own tensor in body axes (the diagonal - a car is near enough symmetric for the rest)
		var principal = new[] { axes * Vector3.Forward, axes * Vector3.Left, axes * Vector3.Up };
		var tensor = Vector3.Zero;
		for ( int k = 0; k < 3; k++ )
		{
			var p = principal[k];
			tensor += new Vector3( p.x * p.x, p.y * p.y, p.z * p.z ) * inertia[k];
		}

		foreach ( var wheel in _wheels )
		{
			var r = wheel.HardPoint * WorldScale - center;
			tensor += new Vector3( r.y * r.y + r.z * r.z, r.x * r.x + r.z * r.z, r.x * r.x + r.y * r.y ) * wheel.Mass;
		}

		_body.MassOverride = _bodyMass + _totalWheelMass;
		_body.InertiaTensor = tensor;
		_body.InertiaTensorRotation = Rotation.Identity;
	}

	/// <summary>
	/// The wheels' pushes on the body for this tick.
	/// </summary>
	private void Simulate( float dt )
	{
		var tx = WorldTransform;

		// how the body is moving, before anything here pushes it
		_tickVelocity = _body.Velocity;
		_tickAngularVelocity = _body.AngularVelocity;
		_tickMassCenter = _body.PhysicsBody.MassCenter;
		_tickDelta = dt;

		foreach ( var wheel in _wheels )
		{
			FindGround( wheel, tx );
		}

		SimulateStabilizer( _wheels[0], _wheels[1], FrontAxle );
		SimulateStabilizer( _wheels[2], _wheels[3], RearAxle );

		SimulateBodyForces( dt );

		foreach ( var wheel in _wheels )
		{
			SimulateSpring( wheel, dt );
		}

		SimulateGrip( dt );
		SimulateWheelObstacles( dt );
	}

	/// <summary>
	/// Find the ground under the wheel (<see cref="WheelSweep"/>) and lay the axle flat on it.
	/// </summary>
	private void FindGround( Wheel wheel, Transform tx )
	{
		wheel.Top = tx.PointToWorld( wheel.HardPoint );
		wheel.Down = tx.Rotation.Down;

		var axle = tx.Rotation * wheel.AxleBody;
		var contact = WheelSweep( wheel.Top, wheel.Down, axle, wheel.Radius, wheel.Width, wheel.SpringLength, wheel.Axle.MaximumCompression * wheel.Scale, wheel.GroundDistance );

		wheel.InContact = contact.Hit;
		wheel.GroundBody = contact.Body;

		if ( contact.Hit )
		{
			wheel.GroundDistance = contact.Distance;
			wheel.Contact = contact.Point;
			wheel.GroundNormal = contact.Normal;
			wheel.SlopeFactor = 1.1f / (MathF.Abs( wheel.Down.Dot( wheel.GroundNormal ) ) + 0.1f);
		}
		else
		{
			// in the air: no load, and the wheel hangs at full extension
			wheel.Load = 0.0f;
			wheel.GroundDistance = wheel.SpringLength + wheel.Radius;
			wheel.Contact = wheel.Top + wheel.Down * wheel.GroundDistance;
			wheel.GroundNormal = -wheel.Down;
			wheel.SlopeFactor = 1.0f;
		}

		// grip is the ground's friction times the tyre's
		wheel.Friction = (contact.Hit ? contact.Surface?.Friction ?? 1.0f : 0.0f) * wheel.TyreFriction;

		wheel.ProjectedAxle = (axle - wheel.GroundNormal * axle.Dot( wheel.GroundNormal )).Normal;
		wheel.ProjectedForward = wheel.ProjectedAxle.Cross( wheel.GroundNormal ).Normal;
	}

	private struct WheelContact
	{
		public bool Hit;

		/// <summary>
		/// See <see cref="Wheel.GroundDistance"/>.
		/// </summary>
		public float Distance;
		public Vector3 Point;
		public Vector3 Normal;
		public Surface Surface;
		public PhysicsBody Body;
	}

	/// <summary>
	/// Where the ground is under a wheel: a sweep of the wheel's own shape, a cylinder as wide as the tyre around its
	/// axle, from a radius above the hard point down to the spring's full length. It meets a step's edge on the front
	/// of the tyre, and the slant of that contact is what lifts the wheel up onto it.
	/// </summary>
	private WheelContact WheelSweep( Vector3 hardPoint, Vector3 down, Vector3 axle, float radius, float width, float springLength, float maximumCompression, float previousDistance )
	{
		var length = springLength + radius;
		var top = hardPoint - down * radius;
		var end = top + down * length;
		var rotation = Rotation.LookAt( axle.Cross( -down ).Normal, axle );
		var compression = Math.Clamp( maximumCompression, 0, radius );
		var previousCenter = hardPoint + down * (previousDistance - radius);
		// Starting a radius up finds ground the wheel's been pushed up onto, but up there it can start inside something
		// - a step's riser, the underside of stairs. Then start lower: from anywhere clear it finds the same ground, where
		// the ray down the middle finds whatever's under the centre, and the wheel would flick between the two.
		for ( var lift = 1.0f; lift >= 0.0f; lift -= 0.5f )
		{
			var tr = Ground( Scene.Trace.Cylinder( width, radius, hardPoint - down * (compression * lift), end ).Rotated( rotation ) );
			if ( tr.StartedSolid ) continue;
			if ( !tr.Hit ) return default;
			if ( tr.Normal.Dot( -down ) < MinimumGroundDot ) break;
			// Do not acquire support above the current axle by rolling into its side.
			if ( (tr.HitPosition - previousCenter).Dot( down ) < 0 ) break;

			// On flat ground the whole width of the tyre touches, and the sweep reports anywhere along it - a different
			// side each tick, rocking the car as the wheel's load jumps across. Take the point on the tyre's centre line.
			// Shape sweeps include a fixed world-space collision margin. Solve the cylinder
			// against the reported contact plane so tiny wheels use the same relative travel.
			var normalAxle = Math.Clamp( tr.Normal.Dot( axle ), -1f, 1f );
			var support = radius * MathF.Sqrt( MathF.Max( 0f, 1f - normalAxle * normalAxle ) )
				+ width * 0.5f * MathF.Abs( normalAxle );
			var travel = (support - (hardPoint - tr.HitPosition).Dot( tr.Normal )) / down.Dot( tr.Normal );
			var distance = radius + travel;
			distance = Math.Clamp( distance, radius - compression, length );
			var center = hardPoint + down * (distance - radius);
			var across = (axle - tr.Normal * axle.Dot( tr.Normal )).Normal;

			return new WheelContact
			{
				Hit = true,
				Distance = distance,
				Point = tr.HitPosition - across * (tr.HitPosition - center).Dot( across ),
				Normal = tr.Normal,
				Surface = tr.Surface,
				Body = tr.Body,
			};
		}

		// Inside something all the way down, or touched it above the axle (a wall, an overhang) - nothing a wheel could
		// roll up onto. Fall back to a ray from the hard point.
		var ground = WheelGroundRay( hardPoint, down, length );
		// A bottomed-out spring must still support the chassis. Clamp travel, not contact.
		if ( ground.Hit ) ground.Distance = MathF.Max( ground.Distance, radius - compression );
		return ground;
	}

	private WheelContact WheelGroundRay( Vector3 hardPoint, Vector3 down, float length )
	{
		var ray = Ground( Scene.Trace.Ray( hardPoint, hardPoint + down * length ) );

		if ( !ray.Hit || ray.StartedSolid || ray.Normal.Dot( -down ) < MinimumGroundDot ) return default;

		return new WheelContact
		{
			Hit = true,
			Distance = ray.Distance,
			Point = ray.HitPosition,
			Normal = ray.Normal,
			Surface = ray.Surface,
			Body = ray.Body,
		};
	}

	/// <summary>
	/// What a wheel stands on: anything but this vehicle and players.
	/// </summary>
	private SceneTraceResult Ground( SceneTrace trace ) => trace.IgnoreGameObjectHierarchy( GameObject.Root ).WithoutTags( "player" ).Run();

	/// <summary>
	/// An anti-roll bar: the more one wheel of an axle is compressed than the other, the harder it pushes them back level.
	/// </summary>
	private void SimulateStabilizer( Wheel left, Wheel right, VehicleAxleParams axle )
	{
		var leftTravel = (left.GroundDistance - left.SpringLength - left.Radius) * MetersPerInch;
		var rightTravel = (right.GroundDistance - right.SpringLength - right.Radius) * MetersPerInch;

		left.StabilizerForce = (rightTravel - leftTravel) * (axle.StabilizerConstant * _bodyMass * 0.5f / left.Scale);
		right.StabilizerForce = -left.StabilizerForce;
	}

	/// <summary>
	/// The spring and damper, pushing the body up off the ground, plus the wheel's own weight on it.
	/// </summary>
	private void SimulateSpring( Wheel wheel, float dt )
	{
		wheel.Load = 0;
		if ( !wheel.InContact )
			return;

		// the wheel stands on the ground under its own weight (see AddWheelMass)
		var force = wheel.Mass * GravityLength;

		// Shorter travel must support the same gravitational acceleration. Scale stiffness
		// inversely with length and damping with its square root to retain damping ratio.
		var compression = -(wheel.GroundDistance - wheel.SpringLength - wheel.Radius) * MetersPerInch;
		if ( compression > 0 )
		{
			var springForce = compression * wheel.Axle.SpringConstant * _bodyMass / wheel.Scale + wheel.StabilizerForce;
			springForce *= wheel.SlopeFactor.Clamp( 0.0f, 3.0f );

			// the body's speed into the ground along the spring, positive while it's lifting off
			var surfaceVelocity = MeasuredVelocity( wheel.Contact ) * MetersPerInch;
			var speed = -surfaceVelocity.Dot( wheel.GroundNormal ) * wheel.GroundNormal.Dot( wheel.Down );
			var damping = (speed > 0 ? wheel.Axle.SpringDamping : wheel.Axle.SpringDampingCompression) * _bodyMass / MathF.Sqrt( FunctionalScale );

			// Damped implicitly: it never pushes harder than it takes to stop the corner moving this tick, or the
			// strong compression damping overshoots every tick and the car buzzes on its springs.
			var inverseMass = InverseMass( wheel.Contact, wheel.GroundNormal, wheel.Contact, wheel.GroundNormal );
			damping /= 1.0f + damping * dt * inverseMass;

			springForce -= damping * speed;

			if ( springForce > 0 )
				force += springForce;
		}

		wheel.Load = force;

		PushAt( wheel, wheel.GroundNormal * (force * dt) );
	}

	private Vector3 _tickVelocity;
	private Vector3 _tickAngularVelocity;
	private Vector3 _tickMassCenter;
	private float _tickDelta;

	/// <summary>
	/// Gravity the physics applies to the body over a tick (the extra gravity is ours, a push like any other).
	/// </summary>
	private Vector3 BodyGravity => Scene.PhysicsWorld.Gravity * _body.GravityScale;

	/// <summary>
	/// How the body moved at a point over the last tick. The velocity we read is where the last tick ended, and the
	/// physics adds gravity across its substeps, so a car sitting still reads as sinking (or rolling downhill) at half
	/// a tick's gravity - 6 in/s at 50 Hz. Taken as it is, the dampers hold the car up instead of the springs, and a
	/// car held on a hill never reads as stopped.
	/// </summary>
	private Vector3 MeasuredVelocity( Vector3 point )
	{
		return _tickVelocity + _tickAngularVelocity.Cross( point - _tickMassCenter ) - BodyGravity * (_tickDelta * 0.5f);
	}

	/// <summary>
	/// How the body will move at a point over the next tick if nothing but gravity acts. What a brake or a locked
	/// wheel has to cancel to hold the car still - including on a hill.
	/// </summary>
	private Vector3 PredictedVelocity( Vector3 point )
	{
		return _tickVelocity + _tickAngularVelocity.Cross( point - _tickMassCenter ) + BodyGravity * (_tickDelta * 0.5f);
	}

	/// <summary>
	/// The tyres' grip. First the sideways forces that would stop each axle sliding, then each wheel's share of those
	/// and its drive or brake force, cut down to what its load and friction can hold.
	/// </summary>
	private void SimulateGrip( float dt )
	{
		var (front, rear) = SidewaysForces( dt );
		var needed = new[] { front, rear };

		var hasTorque = _wheels.Any( w => w.Torque != 0.0f );
		var bodyVelocity = MeasuredVelocity( _tickMassCenter ) * MetersPerInch;
		var holdVelocity = PredictedVelocity( _tickMassCenter ) * MetersPerInch;
		var carMass = _body.PhysicsBody.Mass;

		// parked: hold still
		var parked = bodyVelocity.Length < 0.5f * FunctionalScale && !hasTorque;

		// stopping pushes are shared between the wheels on the ground, so together they stop the car without overshooting
		var wheelsOnGround = Math.Max( 1, _wheels.Count( w => w.InContact ) );

		for ( int i = 0; i < WheelCount; i++ )
		{
			var wheel = _wheels[i];
			var maxForce = wheel.Load * wheel.Friction;
			var axis = i / 2;

			if ( wheel.Locked || parked )
			{
				wheel.AngularVelocity = 0.0f;
				ApplyLockedWheelGrip( wheel, dt, maxForce, ref needed[axis] );
				continue;
			}

			var frontDir = wheel.ProjectedForward;
			var bodySpeed = bodyVelocity.Dot( frontDir );
			var radius = wheel.Radius * MetersPerInch;

			wheel.AngularVelocity = bodySpeed / radius;

			// Match rolling resistance to the size-scaled drive force, independently of gravity.
			// Fade in near rest to avoid chatter.
			var forwardForce = -wheel.Axle.RollingResistance * FunctionalScale * wheel.Load * (bodySpeed / (0.1f * FunctionalScale)).Clamp( -1.0f, 1.0f );

			// A brake holds like friction: whatever it takes to stop the car, up to its strength. It never pushes the car
			// along, and it holds against the hill as well as the speed.
			var torqueForce = wheel.Torque / radius;
			if ( _braking )
			{
				var stop = -holdVelocity.Dot( frontDir ) * carMass / dt / wheelsOnGround;
				var strength = MathF.Abs( torqueForce );
				torqueForce = stop.Clamp( -strength, strength );
			}

			forwardForce += torqueForce;

			// both together can't be more than the tyre holds
			var sidewaysForce = needed[axis];
			var total = forwardForce * forwardForce + sidewaysForce * sidewaysForce;
			wheel.AtGripLimit = total > maxForce * maxForce;
			if ( wheel.AtGripLimit )
			{
				var factor = MathF.Sqrt( maxForce * maxForce / total );
				forwardForce *= factor;
				sidewaysForce *= factor;
			}

			// what the first wheel of an axle couldn't hold, the second tries to
			needed[axis] -= sidewaysForce;

			PushAt( wheel, wheel.ProjectedAxle * (sidewaysForce * dt) );
			PushAt( wheel, frontDir * (forwardForce * dt) );

			// How fast the tyre slides sideways - only once it has let go. A gripping tyre runs at a slip angle, and
			// the inside and outside wheels of a turn roll at different speeds, but neither of those squeals.
			wheel.SlideSpeed = wheel.InContact && wheel.AtGripLimit ? MathF.Abs( MeasuredVelocity( wheel.Contact ).Dot( wheel.ProjectedAxle ) ) : 0.0f;
		}
	}

	/// <summary>
	/// The sideways force at the middle of each axle that takes its sideways speed away this tick (a little more, to
	/// settle quicker), solved for both axles at once since pushing one also turns the body under the other.
	/// </summary>
	private (float Front, float Rear) SidewaysForces( float dt )
	{
		var frontPos = (_wheels[0].Contact + _wheels[1].Contact) * 0.5f;
		var backPos = (_wheels[2].Contact + _wheels[3].Contact) * 0.5f;
		var frontDir = (_wheels[0].ProjectedAxle + _wheels[1].ProjectedAxle).Normal;
		var backDir = _wheels[2].ProjectedAxle;

		// how a push at the front/back moves the front/back
		var m00 = InverseMass( frontPos, frontDir, frontPos, frontDir );
		var m11 = InverseMass( backPos, backDir, backPos, backDir );
		var frontBack = InverseMass( frontPos, frontDir, backPos, backDir );

		const float relaxationRate = 1.2f;
		var a = -PredictedVelocity( frontPos ).Dot( frontDir ) * MetersPerInch / dt * relaxationRate;
		var b = -PredictedVelocity( backPos ).Dot( backDir ) * MetersPerInch / dt * relaxationRate;

		var det = m00 * m11 - frontBack * frontBack;
		if ( MathF.Abs( det ) < 1e-12f )
			return (0, 0);

		var inv = 1.0f / det;
		return ((m11 * a - frontBack * b) * inv, (m00 * b - frontBack * a) * inv);
	}

	/// <summary>
	/// A locked wheel slides with plain friction against wherever its contact patch is going - which includes the
	/// patch swinging round as the car yaws, so a handbrake turn doesn't spin away.
	///
	/// Each locked wheel stops its patch exactly, for the car's real inertia at that point, and the next one works from
	/// the car's motion after it. Pushed all at once from the same motion, the pushes below the centre of mass roll
	/// the car under the other wheels, and a parked car rocks and creeps sideways.
	/// </summary>
	private void ApplyLockedWheelGrip( Wheel wheel, float dt, float maxForce, ref float neededSideways )
	{
		var velocity = PredictedVelocity( wheel.Contact ) * MetersPerInch;
		var planar = velocity - wheel.GroundNormal * velocity.Dot( wheel.GroundNormal );
		var speed = planar.Length;

		var force = Vector3.Zero;
		var stop = 0.0f;
		if ( speed > 1e-4f )
		{
			// the force that stops the patch this tick
			var direction = -planar / speed;
			stop = speed / InverseMass( wheel.Contact, direction, wheel.Contact, direction ) / dt;
			force = direction * MathF.Min( maxForce, stop );
		}

		// a locked wheel that's still holding the car isn't sliding
		wheel.AtGripLimit = stop > maxForce;
		var sliding = MeasuredVelocity( wheel.Contact );
		wheel.SlideSpeed = wheel.InContact && wheel.AtGripLimit ? (sliding - wheel.GroundNormal * sliding.Dot( wheel.GroundNormal )).Length : 0.0f;

		neededSideways -= force.Dot( wheel.ProjectedAxle );

		PushAt( wheel, force * dt );
		TrackImpulse( wheel.Contact, force * dt * InchesPerMeter );
	}

	/// <summary>
	/// Carry an impulse we've just pushed the body with (kg in/s, at a point) into this tick's idea of how it's moving.
	/// </summary>
	private void TrackImpulse( Vector3 point, Vector3 impulse )
	{
		var body = _body.PhysicsBody;
		var inertiaRotation = body.Rotation * body.InertiaRotation;
		var inertia = body.Inertia;

		var angular = inertiaRotation.Inverse * (point - _tickMassCenter).Cross( impulse );
		angular = new Vector3( angular.x / inertia.x, angular.y / inertia.y, angular.z / inertia.z );

		_tickVelocity += impulse / body.Mass;
		_tickAngularVelocity += inertiaRotation * angular;
	}

	/// <summary>
	/// Push the body at the wheel's contact, an impulse in Newton seconds - and push back on whatever it's standing on.
	/// </summary>
	private void PushAt( Wheel wheel, Vector3 impulse )
	{
		var push = impulse * InchesPerMeter;
		_body.PhysicsBody.ApplyImpulseAt( wheel.Contact, push );

		if ( wheel.GroundBody.IsValid() && wheel.GroundBody.BodyType == PhysicsBodyType.Dynamic )
		{
			wheel.GroundBody.ApplyImpulseAt( wheel.Contact, -push );
		}
	}

	/// <summary>
	/// How much a push at point A along direction A changes the body's speed at point B along direction B, per kg m/s -
	/// the inverse mass the body has between those two.
	/// </summary>
	private float InverseMass( Vector3 pointA, Vector3 dirA, Vector3 pointB, Vector3 dirB )
	{
		var body = _body.PhysicsBody;
		var inertiaRotation = body.Rotation * body.InertiaRotation;
		var inertia = body.Inertia;

		var armA = inertiaRotation.Inverse * (pointA - body.MassCenter).Cross( dirA );
		var armB = inertiaRotation.Inverse * (pointB - body.MassCenter).Cross( dirB );
		var angular = armA.x * armB.x / inertia.x + armA.y * armB.y / inertia.y + armA.z * armB.z / inertia.z;

		return dirA.Dot( dirB ) / body.Mass + angular;
	}
}
