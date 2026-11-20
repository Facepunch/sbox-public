public enum ThrowType
{
	Far = 0,
	Near = 1
}

/// <summary>
/// A throwable grenade weapon
/// Cooks while held — explodes in hand if held too long
/// </summary>
public sealed class HandGrenadeWeapon : BaseSandboxWeapon
{
	[Property] public GameObject Prefab { get; set; }
	[Property] public float ThrowPower { get; set; } = 1200f;

	/// <summary>
	/// Sound played when the pin is pulled and cooking starts.
	/// </summary>
	[Property] public SoundEvent PinPullSound { get; set; }

	/// <summary>
	/// Sound played when deploying the next grenade after a throw.
	/// </summary>
	[Property] public SoundEvent DeploySound { get; set; }

	/// <summary>
	/// Fuse time in seconds — grenade explodes after this, whether thrown or not.
	/// </summary>
	[Property] public float Lifetime { get; set; } = 3f;

	/// <summary>
	/// Explosion damage radius.
	/// </summary>
	[Property] public float Radius { get; set; } = 256f;

	/// <summary>
	/// Maximum damage at the center.
	/// </summary>
	[Property] public float MaxDamage { get; set; } = 125f;

	/// <summary>
	/// Physics force scale for the explosion.
	/// </summary>
	[Property] public float Force { get; set; } = 1f;

	[Sync] TimeSince TimeSinceCooked { get; set; }
	[Sync] bool IsCooking { get; set; }
	[Sync] bool IsThrowing { get; set; }
	[Sync] TimeUntil TimeUntilThrown { get; set; }

	ThrowType CurrentThrowType { get; set; } = ThrowType.Far;
	float ThrowBlend { get; set; }

	public override bool IsInUse() => IsCooking;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		SetNextFire( 0.5f );
	}

	public override void OnPlayerDeath( PlayerDiedParams args )
	{
		if ( !IsCooking ) return;

		// Drop the grenade at your feet
		if ( HasOwner )
			Throw( Owner, Vector3.Down, 0.2f );
	}

	protected override void OnPrimaryPressed() => DropGrenade();
	protected override void OnPrimaryDown() { }

	protected override void OnControl()
	{
		var player = Owner;

		// Wait for throw animation to finish
		if ( IsThrowing )
		{
			if ( TimeUntilThrown )
			{
				IsThrowing = false;

				if ( !HasPrimaryAmmo() )
				{
					Inventory?.SwitchToBest();
					DestroyGameObject();
					return;
				}

				// Deploy next grenade
				if ( DeploySound is not null )
					GameObject.PlaySound( DeploySound );

					WeaponModel?.Renderer?.Set( "b_reload", true );
			}

			return;
		}

		// Start cooking on press
		if ( !IsCooking && CanPrimaryAttack() && (Input.Pressed( "Attack1" ) || Input.Pressed( "Attack2" )) )
		{
			IsCooking = true;
			TimeSinceCooked = 0;

			if ( PinPullSound is not null )
				GameObject.PlaySound( PinPullSound );

			WeaponModel?.Renderer?.Set( "b_charge", true );
			WeaponModel?.Renderer?.Set( "charge_type", 0 );
		}

		if ( !IsCooking )
			return;

		// Update throw direction blend
		UpdateThrowType();

		// Cooked too long — explode in hand. Ammo spend + switch/destroy happen host-side in ExplodeInHand.
		if ( TimeSinceCooked > Lifetime )
		{
			IsCooking = false;
			ExplodeInHand();
			return;
		}

		// Release both buttons to throw
		if ( !Input.Down( "Attack1" ) && !Input.Down( "Attack2" ) )
		{
			Throw( player );
		}
	}

	void UpdateThrowType()
	{
		bool attack1 = Input.Down( "Attack1" );
		bool attack2 = Input.Down( "Attack2" );

		float target = (attack1 && attack2) ? 0.5f : attack2 ? 1.0f : 0.0f;
		ThrowBlend = ThrowBlend.LerpTo( target, Time.Delta * 3.0f );
		CurrentThrowType = ThrowBlend < 0.4f ? ThrowType.Far : ThrowType.Near;

		WeaponModel?.Renderer?.Set( "throw_blend", ThrowBlend );
		WeaponModel?.Renderer?.Set( "throw_type", (int)CurrentThrowType );
	}

	void Throw( Player player, Vector3? overrideDirection = null, float powerScale = 1f )
	{
		IsCooking = false;

		// Ammo is host-authoritative (spent in SpawnProjectile); here we only check we still have one so
		// the owner-side reserve write can't be reverted by the host into an infinite-grenade dupe.
		if ( !HasPrimaryAmmo() )
		{
			Inventory?.SwitchToBest();
			DestroyGameObject();
			return;
		}

		var direction = overrideDirection ?? player.EyeTransform.Rotation.Forward;

		if ( !overrideDirection.HasValue && CurrentThrowType == ThrowType.Near )
		{
			direction = (direction + Vector3.Up * 0.3f).Normal;
			powerScale *= 0.5f;
		}

		var startPos = GetThrowPosition( player, direction );

		SpawnProjectile( player, startPos, direction, powerScale );

		PlayAttackSound();

		WeaponModel?.Renderer?.Set( "b_charge", false );
		WeaponModel?.Renderer?.Set( "b_attack", true );

		SetNextFire( 1f );
		IsThrowing = true;
		TimeUntilThrown = 0.5f;
	}

	Vector3 GetThrowPosition( Player player, Vector3 direction )
	{
		var eye = player.EyeTransform;
		var right = eye.Rotation.Right;
		var forward = direction;

		var origin = eye.Position;

		// Underthrow starts lower (waist height)
		if ( CurrentThrowType == ThrowType.Near )
			origin -= Vector3.Up * 20f;

		var target = origin + forward * 18f + right * 8f;

		var tr = Scene.Trace.Box( BBox.FromPositionAndSize( Vector3.Zero, 8f ), origin, target )
			.WithoutTags( "trigger", "ragdoll" )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.Run();

		return tr.Hit ? tr.EndPosition : target;
	}

	[Rpc.Host]
	void DropGrenade()
	{
		if ( !Prefab.IsValid() ) return;

		var go = Prefab.Clone( WorldPosition );

		var explosive = go.GetOrAddComponent<TimedExplosive>();
		if ( explosive.IsValid() )
		{
			explosive.Lifetime = Lifetime;
			explosive.Radius = Radius;
			explosive.Damage = MaxDamage;
			explosive.Force = Force;
			explosive.Attacker = Attacker;
		}

		// Don't collide with the weapon we dropped from
		var filter = go.AddComponent<PhysicsFilter>();
		filter.Body = GameObject;

		// No velocity — just drops in place
		go.NetworkSpawn();
	}

	[Rpc.Host]
	void SpawnProjectile( Player player, Vector3 startPos, Vector3 direction, float powerScale )
	{
		if ( !player.IsValid() ) return;
		if ( !Prefab.IsValid() ) return;

		// Host-authoritative ammo spend - the owner only predicted the throw, the host owns the count.
		TakePrimaryAmmo( 1 );

		var go = Prefab.Clone( startPos );

		// Configure the timed explosive with remaining fuse
		var explosive = go.GetOrAddComponent<TimedExplosive>();
		if ( explosive.IsValid() )
		{
			explosive.Lifetime = MathF.Max( 0.1f, Lifetime - TimeSinceCooked );
			explosive.Radius = Radius;
			explosive.Damage = MaxDamage;
			explosive.Force = Force;
			explosive.Attacker = player.GameObject;
		}

		var rb = go.GetComponent<Rigidbody>();
		if ( rb.IsValid() )
		{
			var baseVelocity = player.GetComponent<PlayerController>().Velocity;
			rb.Velocity = baseVelocity + direction * (ThrowPower * powerScale) + Vector3.Up * 100f;
			rb.AngularVelocity = go.WorldRotation.Right * 10f;
		}

		// Don't collide with the weapon we threw from
		var filter = go.AddComponent<PhysicsFilter>();
		filter.Body = GameObject;

		go.NetworkSpawn();
	}

	[Rpc.Host]
	void ExplodeInHand()
	{
		// Spawn the explosion directly
		Explosion.Spawn( new ExplosionEvent
		{
			Source = GameObject,
			Attacker = Attacker,
			Position = Owner.IsValid() ? Owner.EyeTransform.Position : WorldPosition,
			Rotation = Rotation.Identity,
			Radius = Radius,
			Damage = MaxDamage,
			ForceScale = Force
		} );

		// Spend one grenade host-side; only give up the weapon when the last one is gone (don't discard a
		// remaining stack just because one cooked off in your hand).
		TakePrimaryAmmo( 1 );
		if ( !HasPrimaryAmmo() )
		{
			Inventory?.SwitchToBest();
			DestroyGameObject();
		}
	}

	/// <summary>
	/// Draws the aiming dot with the current grenade availability.
	/// </summary>
	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		using var scope = painter.Scope();
		var color = !HasPrimaryAmmo() ? CrosshairNoShoot : CrosshairCanShoot;

		painter.BlendMode = BlendMode.Lighten;
		painter.Fill = color;
		painter.Stroke = Stroke.None;
		painter.Circle( center, 3f );
	}
}
