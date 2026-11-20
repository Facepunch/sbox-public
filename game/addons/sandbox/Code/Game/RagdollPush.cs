/// <summary>
/// How hard a ragdoll is thrown when a player or NPC dies, based on what killed them.
/// </summary>
public static class RagdollPush
{
	const float MeleeKnockback = 200f;

	public static Vector3 GetVelocity( in DamageInfo damage, GameObject victim, float explosionMin, float explosionMax, out float propMass )
	{
		propMass = 0;
		var position = victim.WorldPosition;

		if ( damage.Tags.Contains( DamageTags.Explosion ) && damage.Origin != Vector3.Zero )
		{
			var dist = (position - damage.Origin).Length;
			var strength = MathX.Remap( dist, 0, 512, explosionMin, explosionMax, true );
			var dir = ((position - damage.Origin).Normal + Vector3.Up).Normal;

			return dir * strength;
		}

		if ( damage.Tags.Contains( DamageTags.Impact ) && damage.Attacker?.GetComponent<Rigidbody>() is { } prop )
		{
			if ( GetImpactVelocity( damage, position, prop ) is { } impact )
			{
				propMass = prop.Mass;
				return impact;
			}

			return GetAttackerVelocity( damage.Attacker );
		}

		// Only guns use their ballistics - melee weapons carry an unused ballistic config (the crowbar's
		// says 3000) that would throw the body across the map
		var weapon = damage.Weapon?.GetComponent<BaseCombatWeapon>();
		var force = weapon switch
		{
			MeleeWeapon => MeleeKnockback,
			BaseBulletWeapon bullets => bullets.Ballistics.Force,
			_ => 0f,
		};

		if ( force > 0 && damage.Position != damage.Origin )
			return Vector3.Direction( damage.Origin, damage.Position ) * force;

		// the victim's own movement is already in the ragdoll's bones
		if ( damage.Attacker.IsValid() && damage.Attacker.Root == victim.Root )
			return Vector3.Zero;

		return GetAttackerVelocity( damage.Attacker );
	}

	public static Vector3 GetAttackerVelocity( GameObject attacker )
	{
		if ( !attacker.IsValid() ) return Vector3.Zero;
		return attacker.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
	}

	public static void Apply( ModelPhysics physics, Vector3 velocity, Vector3 origin, float propMass = 0 )
	{
		if ( !physics.IsValid() ) return;

		if ( propMass > 0 )
		{
			var ragdollMass = physics.Bodies.Sum( x => x.Component.IsValid() ? x.Component.Mass : 0 );
			velocity *= propMass / (propMass + ragdollMass);
		}

		if ( velocity.Length < 1 ) return;

		foreach ( var body in physics.Bodies )
		{
			var rb = body.Component;
			if ( !rb.IsValid() ) continue;

			// physics impacts have no origin
			var direction = origin == Vector3.Zero ? velocity.Normal : Vector3.Direction( origin, rb.WorldPosition );
			rb.ApplyImpulse( direction * velocity.Length * rb.Mass );
		}
	}

	static Vector3? GetImpactVelocity( in DamageInfo damage, Vector3 position, Rigidbody prop )
	{
		if ( DamageBlame.GetThrownVelocity( prop.GameObject ) is { } thrown )
			return thrown;

		// nobody threw it, so push away from where it hit, with the speed from the engine's impact damage formula
		var away = (position + Vector3.Up * 40f - damage.Position).WithZ( 0 );
		if ( away.IsNearlyZero() ) return null;

		var perHit = prop.ImpactDamage > 0 ? prop.ImpactDamage : prop.Mass / 10f;
		var minSpeed = prop.MinImpactDamageSpeed > 0 ? prop.MinImpactDamageSpeed : 500f;
		var speed = damage.Damage / (perHit * 1.2f) * minSpeed;

		return (away.Normal + Vector3.Up * 0.2f).Normal * speed;
	}
}
