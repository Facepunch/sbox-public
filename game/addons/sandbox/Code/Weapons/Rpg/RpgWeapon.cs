using Sandbox.Utility;

[Title( "Ashford R-7" )]
public sealed class RpgWeapon : BaseSandboxWeapon
{
	[Property] public GameObject ProjectilePrefab { get; set; }
	[Property] public float ProjectileSpeed { get; set; } = 1024f;

	/// <summary>
	/// When enabled, fired rockets will continuously track toward the player's crosshair.
	/// Toggle with right-click (player) or SecondaryInput (standalone/seat).
	/// </summary>
	// Host-authoritative: toggled through the [Rpc.Host] ToggleTrackedAim (from the owner when held, or on
	// the host directly for a seated RPG), so it must replicate FROM the host, not owner->everyone.
	[Property, Sync( SyncFlags.FromHost ), ClientEditable, Title( "#weapon.setting.guide_rockets" ), Description( "#weapon.setting.guide_rockets.description" )]
	public bool IsTrackedAim { get; set; } = false;

	public override bool IsTargetedAim => IsTrackedAim;

	[Sync( SyncFlags.FromHost )] RpgProjectile Projectile { get; set; }

	private bool _hasFired;
	private bool _waitingForReload;

	/// <summary>
	/// Whether a live rocket is currently being guided toward the crosshair.
	/// </summary>
	public bool IsGuiding => IsTrackedAim && Projectile.IsValid();

	public override bool CanSecondaryAttack() => false;

	protected override void OnControl()
	{
		base.OnControl();

		if ( Input.Pressed( "attack2" ) )
			ToggleTrackedAim();

		// Auto-reload after firing
		if ( _hasFired && Input.Released( "attack1" ) )
		{
			_hasFired = false;

			if ( IsGuiding )
				_waitingForReload = true;
			else if ( CanReload() )
				Reload();
		}

		if ( IsGuiding )
		{
			var target = GetAimTarget();
			Projectile.UpdateWithTarget( target, ProjectileSpeed );
		}
		else if ( _waitingForReload )
		{
			_waitingForReload = false;
			if ( CanReload() )
				Reload();
		}
	}

	/// <summary>
	/// Standalone / seat control — uses SecondaryInput to toggle tracking.
	/// </summary>
	protected override void OnPrimaryDown()
	{
		base.OnPrimaryDown();
		UpdateGuidance();
	}

	protected override void OnSecondaryPressed() => ToggleTrackedAim();

	protected override void OnSecondaryDown()
	{
		UpdateGuidance();
	}

	private void UpdateGuidance()
	{
		if ( IsGuiding )
		{
			var target = GetAimTarget();
			Projectile.UpdateWithTarget( target, ProjectileSpeed );
		}
	}

	[Rpc.Host]
	private void ToggleTrackedAim()
	{
		IsTrackedAim = !IsTrackedAim;
	}

	/// <summary>
	/// Traces from AimRay and returns the world-space point the player is looking at.
	/// </summary>
	private Vector3 GetAimTarget()
	{
		var ray = AimRay;
		var tr = Scene.Trace.Ray( ray, 16384f )
			.IgnoreGameObjectHierarchy( AimIgnoreRoot )
			.WithoutTags( "trigger", "projectile" )
			.Run();

		return tr.Hit ? tr.HitPosition : ray.Position + ray.Forward * 16384f;
	}

	public override void PrimaryAttack()
	{
		if ( HasOwner && !TakePrimaryAmmo( 1 ) )
		{
			DryFire();
			return;
		}

		TimeSinceShoot = 0;
		SetNextFire( PrimaryDelay );

		// Fire presentation - predicted on the owner for instant feedback, relayed by the host to everyone
		// else. See OnShootEffects.
		ShootEffects();

		var ray = AimRay;
		var muzzlePos = GetMuzzleTransform().Position;
		var spawnPos = muzzlePos + ray.Forward * 64f;

		if ( HasOwner )
		{
			spawnPos = CheckThrowPosition( Owner, muzzlePos, spawnPos );

			if ( KickView() )
				_hasFired = true;
		}

		// The attack runs once on the owner - CreateProjectile is a host RPC, so the rocket spawns
		// exactly once, on the host.
		CreateProjectile( spawnPos, ray.Forward, ProjectileSpeed );
	}

	/// <summary>
	/// Muzzle flash + launch sound. Plays for the shooter (predicted) and, via the host relay, everyone else.
	/// </summary>

	private Vector3 CheckThrowPosition( Player player, Vector3 eyePosition, Vector3 grenadePosition )
	{
		var tr = Scene.Trace.Box( BBox.FromPositionAndSize( Vector3.Zero, 8.0f ), eyePosition, grenadePosition )
			.WithoutTags( "trigger", "ragdoll", "player", "effect" )
			.IgnoreGameObjectHierarchy( player.GameObject )
			.Run();

		if ( tr.Hit )
			return tr.EndPosition;

		return grenadePosition;
	}

	/// <summary>
	/// Creates the projectile with the host's permission
	/// </summary>
	[Rpc.Host]
	void CreateProjectile( Vector3 start, Vector3 direction, float speed )
	{
		var go = ProjectilePrefab?.Clone( start );

		var projectile = go.GetComponent<RpgProjectile>();
		Assert.True( projectile.IsValid(), "RpgProjectile not on projectile prefab" );

		if ( Owner.IsValid() )
			projectile.Instigator = Owner;
		else if ( ControlContext.Player.IsValid() )
			projectile.Instigator = ControlContext.Player;

		go.NetworkSpawn();

		Projectile = projectile;
		projectile.UpdateDirection( direction, speed );
	}

	/// <summary>
	/// Draws a diamond for guided aim or a square for an unguided rocket.
	/// </summary>
	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		using var scope = painter.Scope();
		var w = 2;

		painter.BlendMode = BlendMode.Lighten;
		painter.Fill = Fill.None;

		if ( IsTrackedAim )
		{
			// Diamond crosshair when in tracked aim mode
			Color guideColor = IsGuiding ? new Color( 1f, 0.5f, 0.1f ) : CrosshairCanShoot;
			var size = 32f;

			painter.Stroke = Stroke.Solid( guideColor, w );
			painter.Quad(
				center + new Vector2( 0, -size ),
				center + new Vector2( size, 0 ),
				center + new Vector2( 0, size ),
				center + new Vector2( -size, 0 ) );

			return;
		}

		Color color = !CanPrimaryAttack() ? CrosshairNoShoot : CrosshairCanShoot;

		var squareSize = 64f;

		painter.Stroke = Stroke.Solid( color, w );
		painter.Rect( new Rect( center - new Vector2( squareSize / 2 ), new Vector2( squareSize ) ) );
	}
}
