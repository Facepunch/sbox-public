namespace Sandbox;

public partial class BaseWeapon
{
	//
	// Shot presentation, modelled on GMod's ShootEffects / DoImpactEffect. ShootEffects plays the
	// muzzle/fire presentation and relays it across the network; ImpactEffect is a static utility that
	// spawns the surface impact at a hit. Both are pure presentation - no gameplay, no damage.
	//

	/// <summary>
	/// Play the shot's muzzle/fire presentation with no ranged hit - muzzle flash, fire sound, shell
	/// eject. For melee swings and projectile launches. See <see cref="ShootEffects(ShotEffect[])"/>.
	/// </summary>
	public void ShootEffects() => ShootEffects( [new ShotEffect()] );

	/// <summary>
	/// Play the presentation for a single resolved shot. See <see cref="ShootEffects(ShotEffect[])"/>.
	/// </summary>
	public void ShootEffects( ShotEffect shot ) => ShootEffects( [shot] );

	/// <summary>
	/// Play <see cref="AttackSound"/> from the weapon - non-spatialized when the local player is the
	/// shooter, so your own gun doesn't pan around your head.
	/// </summary>
	protected void PlayAttackSound()
	{
		if ( Application.IsDedicatedServer ) return;
		if ( AttackSound is null ) return;

		var snd = GameObject.PlaySound( AttackSound );

		if ( snd.IsValid() && Network.IsOwner )
			snd.SpacialBlend = 0;
	}

	/// <summary>
	/// A resolved shot's presentation data - the muzzle <see cref="Origin"/> (for the tracer, null uses the
	/// muzzle transform) and the hit result (for the impact). Handed to <see cref="OnShootEffects(ShotEffect)"/>.
	/// </summary>
	public record struct ShotEffect(
		Vector3 HitPosition,
		bool Hit,
		Vector3 Normal,
		GameObject HitObject,
		Surface Surface,
		Vector3? Origin = null,
		bool NoEvents = false );

	/// <summary>
	/// Play the presentation for a volley - every pellet through
	/// <see cref="OnShootEffects(ShotEffect)"/>. Plays immediately for the shooter and relays through
	/// the host to everyone else as one message, so the shot is seen exactly once everywhere. Call
	/// from your attack; override <see cref="OnShootEffects(ShotEffect)"/> to define what plays.
	/// </summary>
	public void ShootEffects( ShotEffect[] volley )
	{
		if ( volley is null || volley.Length == 0 )
			return;

		PlayShotEffects( volley );

		if ( Networking.IsHost )
		{
			using ( Rpc.FilterExclude( c => c == Network.Owner || c == Connection.Local ) )
				BroadcastShotEffectVolley( volley );
		}
		else if ( !IsProxy )
		{
			// Owning client - the attack only runs here, so ask the host to relay for us.
			RelayShotEffectVolley( volley );
		}
	}

	void PlayShotEffects( ShotEffect[] volley )
	{
		foreach ( var shot in volley )
			OnShootEffects( shot );
	}

	// Runs on the host, where ShootEffects takes the play-and-broadcast branch.
	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RelayShotEffectVolley( ShotEffect[] volley ) => ShootEffects( volley );

	[Rpc.Broadcast]
	private void BroadcastShotEffectVolley( ShotEffect[] volley )
	{
		if ( volley is null )
			return;

		PlayShotEffects( volley );
	}

	/// <summary>
	/// Define the presentation for a resolved shot. Base spawns the surface impact and flies the
	/// tracer for every pellet, then plays <see cref="AttackSound"/>, fires the holder's "b_attack"
	/// gesture and runs <see cref="BaseWeaponModel.OnAttack"/> on the shown model (muzzle flash,
	/// brass) - those are skipped for <see cref="ShotEffect.NoEvents"/> pellets, which share the lead
	/// pellet's. Call it when you override. Runs on every peer that should see the shot.
	/// </summary>
	protected virtual void OnShootEffects( ShotEffect shot )
	{
		if ( Application.IsDedicatedServer ) return;

		// Every pellet leaves its mark, even the NoEvents ones.
		OnShootImpact( shot );

		// A default ShotEffect (melee, projectile launch) has no trajectory - no tracer. Every
		// pellet flies its own, so a shotgun blast is a fan of them.
		Vector3? hitPoint = shot.Hit || shot.HitPosition != default ? shot.HitPosition : null;
		if ( hitPoint.HasValue )
			WeaponModel?.DoTracerEffect( hitPoint.Value, shot.Origin );

		if ( shot.NoEvents ) return;

		PlayAttackSound();
		HolderRenderer?.Set( "b_attack", true );
		WeaponModel?.OnAttack( hitPoint, shot.Origin );
	}

	/// <summary>
	/// The mark a pellet leaves where it hit - runs for every pellet of a volley, on every peer.
	/// Base spawns the hit surface's bullet impact (sound, decal, particles - see
	/// <see cref="ImpactEffect(in ShotEffect)"/>). Override for your own (a melee thunk, a scorch).
	/// Does nothing for a miss.
	/// </summary>
	protected virtual void OnShootImpact( in ShotEffect shot ) => ImpactEffect( shot );

	/// <summary>
	/// Spawn a bullet impact at a trace hit - the hit surface's impact sound and decal/particle, oriented
	/// to the surface and parented to what was hit (its nearest bone for skinned models, so decals stick
	/// to moving things). A hit object that's already gone - killed by this very shot - still gets its
	/// impact, left where it landed in the world. A pure presentation utility like
	/// <see cref="ShootBullet(Scene, Ray, float, float, float, float, GameObject, GameObject, GameObject, TagSet)"/>;
	/// call it wherever you resolve a hit. Does nothing on a dedicated server or for a trace that missed.
	/// </summary>
	public static void ImpactEffect( SceneTraceResult tr )
	{
		if ( !tr.Hit )
			return;

		ImpactEffect( tr.GameObject, tr.Surface, tr.HitPosition, tr.Normal );
	}

	/// <inheritdoc cref="ImpactEffect(SceneTraceResult)"/>
	public static void ImpactEffect( in ShotEffect shot )
	{
		if ( !shot.Hit )
			return;

		ImpactEffect( shot.HitObject, shot.Surface, shot.HitPosition, shot.Normal );
	}

	static void ImpactEffect( GameObject hitObject, Surface surface, Vector3 position, Vector3 normal )
	{
		if ( Application.IsDedicatedServer )
			return;

		if ( !surface.IsValid() )
			return;

		var baseSurface = surface.GetBaseSurface();

		var sound = surface.SoundCollection.Bullet ?? baseSurface?.SoundCollection.Bullet;
		if ( sound.IsValid() )
			Sound.Play( sound, position );

		ImpactPrefab( hitObject, surface, position, normal );
	}

	/// <summary>
	/// Spawn just the surface impact prefab (decal/particles) at a hit, stuck to what was hit - no sound.
	/// For attacks that want the visual but their own impact audio (a melee thunk instead of a ricochet).
	/// </summary>
	public static void ImpactPrefab( GameObject hitObject, Surface surface, Vector3 position, Vector3 normal )
	{
		if ( Application.IsDedicatedServer )
			return;

		if ( !surface.IsValid() )
			return;

		var prefab = surface.PrefabCollection.BulletImpact ?? surface.GetBaseSurface()?.PrefabCollection.BulletImpact;
		if ( prefab is null )
			return;

		var impact = prefab.Clone( new CloneConfig
		{
			Transform = new Transform( position, Rotation.LookAt( -normal, Vector3.Random ) ),
			StartEnabled = true
		} );

		// Each peer spawns its own - never networked.
		impact.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;

		// The hit object can be gone by the time the impact plays - this shot killed it - and the
		// impact still belongs where it landed, anchored to the world.
		if ( !hitObject.IsValid() )
			return;

		// Stick it to what we hit, and to the nearest bone of a skinned model so it follows animation.
		var skinned = hitObject.GetComponentInChildren<SkinnedModelRenderer>();
		if ( skinned is { CreateBoneObjects: true } )
		{
			var closest = skinned.GetClosestBone( position );
			if ( closest is not null && skinned.GetBoneObject( closest ) is { } boneObject )
			{
				impact.SetParent( boneObject, true );
				return;
			}
		}

		impact.SetParent( hitObject, true );
	}
}
