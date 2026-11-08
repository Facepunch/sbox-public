namespace Sandbox;

public sealed partial class PlayerController : Component
{
	const float _groundedFeetLift = 1.0f;

	/// <summary>
	/// Make sure the body and our components are created
	/// </summary>
	void EnsureComponentsCreated()
	{
		if ( !ColliderObject.IsValid() )
		{
			ColliderObject = GameObject.Children.FirstOrDefault( x => x.Name == "Colliders" );
			if ( !ColliderObject.IsValid() )
			{
				ColliderObject = new GameObject( GameObject, true, "Colliders" );
			}
		}

		ColliderObject.LocalTransform = global::Transform.Zero;
		ColliderObject.Tags.SetFrom( BodyCollisionTags );

		Body.CollisionEventsEnabled = true;
		Body.CollisionUpdateEventsEnabled = true;
		Body.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds;

		BodyCollider = ColliderObject.GetOrAddComponent<CapsuleCollider>();
		FeetCollider = ColliderObject.GetOrAddComponent<BoxCollider>();

		Body.Flags = Body.Flags.WithFlag( ComponentFlags.Hidden, !_showRigidBodyComponent );

		ColliderObject.Flags = ColliderObject.Flags.WithFlag( GameObjectFlags.Hidden, !_showColliderComponent );
		BodyCollider.Flags = BodyCollider.Flags.WithFlag( ComponentFlags.Hidden, !_showColliderComponent );
		FeetCollider.Flags = FeetCollider.Flags.WithFlag( ComponentFlags.Hidden, !_showColliderComponent );

		if ( Renderer is null && UseAnimatorControls )
		{
			Renderer = GetComponentInChildren<SkinnedModelRenderer>();
		}
	}

	/// <summary>
	/// Update the body dimensions, and change the physical properties based on the current state
	/// </summary>
	void UpdateBody()
	{
		var feetHeight = CurrentHeight * 0.5f;
		var radius = BodyRadius * 0.5f;
		var up = UpDirection;
		var localUp = Rotation.FromYaw( -WorldRotation.Yaw() ) * up;
		var localUpAbs = localUp.Abs();

		// If we're not on the ground, we have slippy as fuck feet
		var feetFriction = 0.0f;

		if ( IsOnGround )
		{
			bool wantsBrakes = false;

			// If we're standing still we want the brakes on
			wantsBrakes = WishVelocity.Length < 5.0f;

			// If we're going slower, we want the brakes on
			wantsBrakes = wantsBrakes || WishVelocity.Length < Velocity.Length * 0.9f;

			//
			// The 1 here is normal friction.
			// The 100 is just so we can change BrakePower to a 0-1 range, to make things
			// less confusing to the user. We multiply by our current ground friction so we
			// will still slide around on ice etc.
			//
			if ( wantsBrakes )
			{
				feetFriction = 1 + (100.0f * BrakePower * GroundFriction);
			}
		}

		//
		// Position the body capsule to cover the upper half of the character.
		// Keep its bottom tip at least 1 unit above the floor.
		// If it becomes too short to fit, disable it and let the feet collider cover the rest.
		//
		BodyCollider.Radius = radius;
		var bodyStart = CurrentHeight - radius;
		var bodyEnd = MathF.Max( bodyStart - (feetHeight - radius), radius + 1.0f );
		BodyCollider.Start = localUp * bodyStart;
		BodyCollider.End = localUp * bodyEnd;
		BodyCollider.Friction = 0.0f;
		BodyCollider.Elasticity = 0.0f;
		BodyCollider.Enabled = bodyEnd < bodyStart;

		var feetLength = BodyCollider.Enabled ? feetHeight : CurrentHeight;
		FeetCollider.Friction = feetFriction;
		FeetCollider.Elasticity = 0.0f;
		FeetCollider.Enabled = true;

		var locking = Body.Locking;
		locking.Pitch = true;
		locking.Yaw = true;
		locking.Roll = true;
		Body.Locking = locking;

		Body.MassOverride = BodyMass;
		Body.EnhancedCcd = false;
		if ( Body.PhysicsBody is { IsValid: true } physicsBody )
		{
			physicsBody.AutoSleep = false;
			physicsBody.ContactRecycling = false;
		}

		//
		// When trying to move, we move the mass center up to the waist so the player can "step" over smaller shit
		// When not moving we drop it to the foot position.
		//
		float massCenter = IsOnGround ? WishVelocity.Length.Clamp( 0, CurrentHeight * 0.5f ) : CurrentHeight * 0.5f;
		Body.MassCenterOverride = localUp * massCenter;
		Body.OverrideMassCenter = true;

		Mode?.UpdateRigidBody( Body );

		//
		// Small lips are absorbed by Reground, and larger "steps" by TryStep.
		//
		// Keep the feet outside Box3D's speculative mesh contacts while ground probes maintain support.
		var feetLift = IsOnGround && !GroundIsDynamic && !Body.Gravity
			? MathF.Min( _groundedFeetLift / MathF.Abs( WorldTransform.UniformScale ), feetLength * 0.5f )
			: 0.0f;
		var feetBoxLength = feetLength - feetLift;
		FeetCollider.Scale = new Vector3( BodyRadius ) + localUpAbs * (feetBoxLength - BodyRadius);
		FeetCollider.Center = localUp * (feetLift + feetBoxLength * 0.5f);
	}
}
