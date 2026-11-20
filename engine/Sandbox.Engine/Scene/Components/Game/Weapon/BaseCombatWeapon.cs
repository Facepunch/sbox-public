namespace Sandbox;

/// <summary>
/// The base for anything a player holds and uses - weapons, tools, cameras, and so on. Adds a
/// first-person view model, a third-person world model attached to the holder's hand, and an aim ray
/// on top of <see cref="BaseInventoryItem"/>. Combat is opt-in: subclass this, override the attack,
/// and call the utility methods (ShootBullet, ShootBullets, ...) - they do the host-authoritative
/// tracing and damage for you. Non-combat items just don't call them.
/// </summary>
/// <remarks>
/// The weapon doesn't animate its holder: it advertises how it's held (<see cref="HoldType"/>,
/// optionally <see cref="HoldFallback"/>) and posts <see cref="IEvents"/> for what it does. The
/// holder's animator maps those onto whatever its model has.
/// </remarks>
[Title( "Weapon" )]
[Group( "Game" )]
[Icon( "sports_martial_arts" )]
[Alias( "Sandbox.BaseWeapon" )]
public partial class BaseCombatWeapon : BaseInventoryItem
{
	/// <summary>
	/// The player controller holding this, or null if it isn't held by one (it's in the world, or
	/// held by something that isn't a <see cref="PlayerController"/>). Derived from the hierarchy.
	/// </summary>
	public PlayerController Owner => GetComponentInParent<PlayerController>( true );

	/// <summary>
	/// Whoever holds this: the nearest <see cref="IHolder"/> above it in the hierarchy, a player or
	/// anything else that carries weapons. Null when it isn't held.
	/// </summary>
	public IHolder Holder => GetComponentInParent<IHolder>( true );

	/// <summary>
	/// True while something holds this, a player or any other <see cref="IHolder"/>.
	/// </summary>
	public bool IsHeld => Holder is not null;

	/// <summary>
	/// The body renderer the world model attaches to - the holder's renderer.
	/// </summary>
	public SkinnedModelRenderer HolderRenderer => Holder?.Renderer;

	/// <summary>
	/// Where this weapon is aiming: where its <see cref="Holder"/> aims it. For a player that is the
	/// eye-forward, or the camera-forward in third person. When not held it defers to <see cref="UnheldAimRay"/>.
	/// </summary>
	public virtual Ray AimRay => Holder is { } holder ? holder.CalculateAimRay( this ) : UnheldAimRay;

	/// <summary>
	/// The aim ray to use when nothing holds this weapon - placed in the world, mounted, or
	/// controlled from a seat. Base fires from the muzzle; override for camera-targeted aim.
	/// </summary>
	protected virtual Ray UnheldAimRay
	{
		get
		{
			var muzzle = GetMuzzleTransform();
			return new Ray( muzzle.Position, muzzle.Rotation.Forward );
		}
	}

	/// <summary>
	/// The <see cref="BaseWeaponModel"/> on the currently-shown model - the view model when it's being
	/// drawn, otherwise the world model, otherwise the weapon's own hierarchy (standalone weapons).
	/// Null when none carries one.
	/// </summary>
	public BaseWeaponModel WeaponModel
	{
		get
		{
			var go = ViewModel;

			// The view model still exists in third person, it's just not drawn - effects (muzzle
			// flash, tracers) should come from the world model's muzzle instead.
			if ( Scene.Camera.IsValid() && Scene.Camera.RenderExcludeTags.Has( "firstperson" ) )
				go = null;

			if ( !go.IsValid() )
				go = WorldModel;

			if ( go.IsValid() && go.GetComponentInChildren<BaseWeaponModel>() is { } model )
				return model;

			// Standalone weapons may carry a model in their own hierarchy without spawning
			// view/world models.
			return GameObject.GetComponentInChildren<BaseWeaponModel>();
		}
	}

	/// <summary>
	/// The transform that shots and muzzle effects fire from. Resolves the active weapon model's
	/// muzzle attachment, then the weapon itself.
	/// Override to add other model-driven resolution.
	/// </summary>
	public virtual Transform GetMuzzleTransform()
	{
		var model = WeaponModel;
		if ( model.IsValid() && model.MuzzleGameObject.IsValid() )
			return model.GetMuzzleTransform();

		return WorldTransform;
	}

	/// <summary>
	/// Drive the holder's body animation for this weapon. Runs every frame while deployed, on every
	/// peer, with the holding player's renderer. Base has the player's animator set the hold and play the
	/// pending deploy, attack and reload gestures; override without calling it to drive the body yourself.
	/// The engine only calls this for a player. Called with any other body, by a game that holds the weapon
	/// its own way, base sets <see cref="HoldType"/>, <see cref="Handedness"/> and the pending gestures on that
	/// body directly.
	/// </summary>
	protected virtual void UpdateBodyAnimations( SkinnedModelRenderer body )
	{
		if ( Holder is PlayerController player )
		{
			player.UpdateWeaponAnimation( this, body );
			return;
		}

		if ( body.Animation.Graph is not null )
		{
			// Not running yet: the gestures wait for it.
			if ( !body.Animation.IsValid )
				return;

			var parameters = body.Animation.Parameters;
			parameters.TrySet( "b_attack", (_pendingGestures & PendingGestures.Attack) != 0 );
			parameters.TrySet( "b_reload", (_pendingGestures & PendingGestures.Reload) != 0 );
			parameters.TrySet( "b_deploy", (_pendingGestures & PendingGestures.Deploy) != 0 );
			parameters.TrySet( "b_reload_cancel", (_pendingGestures & PendingGestures.ReloadCancel) != 0 );
		}

		ClearPendingGestures();

		if ( string.IsNullOrEmpty( HoldType ) )
			return;

		if ( body.Animation.Graph is not null )
		{
			body.Animation.Parameters.TrySet( "holdtype", HoldType );
			body.Animation.Parameters.TrySet( "holdtype_handedness", (int)Handedness );
			return;
		}

#pragma warning disable CS0618
		body.Set( "holdtype", HoldType );
		body.Set( "holdtype_handedness", (int)Handedness );
#pragma warning restore CS0618
	}

	protected override void OnEquipped()
	{
		base.OnEquipped();

		CreateWorldModel();
		CreateViewModel();

		// Can't fire for a moment after switching to the weapon.
		SetNextPrimaryFire( DeployTime );
		SetNextSecondaryFire( DeployTime );

		// Last: a listener may switch away from this or destroy it.
		PostWeaponEvent( WeaponEvents.Deploy );
	}

	protected override void OnHolstered()
	{
		base.OnHolstered();

		CancelReload();

		DestroyWorldModel();
		DestroyViewModel();

		// Last: a listener may take this out again or destroy it.
		PostWeaponEvent( WeaponEvents.Holster );
	}
}
