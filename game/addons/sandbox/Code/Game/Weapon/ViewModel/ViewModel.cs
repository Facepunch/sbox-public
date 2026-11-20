using System.Threading;

public sealed partial class ViewModel : Sandbox.BaseWeaponModel
{
	[ConVar( "sandbox.hideviewmodel", ConVarFlags.Cheat )]
	private static bool HideViewModel { get; set; } = false;

	/// <summary>
	/// A sound to play at a specific time during reload.
	/// </summary>
	public record struct ReloadSoundEntry
	{
		/// <summary>
		/// Seconds after reload starts to play this sound.
		/// </summary>
		[KeyProperty] public float Time { get; set; }

		/// <summary>
		/// The sound to play.
		/// </summary>
		[Property, KeyProperty] public SoundEvent Sound { get; set; }
	}

	/// <summary>
	/// Timed sound events to play during reload.
	/// </summary>
	[Property, Group( "Reload Sounds" )]
	public List<ReloadSoundEntry> ReloadSoundEvents { get; set; } = new();

	/// <summary>
	/// Timed sound events to play during each incremental reload cycle.
	/// </summary>
	[Property, Group( "Reload Sounds" )]
	public List<ReloadSoundEntry> IncrementalReloadSoundEvents { get; set; } = new();

	/// <summary>
	/// Timed sound events played when starting an incremental reload sequence.
	/// </summary>
	[Property, Group( "Reload Sounds" )]
	public List<ReloadSoundEntry> IncrementalReloadStartSounds { get; set; } = new();

	/// <summary>
	/// Timed sound events played when finishing an incremental reload sequence.
	/// </summary>
	[Property, Group( "Reload Sounds" )]
	public List<ReloadSoundEntry> IncrementalReloadFinishSounds { get; set; } = new();

	private CancellationTokenSource _reloadSoundCts;
	private CancellationTokenSource _reloadFinishSoundCts;

	/// <summary>
	/// Turns on incremental reloading parameters.
	/// </summary>
	[Property, Group( "Animation" )]
	public bool IsIncremental { get; set; } = false;

	/// <summary>
	/// Animation speed in general.
	/// </summary>
	[Property, Group( "Animation" )]
	public float AnimationSpeed { get; set; } = 1.0f;

	/// <summary>
	/// Animation speed for incremental reload sections.
	/// </summary>
	[Property, Group( "Animation" )]
	public float IncrementalAnimationSpeed { get; set; } = 1.0f;

	/// <summary>
	/// How much inertia should this weapon have?
	/// </summary>
	[Property, Group( "Inertia" )]
	Vector2 InertiaScale { get; set; } = new Vector2( 2, 2 );

	/// <summary>
	/// Limit the inertia to avoid extreme swaying at lower framerate.
	/// </summary>
	[Property, Group( "Inertia" )]
	public float MaxInertia { get; set; } = 15.0f;

	const float InertiaLagTime = 0.0167f;
	const float InertiaSmoothingTime = 0.03f;

	/// <summary>
	/// Swing the viewmodel around based on the player's look direction, with a springy lag. This is purely cosmetic and does not affect the actual camera.
	/// </summary>
	[Property, Group( "Inertia" )]
	public bool LookInertia { get; set; } = false;

	/// <summary>
	/// Spring stiffness - how quickly it pulls back towards your actual look direction.
	/// </summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )]
	public float LookInertiaFrequency { get; set; } = 3.0f;

	/// <summary>
	/// Spring damping. 1 settles with no overshoot, below 1 wobbles before settling - lower
	/// for a springier, bouncier feel.
	/// </summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )]
	public float LookInertiaDamping { get; set; } = 0.35f;

	/// <summary>
	/// How far out in front of the eye the gun pivots from. Larger values make the same angle
	/// lag produce a bigger visible swing, since the gun is swinging on a longer arm.
	/// </summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )]
	public float LookInertiaPivotDistance { get; set; } = 10.0f;

	/// <summary>
	/// Overall strength of the effect, applied after the spring - 0 is off, 1 is the spring's
	/// natural lag, above 1 exaggerates it. Use this to dial the whole thing up or down without
	/// touching how snappy or bouncy it feels (that's Frequency/Damping).
	/// </summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )]
	public float LookInertiaAmount { get; set; } = 1.0f;

	/// <summary>
	/// Further multiplies <see cref="LookInertiaAmount"/> while aiming down sights (on weapons
	/// with an <see cref="IronSightsWeapon"/>), so the gun holds steadier while aiming - below 1
	/// calms the sway down, above 1 makes it worse.
	/// </summary>
	[Property, Title( "ADS Inertia Scale" ), Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )]
	public float ADSInertiaScale { get; set; } = 0.3f;

	public bool IsAttacking { get; set; }

	TimeSince AttackDuration;

	bool _reloadFinishing;
	TimeSince _reloadFinishTimer;

	Vector2 lastInertia;
	Vector2 currentInertia;
	bool isFirstUpdate = true;

	Vector2 _lookAngles;
	Vector2 _lookVelocity;
	bool _lookInertiaFirstUpdate = true;

	protected override void OnStart()
	{
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>() )
		{
			// Don't render shadows for viewmodels
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
	}

	protected override void OnUpdate()
	{
		UpdateAnimation();
	}

	void ApplyInertia( Rotation rotation )
	{
		var rot = rotation.Angles();

		// Need to fetch data from the camera for the first frame
		if ( isFirstUpdate )
		{
			lastInertia = new Vector2( rot.pitch, rot.yaw );
			currentInertia = Vector2.Zero;
			isFirstUpdate = false;
		}

		var newPitch = rot.pitch;
		var newYaw = rot.yaw;

		var frameDelta = new Vector2( Angles.NormalizeAngle( newPitch - lastInertia.x ), Angles.NormalizeAngle( lastInertia.y - newYaw ) );
		lastInertia = new( newPitch, newYaw );

		var dt = Time.Delta;
		if ( dt <= 0f ) return;

		var turnSpeed = frameDelta / MathF.Max( dt, 1f / 500f );
		var target = (turnSpeed * InertiaLagTime).Clamp( -MaxInertia, MaxInertia );

		currentInertia = new Vector2(
			MathX.ExponentialDecay( currentInertia.x, target.x, InertiaSmoothingTime, dt ),
			MathX.ExponentialDecay( currentInertia.y, target.y, InertiaSmoothingTime, dt ) );
	}

	/// <summary>
	/// Called by the weapon while the camera composes - feeds the aim inertia and lets the
	/// animation's camera bone drive the view (reload sway and kicks authored in the anim).
	/// This mutates the real camera view, not the viewmodel - the viewmodel itself is placed
	/// separately from the pre-bone snapshot, see <see cref="Place"/>.
	/// </summary>
	public void UpdateCameraBone( ref CameraView view )
	{
		if ( !Renderer.IsValid() ) return;

		Renderer.Enabled = !HideViewModel;

		if ( !GamePreferences.ViewBobbing )
		{
			currentInertia = Vector2.Zero;
			isFirstUpdate = true;
			return;
		}

		ApplyInertia( view.Rotation );

		if ( Renderer.TryGetBoneTransformLocal( "camera", out var bone ) )
		{
			var scale = 0.5f;
			view.Position += view.Rotation * bone.Position * scale;
			view.Rotation *= bone.Rotation * scale;
		}
	}

	/// <summary>
	/// Place the view model - called by the weapon's <c>PlaceViewModel</c> with the pre-bone view,
	/// so the camera bone moves the camera around the gun rather than dragging the gun with it.
	/// </summary>
	public void Place( in CameraView view )
	{
		var rotation = view.Rotation;
		var pivotOffset = Vector3.Zero;

		if ( LookInertia && GamePreferences.ViewBobbing )
			rotation = ApplyLookInertia( view.Rotation, out pivotOffset );
		else
		{
			_lookInertiaFirstUpdate = true;
			_lookVelocity = Vector2.Zero;
		}

		WorldPosition = view.Position + pivotOffset;
		WorldRotation = rotation;
	}

	/// <summary>
	/// Follow the render-only camera shake after placement, without feeding it into look inertia.
	/// Use the final camera view as the pivot to preserve the camera bone and procedural bob offsets.
	/// </summary>
	public void FollowCameraEffects( CameraComponent camera, in CameraView view )
	{
		var effects = CameraEffectSystem.Get( Scene );
		if ( effects is null ) return;

		effects.QueryOffsets( camera, out var offset, out var angles, out _ );
		var renderTransform = new Transform( view.Position + view.Rotation * offset, view.Rotation * angles );
		WorldTransform = renderTransform.ToWorld( view.Transform.ToLocal( WorldTransform ) );
	}

	Rotation ApplyLookInertia( Rotation rotation, out Vector3 pivotOffset )
	{
		var angles = rotation.Angles();
		var target = new Vector2( angles.pitch, angles.yaw );

		if ( _lookInertiaFirstUpdate )
		{
			_lookAngles = target;
			_lookVelocity = Vector2.Zero;
			_lookInertiaFirstUpdate = false;
		}
		else
		{
			// Follow yaw the short way round so crossing the -180/180 seam doesn't fling the spring.
			target.y = _lookAngles.y + Angles.NormalizeAngle( target.y - _lookAngles.y );
		}

		_lookAngles = Vector2.SpringDamp( _lookAngles, target, ref _lookVelocity, Time.Delta, LookInertiaFrequency, LookInertiaDamping );

		var laggedFull = new Angles( _lookAngles.x, _lookAngles.y, angles.roll ).ToRotation();

		// Scale the effect's strength here, separately from the spring's own timing/feel above.
		var amount = LookInertiaAmount;

		var ironSights = GetComponentInParent<IronSightsWeapon>();
		if ( ironSights.IsValid() && ironSights.IsAiming )
			amount *= ADSInertiaScale;

		var delta = laggedFull * rotation.Inverse;
		var lagged = Rotation.Slerp( Rotation.Identity, delta, amount, clamp: false ) * rotation;

		var pivotLocal = Vector3.Forward * LookInertiaPivotDistance;
		pivotOffset = (rotation * pivotLocal) - (lagged * pivotLocal);

		return lagged;
	}

	void UpdateAnimation()
	{
		var playerController = GetComponentInParent<PlayerController>();
		if ( !playerController.IsValid() ) return;

		// Eye angles, not the camera - the camera's transform is composed later in the frame.
		var rot = playerController.EyeAngles;

		Renderer.Set( "b_twohanded", true );
		Renderer.Set( "deploy_type", 1 );
		Renderer.Set( "reload_type", 1 );

		Renderer.Set( "b_grounded", playerController.IsOnGround );
		Renderer.Set( "move_bob", GamePreferences.ViewBobbing ? playerController.Velocity.Length.Remap( 0, playerController.RunSpeed * 2f ) : 0 );

		Renderer.Set( "aim_pitch", rot.pitch );
		Renderer.Set( "aim_pitch_inertia", GamePreferences.ViewBobbing ? currentInertia.x * InertiaScale.x : 0 );

		Renderer.Set( "aim_yaw", rot.yaw );
		Renderer.Set( "aim_yaw_inertia", GamePreferences.ViewBobbing ? currentInertia.y * InertiaScale.y : 0 );

		Renderer.Set( "attack_hold", IsAttacking ? AttackDuration.Relative.Clamp( 0f, 1f ) : 0f );

		if ( _reloadFinishing && _reloadFinishTimer >= 0.5f )
		{
			_reloadFinishing = false;
			Renderer.Set( "speed_reload", AnimationSpeed );
			Renderer.Set( "b_reloading", false );
		}

		var velocity = playerController.Velocity;

		var dir = velocity;
		var eyeRotation = rot.ToRotation();
		var forward = eyeRotation.Forward.Dot( dir );
		var sideward = eyeRotation.Right.Dot( dir );

		var angle = MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees();

		Renderer.Set( "move_direction", angle );
		Renderer.Set( "move_speed", velocity.Length );
		Renderer.Set( "move_groundspeed", velocity.WithZ( 0 ).Length );
		Renderer.Set( "move_y", sideward );
		Renderer.Set( "move_x", forward );
		Renderer.Set( "move_z", velocity.z );
	}

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		base.OnAttack( hitPoint, origin );

		// The engine parents the flash to the muzzle, but its particles default to
		// world space. Keep first-person flashes attached through recoil and sway.
		if ( MuzzleGameObject.IsValid() )
		{
			foreach ( var effect in MuzzleGameObject.GetComponentsInChildren<ParticleEffect>( true ) )
				effect.LocalSpace = 1.0f;
		}

		if ( IsThrowable )
		{
			Renderer?.Set( "b_throw", true );

			Invoke( 0.5f, () =>
			{
				Renderer?.Set( "b_deploy_new", true );
				Renderer?.Set( "b_pull", false );
			} );
		}
	}

	/// <summary>
	/// Called when starting to reload a weapon.
	/// </summary>
	public override void OnReloadStart()
	{
		_reloadFinishing = false; // cancel any pending incremental finish from a previous reload
		Renderer?.Set( "speed_reload", AnimationSpeed );
		Renderer?.Set( IsIncremental ? "b_reloading" : "b_reload", true );

		if ( IsIncremental )
			StartSounds( IncrementalReloadStartSounds, ref _reloadFinishSoundCts );
		else
			StartSounds( ReloadSoundEvents, ref _reloadSoundCts );
	}

	/// <summary>
	/// Called when incrementally reloading a weapon.
	/// </summary>
	public override void OnIncrementalReload()
	{
		Renderer?.Set( "speed_reload", IncrementalAnimationSpeed );
		Renderer?.Set( "b_reloading_shell", true );

		StartSounds( IncrementalReloadSoundEvents, ref _reloadSoundCts );
	}

	public override void OnReloadFinish()
	{
		CancelSounds( ref _reloadSoundCts );

		if ( IsIncremental )
		{
			StartSounds( IncrementalReloadFinishSounds, ref _reloadFinishSoundCts );

			_reloadFinishing = true;
			_reloadFinishTimer = 0;
		}
		else
		{
			Renderer?.Set( "b_reload", false );
		}
	}

	public override void OnReloadCancel()
	{
		CancelSounds( ref _reloadSoundCts );
		CancelSounds( ref _reloadFinishSoundCts );
	}

	private void StartSounds( List<ReloadSoundEntry> events, ref CancellationTokenSource cts )
	{
		CancelSounds( ref cts );

		if ( events.Count == 0 )
			return;

		cts = new CancellationTokenSource();
		_ = PlaySoundsAsync( events, cts.Token );
	}

	private void CancelSounds( ref CancellationTokenSource cts )
	{
		if ( cts is null ) return;

		cts.Cancel();
		cts.Dispose();
		cts = null;
	}

	private async Task PlaySoundsAsync( List<ReloadSoundEntry> events, CancellationToken ct )
	{
		var sorted = events.OrderBy( e => e.Time ).ToList();
		var elapsed = 0f;

		foreach ( var entry in sorted )
		{
			var delay = entry.Time - elapsed;

			if ( delay > 0f )
				await Task.DelaySeconds( delay, ct );

			if ( ct.IsCancellationRequested )
				return;

			if ( entry.Sound is not null )
				GameObject.PlaySound( entry.Sound );

			elapsed = entry.Time;
		}
	}
}
