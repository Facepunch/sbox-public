using Sandbox;

/// <summary>
/// Explodes after a set time. Spawns an explosion prefab with configurable radius, damage, and force.
/// </summary>
public sealed class TimedExplosive : Component, Component.IDamageable
{
	[Property] public float Lifetime { get; set; } = 3f;
	[Property] public float Radius { get; set; } = 256f;
	[Property] public float Damage { get; set; } = 125f;
	[Property] public float Force { get; set; } = 1f;

	/// <summary>Who gets credit/blame for the explosion (the thrower). Host-side only. Falls back to the explosion itself.</summary>
	public GameObject Attacker { get; set; }

	[Sync( SyncFlags.FromHost )]
	TimeSince TimeSinceCreated { get; set; }

	bool HasExploded { get; set; }

	protected override void OnEnabled()
	{
		TimeSinceCreated = 0;
		HasExploded = false;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( TimeSinceCreated < Lifetime ) return;

		Explode();
	}

	[Rpc.Host]
	public void Explode()
	{
		if ( HasExploded ) return;

		HasExploded = true;

		Explosion.Spawn( new ExplosionEvent
		{
			Source = GameObject,
			Attacker = Attacker,
			Position = WorldPosition,
			Rotation = WorldRotation,
			Radius = Radius,
			Damage = Damage,
			ForceScale = Force
		} );

		GameObject.Destroy();
	}

	void Component.IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( IsProxy || !Networking.IsHost || HasExploded || damage.Damage <= 0f ) return;
		if ( damage.Tags.Contains( DamageTags.Crush ) ) return;
		if ( damage.Tags.Contains( DamageTags.Impact ) ) return;

		Explode();
	}

}
