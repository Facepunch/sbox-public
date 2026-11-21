[Title( "M700" )]
public sealed class SniperWeapon : BaseBulletWeapon
{
	[Property] public float ScopedFov { get; set; } = 20f;
	[Property] public float ScopeSensitivity { get; set; } = 0.3f;
	[Property] public SoundEvent BoltPullSound { get; set; }

	private bool _isScoped;
	private float _mouseDelta;
	private SniperScopeEffect _scopeEffect;
	private bool _hasFired;
	private TimeUntil _timeUntilHideViewModel;
	private bool _viewModelHidden;

	public bool IsScoped => _isScoped;

	/// <summary>
	/// The scope is something you look through with your own eyes: only in first person, and not while the screen
	/// shows a camera entity instead (its view goes through the same main camera, so the overlay would cover it).
	/// </summary>
	private bool CanLookThroughScope => HasOwner
		&& Owner.Controller.IsValid() && !Owner.Controller.ThirdPerson
		&& CameraViewSystem.Get( Scene )?.ActiveCamera.IsValid() != true;

	protected override bool WantsPrimaryAttack()
	{
		return Input.Pressed( "attack1" );
	}

	protected override bool WantsSecondaryAttack()
	{
		return false;
	}

	public override bool CanSecondaryAttack()
	{
		return false;
	}

	public override void PrimaryAttack()
	{
		base.PrimaryAttack();
		_hasFired = true;
	}

	private void SetScoped( bool scoped )
	{
		_isScoped = scoped;

		// Trigger ironsights animation
		ViewModel?.RunEvent<ViewModel>( x =>
		{
			x.Renderer?.Set( "ironsights", _isScoped ? 1 : 0 );
		} );

		if ( _isScoped )
		{
			// Delay hiding the viewmodel until the ADS animation finishes
			_timeUntilHideViewModel = 0.2f;
		}
		else
		{
			ShowViewModel();

			_scopeEffect?.Destroy();
			_scopeEffect = default;
		}
	}

	private void HideViewModel()
	{
		if ( _viewModelHidden ) return;
		_viewModelHidden = true;

		if ( ViewModel.IsValid() )
			Scene.Camera.RenderExcludeTags.Add( "sniper_scoped" );

		if ( ViewModel.IsValid() )
			ViewModel.Tags.Add( "sniper_scoped" );
	}

	private void ShowViewModel()
	{
		if ( !_viewModelHidden ) return;
		_viewModelHidden = false;

		Scene.Camera.RenderExcludeTags.Remove( "sniper_scoped" );

		if ( ViewModel.IsValid() )
			ViewModel.Tags.Remove( "sniper_scoped" );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		if ( _isScoped )
			SetScoped( false );

		ShowViewModel();
	}

	protected override void OnControl()
	{
		base.OnControl();

		// Hold right mouse to scope, except while the reload animation owns the view model.
		var wantsScope = Input.Down( "attack2" ) && !IsReloading;
		if ( wantsScope != _isScoped )
		{
			SetScoped( wantsScope );
		}

		// Hide viewmodel once ADS animation has finished, then enable scope overlay
		var canLookThrough = CanLookThroughScope;
		if ( _isScoped && canLookThrough && !_viewModelHidden && _timeUntilHideViewModel )
		{
			HideViewModel();

			_scopeEffect = Scene.Camera.Components.GetOrCreate<SniperScopeEffect>();
			_scopeEffect.Flags |= ComponentFlags.NotNetworked;
		}
		// Switched to third person or a camera view while scoped: drop the overlay and zoom, keep holding scope so
		// it comes straight back
		else if ( _viewModelHidden && !canLookThrough )
		{
			ShowViewModel();

			_scopeEffect?.Destroy();
			_scopeEffect = default;
		}

		if ( _hasFired && Input.Released( "attack1" ) )
		{
			_hasFired = false;

			if ( BoltPullSound is not null )
				Sound.Play( BoltPullSound, WorldPosition );

			ViewModel?.RunEvent<ViewModel>( x =>
			{
				x.Renderer?.Set( "speed_reload", 1 );
				x.Renderer?.Set( "b_reload_bolt", true );
			} );
		}
	}

	/// <summary>
	/// Scoped zoom goes through the engine's camera modifier chain - one owner for the FOV, no
	/// fighting the player's preference FOV write.
	/// </summary>
	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( _isScoped && _viewModelHidden )
		{
			view.FieldOfView = ScopedFov;
		}

		// The base drives the view model's camera bone and placement.
		base.ModifyCamera( camera, ref view );
	}

	public override void OnCameraMove( Player player, ref Angles angles )
	{
		_mouseDelta = new Vector2( angles.yaw, angles.pitch ).Length;

		if ( _isScoped && _viewModelHidden )
		{
			angles *= ScopeSensitivity;
		}
	}

	/// <summary>
	/// Updates the scope overlay while scoped, otherwise draws the targeting dot.
	/// </summary>
	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( _isScoped )
		{
			UpdateScopeOverlay();
			return;
		}

		base.DrawHud( painter, crosshair );
	}

	/// <summary>
	/// Draws an outlined targeting dot showing whether the weapon can fire.
	/// </summary>
	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		var color = !HasPrimaryAmmo() || IsReloading || NextPrimaryFire > 0 ? CrosshairNoShoot : CrosshairCanShoot;

		using var scope = painter.Scope();
		painter.BlendMode = BlendMode.Normal;
		painter.Fill = color;
		painter.Stroke = Stroke.Solid( Color.Black, 1f ).WithAlignment( Stroke.StrokeAlignment.Outside );
		painter.Circle( center, 1.5f );
	}

	/// <summary>
	/// Updates the scope's blur from mouse movement and player velocity.
	/// </summary>
	private void UpdateScopeOverlay()
	{
		if ( !_scopeEffect.IsValid() )
			return;

		var mouseBlur = _mouseDelta * 0.25f;
		var velocityBlur = HasOwner ? (Owner.Controller.Velocity.Length / 300f).Clamp( 0f, 1f ) : 0f;

		_scopeEffect.BlurInput = MathF.Min( mouseBlur + velocityBlur, 1f );
	}
}
