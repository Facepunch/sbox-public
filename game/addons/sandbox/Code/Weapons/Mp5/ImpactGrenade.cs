/// <summary>
/// A 40mm launcher round - flies on a ballistic arc, nose along its velocity, and explodes on the
/// first thing it touches. Shooting it mid-flight sets it off too.
/// </summary>
public sealed class ImpactGrenade : ProjectileEntity, Component.IDamageable
{
	[Property] public float ExplosionRadius { get; set; } = 200f;
	[Property] public float ExplosionDamage { get; set; } = 125f;
	[Property] public float ExplosionForce { get; set; } = 1f;

	/// <summary>Seconds before a round that never hit anything is removed.</summary>
	[Property] public float Lifetime { get; set; } = 10f;

	bool _exploded;

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		if ( TimeSinceCreated > Lifetime )
		{
			GameObject.Destroy();
			return;
		}

		var velocity = Rigidbody.Velocity;
		if ( velocity.LengthSquared > 1f )
			WorldRotation = Rotation.LookAt( velocity, Vector3.Up );
	}

	protected override void OnHit( Collision collision = default )
	{
		Detonate();
	}

	void Component.IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( IsProxy || damage.Damage <= 0f ) return;

		Detonate();
	}

	void Detonate()
	{
		if ( _exploded ) return;
		_exploded = true;

		Explode( ExplosionRadius, ExplosionDamage, ExplosionForce );
	}
}
