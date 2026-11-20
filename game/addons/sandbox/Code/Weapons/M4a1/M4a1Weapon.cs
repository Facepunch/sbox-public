/// <summary>
/// M4 assault rifle.
/// </summary>
[Title( "AR-4" )]
public sealed class M4a1Weapon : IronSightsWeapon
{
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
