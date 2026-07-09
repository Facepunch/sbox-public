using Sandbox.Rendering;

namespace Sandbox;

public partial class BaseCombatWeapon
{
	//
	// Weapon HUD, modelled on GMod's DoDrawCrosshair. Every frame the holding client gives the weapon
	// a chance to draw - crosshair, ammo, hints. Pure presentation, local player only.
	//

	/// <summary>Crosshair colour for a weapon that's ready to fire.</summary>
	protected Color CrosshairCanShoot => Color.White;

	/// <summary>Crosshair colour for a weapon that can't fire - empty, reloading, cooling down.</summary>
	protected Color CrosshairNoShoot => Color.Red;

	protected override void OnUpdate()
	{
		// Owner walks the hierarchy - resolve it once for the frame.
		var owner = Owner;

		if ( owner?.Renderer is { } body && body.IsValid() )
			UpdateBodyAnimations( body );

		// The HUD draws for the local player holding this weapon. Unheld weapons don't draw - a game
		// calls DrawHud itself for those (e.g. mounted weapons, from its seat camera).
		if ( IsProxy || !owner.IsValid() )
			return;

		DrawHud( Scene.Camera );
	}

	/// <summary>
	/// Draw this weapon's HUD on the given camera - the crosshair goes at screen centre when the
	/// camera sits on the aim ray, otherwise (third person, mounted weapons) at the projected aim
	/// point. Games call this for weapons they draw manually, e.g. seat-controlled ones.
	/// </summary>
	public void DrawHud( CameraComponent camera )
	{
		if ( !camera.IsValid() )
			return;

		// The UI is hidden - screenshots, cinematics.
		if ( camera.RenderExcludeTags.Has( "ui" ) )
			return;

		var owner = Owner;
		var aimPos = Screen.Size * 0.5f;

		// When the camera isn't on the aim ray, project the point we're actually aiming at.
		if ( !owner.IsValid() || owner.ThirdPerson )
		{
			var tr = Scene.Trace.Ray( AimRay, 4096f )
				.IgnoreGameObjectHierarchy( owner?.GameObject ?? GameObject.Root )
				.Run();

			aimPos = camera.PointToScreenPixels( tr.EndPosition );
		}

		DrawHud( camera.Hud, aimPos );
	}

	/// <summary>
	/// Draw this weapon's HUD - called every frame on the client holding it, with the crosshair
	/// position (screen centre, or the projected aim point in third person). Base draws the crosshair
	/// via <see cref="DrawCrosshair"/>; override to add ammo counts, hints or scopes.
	/// </summary>
	public virtual void DrawHud( HudPainter painter, Vector2 crosshair )
	{
		DrawCrosshair( painter, crosshair );
	}

	/// <summary>
	/// Draw this weapon's crosshair at the aim position. Base draws a simple four-line cross.
	/// </summary>
	public virtual void DrawCrosshair( HudPainter hud, Vector2 center )
	{
		var color = CanPrimaryAttack() ? CrosshairCanShoot : CrosshairNoShoot;

		hud.DrawLine( center + Vector2.Left * 32, center + Vector2.Left * 15, 3, color );
		hud.DrawLine( center - Vector2.Left * 32, center - Vector2.Left * 15, 3, color );
		hud.DrawLine( center + Vector2.Up * 32, center + Vector2.Up * 15, 3, color );
		hud.DrawLine( center - Vector2.Up * 32, center - Vector2.Up * 15, 3, color );
	}
}
