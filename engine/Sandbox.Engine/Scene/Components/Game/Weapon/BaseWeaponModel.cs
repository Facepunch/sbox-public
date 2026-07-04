namespace Sandbox;

/// <summary>
/// Marks up a weapon's view or world model with the attachment points the weapon fires from - the
/// muzzle, the shell-eject port, and so on - and plays the stock presentation off them: muzzle
/// flash, brass and tracer prefabs, plus the b_deploy/b_attack/b_reload animation parameters. Drop
/// this on a weapon model prefab, wire the attachment GameObjects, and override the On* hooks to
/// extend or replace any of it. The holding <see cref="BaseWeapon"/> drives it through those hooks.
/// </summary>
[Title( "Weapon Model" )]
[Group( "Game" )]
[Icon( "precision_manufacturing" )]
public partial class BaseWeaponModel : Component
{
	/// <summary>
	/// The skinned model renderer for this weapon model.
	/// </summary>
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// Sound played when this weapon model is deployed (drawn).
	/// </summary>
	[Property] public SoundEvent DeploySound { get; set; }

	/// <summary>
	/// The muzzle attachment - where shots and muzzle effects originate.
	/// </summary>
	[Property] public GameObject MuzzleGameObject { get; set; }

	/// <summary>
	/// The shell-eject attachment - where spent casings are thrown from.
	/// </summary>
	[Property] public GameObject ShellEjectGameObject { get; set; }

	/// <summary>
	/// The muzzle world transform, or the model's own transform when there's no muzzle attachment.
	/// </summary>
	public Transform GetMuzzleTransform()
		=> MuzzleGameObject.IsValid() ? MuzzleGameObject.WorldTransform : WorldTransform;

	/// <summary>
	/// The shell-eject world transform, or the model's own transform when there's no eject attachment.
	/// </summary>
	public Transform GetShellEjectTransform()
		=> ShellEjectGameObject.IsValid() ? ShellEjectGameObject.WorldTransform : WorldTransform;

	/// <summary>
	/// Effect prefab spawned at the muzzle when the weapon fires (muzzle flash, smoke). Defaults to
	/// a generic flash - clear it for none.
	/// </summary>
	[Property] public GameObject MuzzleEffect { get; set; } = GameObject.GetPrefab( "prefabs/effects/default_muzzleflash.prefab" );

	/// <summary>
	/// Effect prefab spawned at the shell-eject port when the weapon fires (the flying brass).
	/// Defaults to a generic casing - clear it for none.
	/// </summary>
	[Property] public GameObject EjectBrass { get; set; } = GameObject.GetPrefab( "prefabs/effects/default_brasseject.prefab" );

	/// <summary>
	/// Effect prefab spawned from the muzzle toward the hit point (the bullet tracer). Defaults to
	/// a generic tracer - clear it for none.
	/// </summary>
	[Property] public GameObject TracerEffect { get; set; } = GameObject.GetPrefab( "prefabs/effects/default_tracer.prefab" );

	/// <summary>
	/// Spawns <see cref="MuzzleEffect"/> parented to the muzzle attachment, if both are set.
	/// </summary>
	public void DoMuzzleEffect()
	{
		if ( !MuzzleEffect.IsValid() ) return;
		if ( !MuzzleGameObject.IsValid() ) return;

		MuzzleEffect.Clone( new CloneConfig { Parent = MuzzleGameObject, Transform = global::Transform.Zero, StartEnabled = true } );
	}

	/// <summary>
	/// Spawns <see cref="EjectBrass"/> at the shell-eject attachment and throws it out, if both are set.
	/// </summary>
	public void DoEjectBrass()
	{
		if ( !EjectBrass.IsValid() ) return;
		if ( !ShellEjectGameObject.IsValid() ) return;

		var effect = EjectBrass.Clone( new CloneConfig { Transform = ShellEjectGameObject.WorldTransform.WithScale( 1 ), StartEnabled = true } );
		effect.WorldRotation = effect.WorldRotation * new Angles( 90, 0, 0 );

		var ejectDirection = (ShellEjectGameObject.WorldRotation.Forward * 250 + (ShellEjectGameObject.WorldRotation.Right + Vector3.Random * -0.35f) * 250);

		var rb = effect.GetComponentInChildren<Rigidbody>();
		if ( !rb.IsValid() )
			return;

		rb.Velocity = ejectDirection;
		rb.AngularVelocity = ShellEjectGameObject.WorldRotation.Right * 50f;
	}

	/// <summary>
	/// The point a tracer starts from - the muzzle.
	/// </summary>
	public Transform GetTracerOrigin() => GetMuzzleTransform();

	/// <summary>
	/// Spawns <see cref="TracerEffect"/> from the muzzle and aims it at <paramref name="hitPoint"/>.
	/// The spawned effect's <see cref="ITargetedEffect"/> (if any) is given the target.
	/// </summary>
	public void DoTracerEffect( Vector3 hitPoint, Vector3? origin = null )
	{
		if ( !TracerEffect.IsValid() ) return;

		var tracerOrigin = GetTracerOrigin().WithScale( 1 );
		if ( origin.HasValue ) tracerOrigin = tracerOrigin.WithPosition( origin.Value );

		var effect = TracerEffect.Clone( new CloneConfig { Transform = tracerOrigin, StartEnabled = true } );

		if ( effect.GetComponentInChildren<ITargetedEffect>() is { } targeted )
			targeted.SetTarget( hitPoint );
	}

	/// <summary>
	/// Called when this weapon model is deployed (drawn). Base sets the "b_deploy" animation parameter
	/// and plays <see cref="DeploySound"/>. Override to extend.
	/// </summary>
	public virtual void OnDeploy()
	{
		Renderer?.Set( "b_deploy", true );

		if ( DeploySound is not null )
			GameObject.PlaySound( DeploySound );
	}

	/// <summary>
	/// Called when the weapon attacks - once per trigger pull. Base sets the "b_attack" animation
	/// parameter and spawns the muzzle flash and ejected brass. Tracers are per-pellet, fired
	/// separately through <see cref="DoTracerEffect"/>. Override to extend.
	/// </summary>
	/// <param name="hitPoint">Where the shot landed - null for attacks with no ranged hit (e.g. melee).</param>
	/// <param name="origin">Where the shot originated, when it wasn't the muzzle.</param>
	public virtual void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		Renderer?.Set( "b_attack", true );

		DoMuzzleEffect();
		DoEjectBrass();
	}

	/// <summary>
	/// Called when the weapon starts reloading. Base sets the "b_reload" animation parameter. Override to
	/// extend (incremental animations, timed reload sounds).
	/// </summary>
	public virtual void OnReloadStart()
	{
		Renderer?.Set( "b_reload", true );
	}

	/// <summary>
	/// Called when a round is loaded during an incremental (one-at-a-time) reload. Base does nothing -
	/// override for the per-shell animation.
	/// </summary>
	public virtual void OnIncrementalReload() { }

	/// <summary>
	/// Called when the reload finishes. Base clears the "b_reload" animation parameter. Override to extend.
	/// </summary>
	public virtual void OnReloadFinish()
	{
		Renderer?.Set( "b_reload", false );
	}

	/// <summary>
	/// Called when a reload is cancelled part-way through. Base does nothing - override to stop timed
	/// sounds and so on.
	/// </summary>
	public virtual void OnReloadCancel() { }
}
