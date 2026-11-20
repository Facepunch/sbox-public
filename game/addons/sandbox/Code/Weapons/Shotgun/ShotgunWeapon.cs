[Title( "Spaghelli M4" )]
public sealed class ShotgunWeapon : IronSightsWeapon
{
	// The volley (pellet count, spread, damage) comes from the engine Ballistics; recoil from
	// BaseBulletWeapon.

	protected override bool WantsPrimaryAttack()
	{
		return Input.Pressed( "attack1" );
	}

	/// <summary>
	/// Draws a smooth spread ring and aiming dot with the current firing status.
	/// </summary>
	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		using var scope = painter.Scope();
		var spread = SpreadBloom;
		var radius = 20 + spread * 40;

		var color = !HasPrimaryAmmo() || IsReloading || NextPrimaryFire > 0 ? CrosshairNoShoot : CrosshairCanShoot;

		painter.BlendMode = BlendMode.Lighten;
		painter.Fill = Fill.None;
		painter.Stroke = Stroke.Solid( color, 2f );
		painter.Circle( center, radius );

		painter.Fill = color;
		painter.Stroke = Stroke.None;
		painter.Circle( center, 1.5f );
	}
}
