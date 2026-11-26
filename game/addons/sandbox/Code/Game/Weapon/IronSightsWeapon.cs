/// <summary>
/// A weapon that can aim down sights
/// </summary>
public abstract class IronSightsWeapon : BaseBulletWeapon
{
	/// <summary>
	/// Lowers the amount of recoil / visual noise when aiming
	/// </summary>
	[Property] public float IronSightsFireScale { get; set; } = 0.2f;

	/// <summary>
	/// Multiplier for the player's FOV while aiming in first person. 1 disables zoom.
	/// </summary>
	[Property, Group( "Iron Sights" ), Title( "Aim FOV Scale" ), Range( 0.1f, 1f )]
	public float AimFovScale { get; set; } = 0.8f;

	/// <summary>
	/// Seconds to blend between normal and aimed FOV. 0 switches instantly.
	/// </summary>
	[Property, Group( "Iron Sights" ), Title( "Aim FOV Transition Time" ), Range( 0f, 1f )]
	public float AimFovTransitionTime { get; set; } = 0.15f;

	/// <summary>
	/// View-space offset blended in while aiming, for lining up sights that don't match the
	/// animation's aim pose (e.g. raised sights). X forward, Y left, Z up.
	/// </summary>
	[Property, Group( "Iron Sights" ), Title( "Aim Offset" )]
	public Vector3 AimOffset { get; set; }

	private float _aimFovBlend;

	protected override Vector3 ViewModelOffset => AimOffset * _aimFovBlend;

	// Synced (owner-authoritative) so the host's authoritative shot trace sees it too - otherwise ADS
	// only narrows the cone on the owner's predicted (non-damaging) pass and the real shot stays wide.
	[Sync] public bool IsAiming { get; set; }

	public override bool CanSecondaryAttack() => false;

	/// <summary>
	/// Draws the crosshair while the weapon is not aiming down sights.
	/// </summary>
	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( IsAiming ) return;

		base.DrawHud( painter, crosshair );
	}

	protected override void OnControl()
	{
		base.OnControl();

		var wantsAim = Input.Down( "attack2" ) && !IsReloading;

		if ( wantsAim == IsAiming )
			return;

		IsAiming = wantsAim;
		ViewModel?.RunEvent<ViewModel>( x =>
		{
			x.Renderer?.Animation.Parameters.TrySet( "ironsights", IsAiming );
			x.Renderer?.Animation.Parameters.TrySet( "ironsights_fire_scale", IsAiming ? IronSightsFireScale : 1f );
		} );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		IsAiming = false;
		_aimFovBlend = 0f;
	}

	protected override void OnHolstered()
	{
		base.OnHolstered();
		IsAiming = false;
		_aimFovBlend = 0f;
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( HasOwner && Owner.Controller.IsValid() && !Owner.Controller.ThirdPerson )
		{
			_aimFovBlend = AimFovTransitionTime <= 0f
				? (IsAiming ? 1f : 0f)
				: (_aimFovBlend + (IsAiming ? 1f : -1f) * Time.Delta / AimFovTransitionTime).Clamp( 0f, 1f );

			view.FieldOfView *= MathX.Lerp( 1f, AimFovScale.Clamp( 0.1f, 1f ), _aimFovBlend );
		}
		else
		{
			_aimFovBlend = 0f;
		}

		base.ModifyCamera( camera, ref view );
	}

	/// <summary>
	/// Aiming down sights narrows the spread cone.
	/// </summary>
	public override Vector2 CurrentSpread => base.CurrentSpread * (IsAiming ? IronSightsFireScale : 1f);
}
