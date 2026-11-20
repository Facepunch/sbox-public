[Title( "SG-5" )]
public sealed class Mp5Weapon : BaseBulletWeapon
{
	/// <summary>
	/// The round the launcher fires - needs an <see cref="ImpactGrenade"/>.
	/// </summary>
	[Property, Feature( "Grenade Launcher" )] public GameObject GrenadePrefab { get; set; }

	/// <summary>
	/// Launch speed of the grenade.
	/// </summary>
	[Property, Feature( "Grenade Launcher" )] public float GrenadeSpeed { get; set; } = 1500f;

	/// <summary>
	/// Reserve grenades granted on first pickup - reloads top the launcher's magazine up from these.
	/// </summary>
	[Property, Feature( "Grenade Launcher" )] public int StartingGrenades { get; set; } = 6;

	protected override void OnAdded( Sandbox.BaseInventoryComponent inventory )
	{
		// Seed the grenade reserve once, like the bullet reserve.
		if ( StartingGrenades > 0 && SecondaryAmmoType is not null && !inventory.HasAmmo( SecondaryAmmoType ) )
			inventory.GiveAmmo( SecondaryAmmoType, StartingGrenades );

		base.OnAdded( inventory );
	}

	public override void SecondaryAttack()
	{
		if ( !TakeSecondaryAmmo() )
			return;

		ShootEffects();

		// The attack runs once on the owner - LaunchGrenade is a host RPC, so the grenade spawns
		// exactly once, on the host.
		LaunchGrenade( GetMuzzleTransform().Position + AimRay.Forward * 16f, AimRay.Forward );
	}

	/// <summary>
	/// Spawns and launches the grenade with the host's permission.
	/// </summary>
	[Rpc.Host]
	void LaunchGrenade( Vector3 start, Vector3 direction )
	{
		if ( !GrenadePrefab.IsValid() )
			return;

		var go = GrenadePrefab.Clone( new Transform( start, Rotation.LookAt( direction, Vector3.Up ) ) );

		var grenade = go.GetComponent<ImpactGrenade>();
		Assert.True( grenade.IsValid(), "ImpactGrenade not on grenade prefab" );

		if ( Owner.IsValid() )
			grenade.Instigator = Owner;
		else if ( ControlContext.Player.IsValid() )
			grenade.Instigator = ControlContext.Player;

		grenade.Rigidbody.Velocity = direction * GrenadeSpeed;

		// Don't collide with the weapon it launched from
		var filter = go.AddComponent<PhysicsFilter>();
		filter.Body = GameObject;

		go.NetworkSpawn();
	}

	/// <summary>
	/// Draws the crosshair arms at the current spread and firing status.
	/// </summary>
	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		using var scope = painter.Scope();
		var gap = 16 + SpreadBloom * 32;
		var len = 12;
		var w = 2f;

		var color = !HasPrimaryAmmo() || IsReloading || NextPrimaryFire > 0 ? CrosshairNoShoot : CrosshairCanShoot;

		painter.BlendMode = BlendMode.Lighten;
		painter.Stroke = Stroke.Solid( color, w );
		painter.Line( center + Vector2.Left * (len + gap), center + Vector2.Left * gap );
		painter.Line( center - Vector2.Left * (len + gap), center - Vector2.Left * gap );
		painter.Line( center + Vector2.Up * (len + gap), center + Vector2.Up * gap );
		painter.Line( center - Vector2.Up * (len + gap), center - Vector2.Up * gap );
	}
}
