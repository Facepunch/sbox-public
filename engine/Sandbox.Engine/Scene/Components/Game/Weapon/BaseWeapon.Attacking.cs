namespace Sandbox;

public partial class BaseWeapon
{
	//
	// Primary / secondary attack, modelled on GMod's SWEP. You write one PrimaryAttack that spends ammo
	// (TakePrimaryAmmo) and fires the shot. It runs once, on whoever controls the weapon - the owning
	// client for a held weapon, the host for seats/NPCs/world weapons. The client is authoritative over
	// its own shots: it traces against the world it sees (so hits land where the shooter aimed, no lag
	// compensation) and reports them to the host as hit claims, which the host applies - and can reject
	// (see BaseWeapon.HitClaims). Ammo spends are mirrored to the host, which owns the magazine.
	//
	// The inventory pumps OnControl on the owning client: it reads input and fires the attack.
	//

	/// <summary>Seconds between primary shots - the fire rate (GMod's <c>Primary.Delay</c>).</summary>
	[Property, Feature( "Shooting" )] public float PrimaryDelay { get; set; } = 0.1f;

	/// <summary>Seconds between secondary shots.</summary>
	[Property, Feature( "Shooting" )] public float SecondaryDelay { get; set; } = 0.5f;

	/// <summary>
	/// When true primary fire keeps firing while the button is held; when false it fires once per press
	/// (GMod's <c>Primary.Automatic</c>).
	/// </summary>
	[Property, Feature( "Shooting" )] public bool PrimaryAutomatic { get; set; } = true;

	/// <summary>When true secondary fire is full-auto while held.</summary>
	[Property, Feature( "Shooting" )] public bool SecondaryAutomatic { get; set; } = false;

	/// <summary>Seconds after deploying (switching to) this weapon before it can fire.</summary>
	[Property, Feature( "Shooting" )] public float DeployTime { get; set; } = 0.5f;

	/// <summary>Sound played when the weapon attacks - the gunshot, swing, launch. Played by the base
	/// <see cref="OnShootEffects"/>, so it reaches every peer that sees the shot.</summary>
	[Property, Feature( "Shooting" )] public SoundEvent AttackSound { get; set; }

	/// <summary>Sound played when the trigger is pulled with no ammo.</summary>
	[Property, Feature( "Shooting" )] public SoundEvent DryFireSound { get; set; }

	/// <summary>
	/// Time until primary fire is off cooldown. Not synced - the firing side gates itself (GMod's
	/// GetNext/SetNextPrimaryFire). The host doesn't enforce the rate; a hardened game rate-checks
	/// incoming shot claims instead (see <see cref="OnValidateShotClaim"/>).
	/// </summary>
	protected TimeUntil NextPrimaryFire { get; set; }

	/// <summary>Time until secondary fire is off cooldown.</summary>
	protected TimeUntil NextSecondaryFire { get; set; }

	/// <summary>Put primary fire on cooldown for <paramref name="delay"/> seconds.</summary>
	public void SetNextPrimaryFire( float delay ) => NextPrimaryFire = delay;

	/// <summary>Put secondary fire on cooldown for <paramref name="delay"/> seconds.</summary>
	public void SetNextSecondaryFire( float delay ) => NextSecondaryFire = delay;

	/// <summary>Put both triggers on cooldown for <paramref name="delay"/> seconds - for weapons where a
	/// shot blocks primary and secondary alike.</summary>
	public void SetNextFire( float delay )
	{
		SetNextPrimaryFire( delay );
		SetNextSecondaryFire( delay );
	}

	/// <summary>
	/// Per-frame weapon logic, pumped by the inventory on the owning client. Reads input and drives the
	/// fire/reload loop. Override the individual hooks (PrimaryAttack, Think, ...) rather than this.
	/// </summary>
	protected override void OnControl()
	{
		Think();

		if ( WantsReload() && CanReload() )
			Reload();

		// Pulling a trigger cancels a cancellable reload, as long as that trigger has a round to fire.
		if ( IsReloading && CanCancelReload && ((WantsPrimaryAttack() && HasPrimaryAmmo()) || (WantsSecondaryAttack() && HasSecondaryAmmo())) )
			CancelReload();

		if ( WantsPrimaryAttack() )
		{
			if ( CanPrimaryAttack() )
				FirePrimary();
			else if ( NextPrimaryFire <= 0 && !IsReloading && !HasPrimaryAmmo() )
				DryFire();
		}

		if ( WantsSecondaryAttack() )
		{
			if ( CanSecondaryAttack() )
				FireSecondary();
			else if ( NextSecondaryFire <= 0 && !IsReloading && !HasSecondaryAmmo() )
				DryFire();
		}
	}

	/// <summary>Runs every frame before attack handling - continuous logic (spin-up, charge, ...). Base does nothing.</summary>
	protected virtual void Think() { }

	/// <summary>Is the player asking to fire primary this frame? Held when automatic, pressed otherwise.</summary>
	protected virtual bool WantsPrimaryAttack() => PrimaryAutomatic ? Input.Down( "attack1" ) : Input.Pressed( "attack1" );

	/// <summary>Is the player asking to fire secondary this frame?</summary>
	protected virtual bool WantsSecondaryAttack() => SecondaryAutomatic ? Input.Down( "attack2" ) : Input.Pressed( "attack2" );

	/// <summary>Is the player asking to reload this frame?</summary>
	protected virtual bool WantsReload() => Input.Pressed( "reload" );

	/// <summary>
	/// Can primary fire right now? Base checks the cooldown, that we're not mid-reload, and that
	/// there's ammo. A pure check, safe to call from HUDs - the trigger dry-fires on empty, not this.
	/// Override to add conditions.
	/// </summary>
	public virtual bool CanPrimaryAttack()
	{
		if ( NextPrimaryFire > 0 ) return false;
		if ( IsReloading ) return false;
		if ( !HasPrimaryAmmo() ) return false;

		return true;
	}

	/// <inheritdoc cref="CanPrimaryAttack"/>
	public virtual bool CanSecondaryAttack()
	{
		if ( NextSecondaryFire > 0 ) return false;
		if ( IsReloading ) return false;
		if ( !HasSecondaryAmmo() ) return false;

		return true;
	}

	/// <summary>
	/// Pull the primary trigger - fires if <see cref="CanPrimaryAttack"/> allows, putting it on
	/// cooldown. Returns whether it fired. The way to shoot from code (AI, turrets) - calling
	/// <see cref="PrimaryAttack"/> directly skips the fire rate. Runs on whoever controls the
	/// weapon; the attack doesn't re-run on the host.
	/// </summary>
	public bool FirePrimary()
	{
		if ( !CanPrimaryAttack() ) return false;

		SetNextPrimaryFire( PrimaryDelay );
		PrimaryAttack();
		return true;
	}

	/// <inheritdoc cref="FirePrimary"/>
	public bool FireSecondary()
	{
		if ( !CanSecondaryAttack() ) return false;

		SetNextSecondaryFire( SecondaryDelay );
		SecondaryAttack();
		return true;
	}

	/// <summary>
	/// Fire the primary attack. The default fires <see cref="Ballistics"/> - spends a round, shoots
	/// the volley from <see cref="AimRay"/> with <see cref="CurrentSpread"/>, and plays the effects
	/// on every peer. Override for melee, projectiles and tools. Runs once, on whoever controls the
	/// weapon - the owning client for a held weapon (its hits reach the host as claims, see
	/// <see cref="ShootBullet(SceneTraceResult,float,float,TagSet,bool)"/>), or the host for
	/// seat/NPC/world weapons (damage applies directly).
	/// </summary>
	public virtual void PrimaryAttack()
	{
		// Unheld weapons (seats, NPCs, world) have no ammo pool to spend from.
		if ( IsHeld && !TakePrimaryAmmo() )
			return;

		var config = Ballistics;
		var pellets = ShootBullets( config.Pellets, CurrentSpread, config.Range, config.Radius, config.Damage, config.Force );

		TimeSinceShoot = 0;

		// The first pellet carries the muzzle/anim/sound events - every pellet still flies its
		// tracer and leaves its impact. The volley relays across the network as one message.
		var volley = new ShotEffect[pellets.Length];

		for ( var i = 0; i < pellets.Length; i++ )
		{
			var tr = pellets[i];
			volley[i] = new ShotEffect( tr.EndPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface, NoEvents: i > 0 );
		}

		ShootEffects( volley );
	}

	/// <summary>
	/// Fire the secondary attack. Base does nothing - override it. Same contract as
	/// <see cref="PrimaryAttack"/>: runs once, on whoever controls the weapon.
	/// </summary>
	public virtual void SecondaryAttack() { }

	/// <summary>
	/// The player pulled the trigger with no ammo. Base plays <see cref="DryFireSound"/>, throttles
	/// both triggers so it doesn't spam, and starts a reload when <see cref="AutoReload"/> allows.
	/// Override to extend.
	/// </summary>
	public virtual void DryFire()
	{
		if ( DryFireSound is not null )
			GameObject.PlaySound( DryFireSound );

		SetNextFire( 0.15f );

		if ( AutoReload && CanReload() )
			Reload();
	}
}
