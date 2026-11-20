public partial class Physgun
{
	/// <summary>
	/// Half-angle, in degrees, of the cone player aim has to stay within to keep pulling a locked object.
	/// </summary>
	static float PullConeAngle => 35.0f;

	/// <summary>
	/// Within this many degrees of the object the pull runs at full strength, fading out towards <see cref="PullConeAngle"/>.
	/// </summary>
	static float PullConeFullStrengthAngle => 10.0f;

	/// <summary>
	/// How far in front of player aim the cone's apex is placed when we lock onto an object.
	/// </summary>
	static float PullConeApexDistance => 16.0f;

	/// <summary>
	/// Once a gravity pull finds an object it locks onto it, and keeps pulling while our aim stays within
	/// a cone from an apex in front of us to the object. The apex follows our position.
	/// </summary>
	GrabState UpdatePullLock( GrabState locked, GrabState found, Transform aim )
	{
		if ( IsPullLock( locked ) )
		{
			var strength = GetPullStrength( locked, aim );
			if ( strength > 0 )
			{
				var state = locked with
				{
					GrabOffset = aim.Rotation.Inverse * locked.GameObject.WorldRotation,
					PullStrength = strength
				};

				_rotation.Reset( state.GrabOffset );

				return state;
			}
		}

		if ( !found.IsValid() ) return found;

		found.PullApex = aim.Forward * PullConeApexDistance;
		found.PullStrength = GetPullStrength( found, aim );

		if ( IsPullLock( found ) )
			PlayGravityLockSound();

		return found;
	}

	static bool IsPullLock( GrabState state )
	{
		return state.IsValid() && state.Pulling && state.Body.IsValid() && state.Body.MotionEnabled;
	}

	/// <summary>
	/// How strongly we pull, from 1 when aiming at the object down to 0 at the edge of the cone.
	/// </summary>
	static float GetPullStrength( GrabState state, Transform aim )
	{
		var apex = aim.Position + state.PullApex;

		// Measure against the part of the object nearest our aim, so the cone always covers the whole object.
		var aimPoint = apex + aim.Forward * apex.Distance( state.EndPoint );
		var target = FindClosestPoint( state.Body, aimPoint );

		var angle = Vector3.GetAngle( aim.Forward, target - apex );
		if ( angle >= PullConeAngle ) return 0;

		return 1 - MathX.LerpInverse( angle, PullConeFullStrengthAngle, PullConeAngle );
	}

	/// <summary>
	/// Pulls the locked object along the cone, towards its apex.
	/// </summary>
	void ApplyPullForce( GrabState state )
	{
		var apex = CurrentAimTransform.Position + state.PullApex;
		var direction = (apex - state.EndPoint).Normal;

		var ragdoll = GetRagdoll( state.Body );
		if ( !ragdoll.IsValid() )
		{
			state.Body.ApplyForceAt( state.EndPoint, direction * state.Body.Mass * PullForce * state.PullStrength );
			return;
		}

		foreach ( var part in ragdoll.Bodies )
		{
			var body = part.Component;
			if ( !body.IsValid() || !body.MotionEnabled ) continue;

			body.ApplyForce( direction * body.Mass * PullForce * state.PullStrength );
		}
	}

	static float PullSearchRadius => 4.0f;
	static float PullSearchNearAimRadius => 32.0f;

	static bool CanPull( Rigidbody body )
	{
		return body.IsValid() && body.MotionEnabled && body.PhysicsBody.IsValid();
	}

	/// <summary>
	/// When the aim ray misses, look for something to pull near it.
	/// </summary>
	Rigidbody FindPullTarget( Transform aim, SceneTraceResult aimTrace )
	{
		var swept = Scene.Trace.Ray( aim.Position, aim.Position + aim.Forward * Range )
			.Radius( PullSearchRadius )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( swept.Hit && swept.Component is Rigidbody sweptBody && CanPull( sweptBody ) )
			return sweptBody;

		if ( !aimTrace.Hit )
			return null;

		var point = aimTrace.EndPosition;
		var candidates = Scene.FindInPhysics( new Sphere( point, PullSearchNearAimRadius ) )
			.Select( x => x.GetComponent<Rigidbody>() )
			.Where( x => CanPull( x ) && x.GameObject.Root != GameObject.Root )
			.Distinct()
			.OrderBy( x => x.FindClosestPoint( point ).Distance( point ) );

		foreach ( var body in candidates )
		{
			var tr = Scene.Trace.Ray( aim.Position, body.FindClosestPoint( point ) )
				.IgnoreGameObjectHierarchy( GameObject.Root )
				.Run();

			if ( !tr.Hit || tr.GameObject?.Root == body.GameObject.Root )
				return body;
		}

		return null;
	}

	/// <summary>
	/// The ragdoll a body is part of, if it's one of its bodies.
	/// </summary>
	static ModelPhysics GetRagdoll( Rigidbody body )
	{
		if ( !body.IsValid() ) return null;

		var ragdoll = body.GameObject.GetComponentInParent<ModelPhysics>();
		if ( !ragdoll.IsValid() || ragdoll.Bodies.Count < 2 ) return null;

		foreach ( var part in ragdoll.Bodies )
		{
			if ( part.Component == body )
				return ragdoll;
		}

		return null;
	}

	/// <summary>
	/// The root body of the ragdoll this body is part of, the same one that drives the ragdoll's transform.
	/// </summary>
	static Rigidbody GetRagdollRoot( Rigidbody body )
	{
		var ragdoll = GetRagdoll( body );
		if ( !ragdoll.IsValid() ) return null;

		var root = ragdoll.GetComponentInChildren<Rigidbody>( true, true );
		return CanPull( root ) ? root : null;
	}

	/// <summary>
	/// The closest point on an object, counting every body of a ragdoll and not just the one we hold.
	/// </summary>
	static Vector3 FindClosestPoint( Rigidbody body, Vector3 position )
	{
		var closest = body.FindClosestPoint( position );

		var ragdoll = GetRagdoll( body );
		if ( !ragdoll.IsValid() ) return closest;

		foreach ( var part in ragdoll.Bodies )
		{
			if ( !part.Component.IsValid() ) continue;

			var point = part.Component.FindClosestPoint( position );
			if ( point.Distance( position ) < closest.Distance( position ) )
				closest = point;
		}

		return closest;
	}
}
