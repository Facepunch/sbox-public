using Sandbox.Npcs;

public partial class BaseBulletWeapon : BaseSandboxWeapon
{
	//
	// Ballistics (damage, pellets, spread, reach) and the attack itself live on the engine BaseCombatWeapon.
	// What's left here is the game feel - recoil and camera shake - and the sandbox's bullet collision rules.
	//

	/// <summary>
	/// Physical kick applied to a standalone (unheld) gun when it fires.
	/// </summary>
	[Property, Group( "#weapon.group.recoil" ), ClientEditable, Range( 0f, 500000f ), Step( 10f ), Title( "#weapon.setting.recoil_force" ), Description( "#weapon.setting.recoil_force.description" )]
	public float ShootForce { get; set; } = 100000f;

	/// <summary>
	/// Ragdolls take the bullet as a velocity change rather than a raw impulse, so a torso shot moves the
	/// body as visibly as a hand shot moves the hand. The struck bone gains <c>Ballistics.Force * RagdollImpactScale</c>
	/// units/sec along the shot; every other bone gains <see cref="RagdollBodyShare"/> of that.
	/// </summary>
	[Property, Group( "Ragdoll Impact" ), Range( 0f, 10f )] public float RagdollImpactScale { get; set; } = 1f;

	/// <summary>Fraction of the struck bone's velocity change given to the rest of the ragdoll.</summary>
	[Property, Group( "Ragdoll Impact" ), Range( 0f, 1f )] public float RagdollBodyShare { get; set; } = 0.3f;

	public override void PrimaryAttack()
	{
		base.PrimaryAttack();

		DoRecoil();
	}

	/// <summary>
	/// The per-shot kick - eye punch and camera shake for a held gun, a physical shove for a
	/// standalone one.
	/// </summary>
	protected virtual void DoRecoil()
	{
		if ( !HasOwner )
		{
			// Simulate physical recoil by pushing the weapon opposite to its fire direction
			if ( ShootForce > 0f && GetComponent<Rigidbody>( true ) is { } rb )
			{
				var muzzle = WeaponModel?.MuzzleGameObject?.WorldTransform ?? WorldTransform;
				rb.ApplyForce( muzzle.Rotation.Up * ShootForce );
			}

			return;
		}

		KickView();
	}

	/// <summary>
	/// Runs for every pellet on every peer - death ragdolls are local to each peer, so the push has to be
	/// applied wherever the body is simulated, not just on the host.
	/// </summary>
	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );

		EmitGunfireNoise( shot );

		if ( shot.Hit )
			PushRagdoll( shot.HitObject, shot.HitPosition, shot.Origin ?? GetMuzzleTransform().Position );
	}

	private TimeSince _timeSinceNoise;

	/// <summary>
	/// Gunfire is loud - let NPCs hear a player shooting. NPC shooters announce their own bursts.
	/// </summary>
	void EmitGunfireNoise( ShotEffect shot )
	{
		if ( !Networking.IsHost || shot.NoEvents || _timeSinceNoise < 0.25f ) return;

		var owner = Owner;
		if ( !owner.IsValid() ) return;

		_timeSinceNoise = 0;
		Scene.GetSystem<NpcStimulusSystem>()?.Broadcast( StimulusKind.Gunshot, owner.WorldPosition, owner.GameObject, radius: 3000f, lifetime: 1.5f );
	}

	void PushRagdoll( GameObject hitObject, Vector3 hitPosition, Vector3 origin )
	{
		if ( !hitObject.IsValid() ) return;

		var struck = hitObject.Components.GetInAncestorsOrSelf<Rigidbody>();
		if ( !struck.IsValid() || struck.IsProxy ) return;

		var physics = hitObject.Components.GetInAncestorsOrSelf<ModelPhysics>();
		if ( !physics.IsValid() || physics.Bodies.Count < 2 ) return;

		var speed = Ballistics.Force * RagdollImpactScale;
		if ( speed <= 0f ) return;

		var dir = (hitPosition - origin).Normal;

		foreach ( var body in physics.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;

			if ( rb == struck )
				rb.ApplyImpulseAt( hitPosition, dir * speed * rb.Mass );
			else if ( RagdollBodyShare > 0f )
				rb.ApplyImpulse( dir * speed * RagdollBodyShare * rb.Mass );
		}
	}

	/// <summary>
	/// Bullet trace for this gun - the sandbox's bullet collision rules, skipping player controller
	/// colliders (players are hit through their hitboxes).
	/// </summary>
	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius )
	{
		return Scene.Trace.Ray( ray, distance )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot )
			.WithCollisionRules( "bullet" )
			.WithoutTags( "playercontroller" )
			.Radius( radius )
			.UseHitboxes();
	}

}
