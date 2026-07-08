namespace Sandbox;

public partial class BaseWeapon
{
	/// <summary>
	/// Traces a bullet through the world and, on the host, damages and pushes whatever it hits. This
	/// is a pure utility - it holds no state, so you can call it from anywhere (a weapon, a trap, an
	/// NPC, an explosion). Returns the trace result so the caller can spawn tracers, decals and play
	/// sounds.
	/// </summary>
	/// <remarks>
	/// Damage and the physics impulse are applied on the host only, so it stays authoritative no
	/// matter who calls it - a client still gets the trace result back for effects, but can't deal
	/// damage. Call it from the host (or a host-routed path) when you want the hit to count.
	/// </remarks>
	/// <param name="scene">Scene to trace in.</param>
	/// <param name="ray">Origin and direction of the bullet (apply any aim cone before calling).</param>
	/// <param name="distance">How far the bullet travels.</param>
	/// <param name="radius">Bullet radius - 0 for a thin ray, higher to be forgiving.</param>
	/// <param name="damage">Damage dealt to what we hit.</param>
	/// <param name="force">Impulse applied to the hit physics body, along the bullet direction.</param>
	/// <param name="attacker">Who fired this - credited for the damage and ignored by the trace.</param>
	/// <param name="weapon">The weapon GameObject, recorded on the damage.</param>
	/// <param name="ignore">Hierarchy to ignore. Defaults to <paramref name="attacker"/>.</param>
	/// <param name="tags">Optional damage tags (e.g. "bullet").</param>
	public static SceneTraceResult ShootBullet( Scene scene, Ray ray, float distance, float radius, float damage, float force, GameObject attacker, GameObject weapon, GameObject ignore = null, TagSet tags = null )
	{
		var tr = scene.Trace.Ray( ray, distance )
			.IgnoreGameObjectHierarchy( ignore ?? attacker )
			.Radius( radius )
			.UseHitboxes()
			.Run();

		return ShootBullet( tr, damage, force, attacker, weapon, tags );

	}

	/// <summary>
	/// Applies the damage and physics impulse for an already-run bullet trace. Use this overload when
	/// you want to run - and inspect or customise - the trace yourself, then resolve the hit; the
	/// <see cref="ShootBullet(Scene, Ray, float, float, float, float, GameObject, GameObject, GameObject, TagSet)"/>
	/// overload is just this with a standard trace run for you. Returns <paramref name="tr"/> unchanged
	/// so callers can chain tracers, decals and sounds.
	/// </summary>
	/// <remarks>
	/// Damage and the impulse are applied on the host only - a client gets the trace straight back and
	/// deals nothing. Call it from the host (or a host-routed path) when you want the hit to count.
	/// </remarks>
	/// <param name="tr">The bullet trace result to resolve. Nothing happens if it didn't hit.</param>
	/// <param name="damage">Damage dealt to what we hit.</param>
	/// <param name="force">Impulse applied to the hit physics body, along the trace direction.</param>
	/// <param name="attacker">Who fired this - credited for the damage.</param>
	/// <param name="weapon">The weapon GameObject, recorded on the damage.</param>
	/// <param name="tags">Optional damage tags (e.g. "bullet").</param>
	/// <param name="hitboxTags">Merge the hit hitbox's tags (e.g. "head") into the damage tags, so
	/// receivers can apply hitgroup rules like headshot multipliers. Turn off for attacks that
	/// shouldn't count hitgroups (e.g. melee).</param>
	public static SceneTraceResult ShootBullet( SceneTraceResult tr, float damage, float force, GameObject attacker, GameObject weapon, TagSet tags = null, bool hitboxTags = true )
	{
		// Damage and physics are host authoritative - clients only get the trace back for effects.
		if ( !Networking.IsHost )
			return tr;

		if ( !tr.Hit || !tr.GameObject.IsValid() )
			return tr;

		tags = MergeHitboxTags( tr, tags, hitboxTags );

		ApplyBulletDamage( tr.GameObject, tr.StartPosition, tr.HitPosition, tr.Direction, damage, force, attacker, weapon, tags, tr.Hitbox, tr.Shape );

		return tr;
	}

	/// <summary>
	/// The hit hitbox's tags (e.g. "head") merged with <paramref name="tags"/> into a fresh set, so a
	/// caller-shared TagSet isn't polluted with one hit's tags. Off or no hitbox returns <paramref name="tags"/>.
	/// </summary>
	static TagSet MergeHitboxTags( in SceneTraceResult tr, TagSet tags, bool hitboxTags )
	{
		if ( !hitboxTags || tr.Hitbox?.Tags is null )
			return tags;

		var merged = new TagSet();
		if ( tags is not null ) merged.Add( tags );
		merged.Add( tr.Hitbox.Tags );
		return merged;
	}

	// The damage + impulse core shared by trace hits and client hit claims (BaseWeapon.HitClaims).
	static void ApplyBulletDamage( GameObject hitObject, Vector3 origin, Vector3 position, Vector3 direction, float damage, float force, GameObject attacker, GameObject weapon, TagSet tags, Hitbox hitbox = null, PhysicsShape shape = null )
	{
		if ( !hitObject.IsValid() )
			return;

		if ( hitObject.GetComponentInParent<IDamageable>() is { } damageable )
		{
			damageable.OnDamage( new DamageInfo( damage, attacker, weapon )
			{
				Position = position,
				Origin = origin,
				Hitbox = hitbox,
				Shape = shape,
				Tags = tags ?? new(),
			} );
		}

		if ( hitObject.GetComponentInParent<Rigidbody>() is { } body )
			body.ApplyImpulseAt( position, direction * force );
	}

	/// <summary>
	/// Fires a volley of bullets - one trace per pellet, each randomly deflected within
	/// <paramref name="spread"/>, resolved through <see cref="ShootBullet(SceneTraceResult, float, float, GameObject, GameObject, TagSet, bool)"/>
	/// (GMod's <c>FireBullets</c>). A pure utility like ShootBullet - damage and impulse apply on the
	/// host only. Returns every pellet's trace so the caller can drive tracers and impacts.
	/// </summary>
	/// <param name="scene">Scene to trace in.</param>
	/// <param name="ray">Origin and direction the volley is centred on.</param>
	/// <param name="count">How many pellets to fire.</param>
	/// <param name="spread">Spread cone in degrees - x wide, y tall.</param>
	/// <param name="distance">How far each pellet travels.</param>
	/// <param name="radius">Pellet radius - 0 for a thin ray, higher to be forgiving.</param>
	/// <param name="damage">Damage dealt by each pellet.</param>
	/// <param name="force">Impulse applied by each pellet to the hit physics body.</param>
	/// <param name="attacker">Who fired this - credited for the damage and ignored by the traces.</param>
	/// <param name="weapon">The weapon GameObject, recorded on the damage.</param>
	/// <param name="ignore">Hierarchy to ignore. Defaults to <paramref name="attacker"/>.</param>
	/// <param name="tags">Optional damage tags (e.g. "bullet").</param>
	public static SceneTraceResult[] ShootBullets( Scene scene, Ray ray, int count, Vector2 spread, float distance, float radius, float damage, float force, GameObject attacker, GameObject weapon, GameObject ignore = null, TagSet tags = null )
	{
		count = Math.Max( count, 0 );
		var results = new SceneTraceResult[count];

		for ( var i = 0; i < count; i++ )
		{
			var pellet = ray with { Forward = ray.Forward.WithAimCone( spread.x, spread.y ) };
			results[i] = ShootBullet( scene, pellet, distance, radius, damage, force, attacker, weapon, ignore, tags );
		}

		return results;
	}

	/// <summary>
	/// The GameObject credited for this weapon's damage - the holding player, or the weapon itself
	/// when unheld. Override to credit someone else (e.g. the driver of a mounted weapon).
	/// </summary>
	protected virtual GameObject Attacker => Owner?.GameObject ?? GameObject;

	/// <summary>
	/// The trace used for this weapon's bullets. Base traces against hitboxes and ignores the
	/// <see cref="Attacker"/>'s hierarchy. Override to add collision rules or filters.
	/// </summary>
	protected virtual SceneTrace BulletTrace( Ray ray, float distance, float radius )
	{
		return Scene.Trace.Ray( ray, distance )
			.IgnoreGameObjectHierarchy( Attacker )
			.Radius( radius )
			.UseHitboxes();
	}

	/// <summary>
	/// Resolve a bullet trace you ran yourself - damage, impulse and hitbox tags, credited to
	/// <see cref="Attacker"/>. When the host controls the weapon (seats, NPCs, a host-owned weapon)
	/// the damage applies immediately. On the owning client the hit is sent to the host as a claim -
	/// the client's trace decides where the shot landed, the host applies (or rejects) the damage.
	/// See BaseWeapon.HitClaims.
	/// </summary>
	protected SceneTraceResult ShootBullet( SceneTraceResult tr, float damage, float force, TagSet tags = null, bool hitboxTags = true )
	{
		if ( Networking.IsHost )
			return ShootBullet( tr, damage, force, Attacker, GameObject, tags, hitboxTags );

		// Only the owner can claim hits with this weapon.
		if ( IsProxy )
			return tr;

		if ( TryBuildPelletClaim( tr, tags, hitboxTags, out var pellet ) )
			SendShotClaim( damage, force, new[] { pellet } );

		return tr;
	}

	/// <summary>
	/// Fires a bullet from this weapon's <see cref="BaseWeapon.AimRay"/> - traced with
	/// <see cref="BulletTrace"/>, resolved through <see cref="ShootBullet(SceneTraceResult,float,float,TagSet,bool)"/>.
	/// </summary>
	protected SceneTraceResult ShootBullet( float distance, float radius, float damage, float force, TagSet tags = null )
	{
		var tr = BulletTrace( AimRay, distance, radius ).Run();
		return ShootBullet( tr, damage, force, tags );
	}

	/// <summary>
	/// Fires a volley of bullets from this weapon's <see cref="BaseWeapon.AimRay"/> - one
	/// <see cref="BulletTrace"/> per pellet, each randomly deflected within <paramref name="spread"/>
	/// degrees. On the host the damage applies immediately; on the owning client the whole volley is
	/// claimed to the host in a single message (see BaseWeapon.HitClaims). Returns every pellet's
	/// trace so the caller can drive tracers and impacts.
	/// </summary>
	protected SceneTraceResult[] ShootBullets( int count, Vector2 spread, float distance, float radius, float damage, float force, TagSet tags = null )
	{
		count = Math.Max( count, 0 );
		var results = new SceneTraceResult[count];
		var ray = AimRay;

		if ( Networking.IsHost )
		{
			for ( var i = 0; i < count; i++ )
			{
				var pellet = ray with { Forward = ray.Forward.WithAimCone( spread.x, spread.y ) };
				var tr = BulletTrace( pellet, distance, radius ).Run();
				results[i] = ShootBullet( tr, damage, force, Attacker, GameObject, tags );
			}

			return results;
		}

		// Owning client - trace every pellet, then claim the volley to the host as one message.
		List<PelletClaim> claims = null;

		for ( var i = 0; i < count; i++ )
		{
			var pellet = ray with { Forward = ray.Forward.WithAimCone( spread.x, spread.y ) };
			var tr = BulletTrace( pellet, distance, radius ).Run();
			results[i] = tr;

			if ( !IsProxy && TryBuildPelletClaim( tr, tags, hitboxTags: true, out var claim ) )
				(claims ??= new()).Add( claim );
		}

		if ( claims is not null )
			SendShotClaim( damage, force, claims.ToArray() );

		return results;
	}
}
