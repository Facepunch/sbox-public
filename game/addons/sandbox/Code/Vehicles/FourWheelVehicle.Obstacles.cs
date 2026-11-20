public sealed partial class FourWheelVehicle
{
	// Suspension owns upward-facing contacts. These contacts only block obstacles
	// beside or above a tyre, and apply their impulses to the chassis.
	private void SimulateWheelObstacles( float dt )
	{
		if ( !WheelObstacleCollisions || dt <= 0 ) return;
		foreach ( var wheel in _wheels )
		{
			var center = wheel.Top + wheel.Down * (wheel.GroundDistance - wheel.Radius);
			var axle = WorldRotation * wheel.AxleBody;
			var forward = axle.Cross( -wheel.Down ).Normal;
			var rotation = Rotation.LookAt( forward, axle );
			var motion = PredictedVelocity( center ) * dt;
			if ( motion.LengthSquared > 0.0001f * wheel.Scale * wheel.Scale )
			{
				var hit = Ground( Scene.Trace.Cylinder( wheel.Width, wheel.Radius, center, center + motion ).Rotated( rotation ) );
				if ( hit.Hit && !hit.StartedSolid )
					ResolveWheelObstacle( wheel, hit, -MathF.Max( 0, hit.Distance - 0.1f * wheel.Scale ) * MathF.Max( 0, -motion.Normal.Dot( hit.Normal ) ), dt );
			}

			// Sweeps starting overlapped cannot supply a useful entry contact. Probe
			// the tyre's lateral cross-section to recover wall penetration, including
			// overlaps introduced by steering while the vehicle is stationary.
			for ( var i = 0; i < 16; i++ )
			{
				var angle = i * MathF.Tau / 16;
				var direction = forward * MathF.Cos( angle ) + axle * MathF.Sin( angle );
				var along = MathF.Abs( direction.Dot( axle ) );
				var radial = MathF.Sqrt( MathF.Max( 0, 1 - along * along ) );
				var reach = MathF.Min( wheel.Width * 0.5f / MathF.Max( along, 0.0001f ), wheel.Radius / MathF.Max( radial, 0.0001f ) );
				var hit = Ground( Scene.Trace.Ray( center, center + direction * (reach + 0.1f * wheel.Scale) ) );
				if ( !hit.Hit || hit.StartedSolid ) continue;
				var penetration = (reach - hit.Distance) * MathF.Max( 0, -direction.Dot( hit.Normal ) );
				ResolveWheelObstacle( wheel, hit, penetration, dt );
			}
		}
	}

	private void ResolveWheelObstacle( Wheel wheel, SceneTraceResult hit, float penetration, float dt )
	{
		var normal = hit.Normal;
		if ( normal.Dot( -wheel.Down ) >= MinimumGroundDot ) return;
		var point = hit.HitPosition;
		var other = hit.Body;
		var dynamicOther = other.IsValid() && other.BodyType == PhysicsBodyType.Dynamic;
		var relative = PredictedVelocity( point ) - (other.IsValid() ? other.GetVelocityAtPoint( point ) : Vector3.Zero);
		// Small, bounded separation velocity avoids explosive corrections at corners.
		var targetSpeed = penetration > 0 ? MathF.Min( MathF.Max( penetration - 0.05f * wheel.Scale, 0 ) * 0.2f / dt, 40f * wheel.Scale ) : penetration / dt;
		var speedChange = targetSpeed - relative.Dot( normal );
		if ( speedChange <= 0 ) return;
		var inverseMass = InverseMass( point, normal, point, normal );
		if ( dynamicOther )
		{
			var rotation = other.Rotation * other.InertiaRotation;
			var arm = rotation.Inverse * (point - other.MassCenter).Cross( normal );
			var inertia = other.Inertia;
			inverseMass += 1f / other.Mass + arm.x * arm.x / inertia.x + arm.y * arm.y / inertia.y + arm.z * arm.z / inertia.z;
		}
		if ( inverseMass <= 0 ) return;
		var impulse = normal * (speedChange / inverseMass);
		_body.PhysicsBody.ApplyImpulseAt( point, impulse );
		TrackImpulse( point, impulse );
		if ( dynamicOther ) other.ApplyImpulseAt( point, -impulse );
		ReportImpact( MathF.Max( 0, -relative.Dot( normal ) ), point );
	}
}