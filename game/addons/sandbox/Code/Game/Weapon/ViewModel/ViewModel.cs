using System.Threading;

/// <summary>
/// Places and animates the local player's first-person weapon model.
/// </summary>
public partial class ViewModel : Sandbox.BaseWeaponModel
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
	/// Shifts the weapon as the player turns.
	/// </summary>
	[Property, Group( "Inertia" )]
	public bool LookInertia { get; set; } = false;

	/// <summary>
	/// Multiplies the distance the weapon sways. A value of 1 gives normal sway.
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

	protected override void OnStart()
	{
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>() )
		{
			// Don't render shadows for viewmodels
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
	}

	/// <summary>
	/// Called while the camera composes to let the
	/// animation's camera bone drive the view (reload sway and kicks authored in the anim).
	/// This mutates the real camera view, not the viewmodel - the viewmodel itself is placed
	/// separately from the pre-bone snapshot, see <see cref="Place"/>.
	/// </summary>
	public void UpdateCameraBone( ref CameraView view )
	{
		if ( !Renderer.IsValid() ) return;

		Renderer.Enabled = !HideViewModel;

		if ( !GamePreferences.ViewBobbing ) return;

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
		var position = view.Position;
		var player = GetComponentInParent<Player>();
		if ( player.IsValid() )
		{
			if ( MovementBob && GamePreferences.ViewBobbing )
				ApplyMovementBob( player, view.Rotation, ref position, ref rotation );

			if ( LookInertia && GamePreferences.ViewBobbing )
			{
				var amount = LookInertiaAmount;
				if ( GetComponentInParent<IronSightsWeapon>() is { IsAiming: true } )
					amount *= ADSInertiaScale;

				position += player.GetViewModelSway( rotation ) * amount;
			}
			else
			{
				player.ResetViewModelSway();
			}
		}

		WorldPosition = position;
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

	/// <summary>
	/// Pulses a one-shot input on the viewmodel's active animation graph.
	/// </summary>
	internal void TriggerAnimation( string name )
	{
		if ( !Renderer.IsValid() ) return;

		Renderer.Animation.Parameters.TryTrigger( name );
	}

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		Renderer?.Animation.Parameters.TrySet( "b_empty", GetComponentInParent<BaseCombatWeapon>()?.Clip1 == 0 );
		base.OnAttack( hitPoint, origin );

		// The engine parents the flash to the muzzle, but its particles default to
		// world space. Keep first-person flashes attached through recoil and sway.
		if ( MuzzleGameObject.IsValid() )
		{
			foreach ( var effect in MuzzleGameObject.GetComponentsInChildren<ParticleEffect>( true ) )
				effect.LocalSpace = 1.0f;
		}
	}

	/// <summary>
	/// Plays the empty-trigger animation.
	/// </summary>
	public override void OnDryFire() => TriggerAnimation( "b_attack_dry" );

	/// <summary>
	/// Called when starting to reload a weapon.
	/// </summary>
	public override void OnReloadStart()
	{
		if ( Renderer?.Animation.Graph is not null )
		{
			var parameters = Renderer.Animation.Parameters;
			var weapon = GetComponentInParent<BaseCombatWeapon>();
			parameters.TrySet( "b_empty", weapon?.Clip1 == 0 );
			if ( weapon is not null )
			{
				var duration = weapon.IncrementalReloading && weapon.ReloadStartTime > 0f ? weapon.ReloadStartTime : weapon.ReloadTime;
				parameters.TrySet( "reload_duration", duration );
			}

			parameters.TryTrigger( "b_reload" );
		}

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
		if ( Renderer?.Animation.Graph is not null )
		{
			var parameters = Renderer.Animation.Parameters;
			if ( GetComponentInParent<BaseCombatWeapon>() is { } weapon )
				parameters.TrySet( "reload_duration", weapon.ReloadTime );

			parameters.TryTrigger( "b_reloading_shell" );
		}

		StartSounds( IncrementalReloadSoundEvents, ref _reloadSoundCts );
	}

	/// <summary>
	/// Plays the reload finish animation and its sounds.
	/// </summary>
	public override void OnReloadFinish()
	{
		CancelSounds( ref _reloadSoundCts );

		if ( IsIncremental )
			StartSounds( IncrementalReloadFinishSounds, ref _reloadFinishSoundCts );

		Renderer?.Animation.Parameters.TryTrigger( "b_reload_finish" );
	}

	public override void OnReloadCancel()
	{
		Renderer?.Animation.Parameters.TryTrigger( "b_reload_cancel" );
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
