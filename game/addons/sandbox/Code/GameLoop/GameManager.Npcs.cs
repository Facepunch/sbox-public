using Sandbox.Npcs;

/// <summary>
/// Sandbox's rules for NPCs getting hurt and dying: who gets the blame, headshots, blood, the kill feed, and what happens to the ragdoll.
/// </summary>
public sealed partial class GameManager : Component.IDamageable.IEvents
{
	/// <summary>
	/// Seconds an NPC's ragdoll lies around, unless <see cref="ServerSettings.KeepNpcCorpses"/> is on.
	/// </summary>
	const float NpcCorpseLifetime = 30f;

	/// <summary>
	/// Seconds a weapon an NPC dropped on death lies around before it's removed, if nobody picks it up.
	/// </summary>
	const float NpcDroppedWeaponLifetime = 20f;

	void Component.IDamageable.IEvents.OnDamaging( Component.IDamageable.DamageEvent e )
	{
		if ( !((Target)e.Victim).IsNpc )
			return;

		e.Instigator = DamageBlame.Find( e.DamageInfo )?.GameObject ?? e.Instigator;

		if ( e.DamageInfo.Tags.Contains( DamageTags.Headshot ) )
			e.AcceptedDamage *= 2f;
	}

	void Component.IDamageable.IEvents.OnDamaged( Component.IDamageable.DamageEvent e )
	{
		// Slammed into something: the damage carries the surface normal there, facing the victim
		var normal = e.DamageInfo.Normal;
		if ( ((Target)e.Victim).IsNpc && !normal.IsNearlyZero() )
			SplatterBlood( e.Victim, e.DamageInfo.Position, normal, FindImpactObject( e.Victim, e.DamageInfo ), e.DamageInfo.Damage / 100f );
	}

	// The damage only carries where and which way the victim hit something, so look up what is there.
	GameObject FindImpactObject( GameObject victim, DamageInfo damage )
	{
		var normal = damage.Normal;
		var position = damage.Position;

		return Scene.Trace.Ray( position + normal * 8f, position - normal * 16f )
			.IgnoreGameObjectHierarchy( victim )
			.WithoutTags( "npc", "player", "ragdoll" )
			.Run().GameObject;
	}

	void Component.IDamageable.IEvents.OnDied( Component.IDamageable.DeathEvent e )
	{
		if ( ((Target)e.Victim).Get<BaseNpc>() is not { } npc )
			return;

		OnNpcDeath( npc, e.DamageInfo, e.Instigator );

		if ( e.Ragdoll.IsValid() )
			SetUpNpcCorpse( npc, e.Ragdoll, e.DamageInfo );

		// the timer lives on the weapon, the NPC is about to be destroyed
		if ( npc is BaseCombatNpc { Weapon: { } weapon } && weapon.IsValid() && !weapon.IsHeld && !weapon.Inventory.IsValid() )
		{
			// A sandbox weapon marks itself when it falls out of a hand; others are marked here
			WeaponControls.PrepareDroppedWeapon( weapon, null );

			weapon.Invoke( NpcDroppedWeaponLifetime, () => RemoveIfUnclaimed( weapon ) );
		}
	}

	/// <summary>
	/// Remove a gun a dead NPC dropped, unless a player has picked it up since. A player who picked it up and
	/// dropped it again owns it now, so that drop is theirs to keep.
	/// </summary>
	static void RemoveIfUnclaimed( BaseCombatWeapon weapon )
	{
		if ( !weapon.IsValid() || weapon.IsHeld || weapon.Inventory.IsValid() )
			return;

		if ( weapon.GetComponent<Ownable>()?.Owner is not null )
			return;

		weapon.GameObject.Destroy();
	}

	/// <summary>
	/// Throws the ragdoll from the hit, lets the remover take it, gives it the NPC's owner, and clears it away after a while.
	/// </summary>
	static void SetUpNpcCorpse( BaseNpc npc, GameObject ragdoll, DamageInfo damage )
	{
		var velocity = RagdollPush.GetVelocity( damage, npc.GameObject, 500, 1500, out var propMass );
		RagdollPush.Apply( ragdoll.GetComponent<ModelPhysics>(), velocity, damage.Origin, propMass );

		ragdoll.Tags.Add( "removable" );

		if ( npc.Components.TryGet<Ownable>( out var ownable ) )
			Ownable.Set( ragdoll, ownable.Owner );

		ragdoll.Network.Refresh();

		// any of its components can run the timer
		if ( !ServerSettings.KeepNpcCorpses && ragdoll.GetComponent<Component>() is { } timer )
			timer.Invoke( NpcCorpseLifetime, ragdoll.Destroy );
	}

	static readonly string[] BloodDecals =
	[
		"decals/blood/blood_splatter_01.decal",
		"decals/blood/blood_splatter_02.decal",
		"decals/blood/blood_splatter_03.decal",
		"decals/blood/blood_splatter_04.decal",
	];

	/// <summary>
	/// Paint the surface an NPC smacked into - a big splat where it hit, with droplets around it. Harder
	/// hits make more mess. Purely cosmetic, so every client makes its own.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void SplatterBlood( GameObject victim, Vector3 point, Vector3 normal, GameObject surface, float severity )
	{
		if ( Application.IsDedicatedServer )
			return;

		severity = severity.Clamp( 0f, 1f );
		if ( severity < 0.15f )
			return;

		var facing = Rotation.LookAt( normal );

		// A body lands flat on floors; only part of it hits a wall.
		var floor = MathF.Abs( normal.z ) > 0.7f;
		var size = floor ? 1f : 0.5f;

		var main = PickBloodDecal();
		if ( main is null )
			return;

		var mainScale = MathX.Lerp( 1f, 6f, severity ) * size;
		SpawnBloodDecal( main, point, normal, surface, mainScale );

		// Droplets can overlap the main splat, but must stay apart from one another.
		var mainRadius = BloodRadius( main, mainScale );
		var reach = floor ? mainRadius * 0.8f + 16f * severity : mainRadius + 48f * severity;
		Span<(Vector3 Position, float Radius)> placed = stackalloc (Vector3, float)[10];
		var placedCount = 0;

		var droplets = (int)(10f * severity * severity);
		for ( var i = 0; i < droplets; i++ )
		{
			var definition = PickBloodDecal();
			var scale = Game.Random.Float( 0.6f, 1.6f ) * MathX.Lerp( 0.6f, 1.2f, severity ) * size;
			var radius = BloodRadius( definition, scale );

			for ( var attempt = 0; attempt < 6; attempt++ )
			{
				var direction = facing * new Vector3( 0f, Game.Random.Float( -1f, 1f ), Game.Random.Float( -1f, 1f ) ).Normal;
				var distance = reach * MathF.Sqrt( Game.Random.Float( 0.02f, 1f ) );
				var spot = point + direction * distance;

				var overlaps = false;
				foreach ( var previous in placed[..placedCount] )
				{
					if ( previous.Position.Distance( spot ) < (previous.Radius + radius) * 0.6f )
					{
						overlaps = true;
						break;
					}
				}

				if ( overlaps )
					continue;

				// Find the surface again, so droplets follow it round corners instead of hanging in the air
				var trace = Scene.Trace.Ray( spot + normal * 8f, spot - normal * 16f ).WithoutTags( "npc", "player", "ragdoll" );
				if ( victim.IsValid() )
					trace = trace.IgnoreGameObjectHierarchy( victim );

				var tr = trace.Run();

				if ( !tr.Hit )
					continue;

				SpawnBloodDecal( definition, tr.HitPosition, tr.Normal, tr.GameObject, scale );
				placed[placedCount++] = (spot, radius);
				break;
			}
		}
	}

	static DecalDefinition PickBloodDecal()
	{
		return ResourceLibrary.Get<DecalDefinition>( Game.Random.FromArray( BloodDecals ) );
	}

	// Roughly how far the visible blood reaches from the decal's centre - the textures have a clear
	// border around the splat, so a bit under half the decal's width
	static float BloodRadius( DecalDefinition definition, float scale )
	{
		return definition is null ? 0f : definition.Width * scale * 0.35f;
	}

	static void SpawnBloodDecal( DecalDefinition definition, Vector3 point, Vector3 normal, GameObject surface, float scale )
	{
		if ( definition is null )
			return;

		var go = new GameObject( true, "blood" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.WorldPosition = point + normal;
		go.WorldRotation = Rotation.LookAt( -normal );

		// Stick to whatever we hit, so it moves with props
		if ( surface.IsValid() )
			go.SetParent( surface, true );

		var decal = go.Components.Create<Decal>();
		decal.Decals = [definition];
		decal.Scale = scale;
		decal.Rotation = Game.Random.Float( 0f, 360f );
		decal.Transient = true;
	}
}
