using Sandbox.Physics;
using Sandbox.Rendering;

[Title( "#weapon.name.physgun" )]
public partial class Physgun : Local.IPlayerEvents
{
	public Physgun()
	{
		// Tools don't use ammo (the engine default is on, which would show an ammo indicator).
		UsesAmmo = false;
	}

	[Property, RequireComponent] public HighlightOutline BeamHighlight { get; set; }

	[Property, Group( "Sound" )] SoundEvent ReleasedSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent ButtonInSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent ButtonOutSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent GravityDrySound { get; set; }
	[Property, Group( "Sound" )] SoundEvent GravityLaunchSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent GravityDropSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent GravityPullSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent GravityHoldSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent GravityLookOpenSound { get; set; }
	[Property, Group( "Sound" )] SoundEvent GravityLookCloseSound { get; set; }
	[Property, Group( "Sound" )] float GravityLookDropCooldown { get; set; } = 0.5f;

	SoundHandle _gravityHoldSound;
	SoundHandle _gravityLookOpenSound;
	SoundHandle _gravityLookCloseSound;
	bool _clawsOpen;
	bool _clawSoundsSuppressed;
	TimeUntil _gravityLookCooldown;
	TimeUntil _clawOpenSoundCooldown;
	TimeUntil _clawCloseSoundCooldown;
	const float ClawSoundMinInterval = 2f;
	float ClawOpenAmount => _state.Active ? 1f : CanGravityGrabHovered() ? 0.5f : 0f;

	bool CanGravityGrabHovered()
	{
		if ( !_stateHovered.IsValid() ) return false;
		var body = _stateHovered.Body;
		if ( !body.IsValid() || !body.MotionEnabled ) return false;

		var eyePosition = CurrentAimTransform.Position;
		return FindClosestPoint( body, eyePosition ).Distance( eyePosition ) <= PullDistance;
	}

	void UpdateClawSounds()
	{
		// Attack-driven claw movement has its own sounds. Only track idle gravity targeting here.
		if ( _state.Active || _state.Pulling || _stateHovered.Pulling )
		{
			_gravityLookOpenSound?.Stop( 0.2f );
			_gravityLookOpenSound = null;
			_gravityLookCloseSound?.Stop( 0.2f );
			_gravityLookCloseSound = null;
			_clawSoundsSuppressed = true;
			return;
		}

		var open = CanGravityGrabHovered();
		if ( _gravityLookCooldown > 0 )
		{
			_clawsOpen = open;
			return;
		}

		if ( _clawSoundsSuppressed )
		{
			_clawSoundsSuppressed = false;
			_clawsOpen = open;
			return;
		}

		if ( open == _clawsOpen ) return;

		_clawsOpen = open;
		if ( open )
		{
			if ( _clawOpenSoundCooldown > 0 ) return;
			_clawOpenSoundCooldown = ClawSoundMinInterval;
			_gravityLookOpenSound?.Stop( 0.2f );
			_gravityLookOpenSound = GameObject.PlaySound( GravityLookOpenSound );
		}
		else
		{
			if ( _clawCloseSoundCooldown > 0 ) return;
			_clawCloseSoundCooldown = ClawSoundMinInterval;
			_gravityLookCloseSound?.Stop( 0.2f );
			_gravityLookCloseSound = GameObject.PlaySound( GravityLookCloseSound );
		}
	}

	[Rpc.Broadcast]
	void PlayGravityShotSound( bool pulling )
	{
		GameObject.PlaySound( pulling ? GravityPullSound : GravityDrySound );
	}

	[Rpc.Broadcast]
	void PlayGravityLaunchSound()
	{
		GameObject.PlaySound( GravityLaunchSound );
	}

	[Rpc.Broadcast]
	void PlayGravityDropSound()
	{
		_gravityLookCooldown = GravityLookDropCooldown;
		GameObject.PlaySound( GravityDropSound );
	}

	[Rpc.Broadcast]
	void PlayGravityLockSound()
	{
		GameObject.PlaySound( GravityDropSound );
	}

	/// <summary>
	/// The gravity hold loop plays at <see cref="HoldSoundFarPitch"/> when the object is this far away or further.
	/// </summary>
	const float HoldSoundFarDistance = 1024f;

	/// <summary>
	/// The gravity hold loop plays at full pitch when the object is this close or closer.
	/// </summary>
	const float HoldSoundNearDistance = 128f;

	const float HoldSoundFarPitch = 0.5f;

	void UpdateGravityHoldSound()
	{
		// Plays while holding an object, and while pulling one in
		var state = _state.Active && _state.Pulling ? _state : _stateHovered;

		if ( state.Pulling && state.IsValid() && state.Body.IsValid() && state.Body.MotionEnabled )
		{
			var eye = CurrentAimTransform.Position;
			var distance = FindClosestPoint( state.Body, eye ).Distance( eye );

			_gravityHoldSound ??= GameObject.PlaySound( GravityHoldSound );
			_gravityHoldSound?.Position = GetMuzzleTransform().Position;
			_gravityHoldSound?.Pitch = MathX.Remap( distance, HoldSoundFarDistance, HoldSoundNearDistance, HoldSoundFarPitch, 1f );
		}
		else
		{
			_gravityHoldSound?.Stop( 0.2f );
			_gravityHoldSound = null;
		}
	}

	[Property] public float Range { get; set; } = 8196f;

	public record struct GrabState
	{
		public bool Active { get; set; }
		public bool Pulling { get; set; }
		public GameObject GameObject { get; set; }
		public Vector3 LocalOffset { get; set; }
		public Vector3 LocalNormal { get; set; }
		public Rotation GrabOffset { get; set; }
		public float GrabDistance { get; set; }

		/// <summary>
		/// Gravity pulls only: the pull cone's apex, as a world-space offset from the aim position.
		/// </summary>
		public Vector3 PullApex { get; set; }

		/// <summary>
		/// Gravity pulls only: how strongly the object is pulled, depending on how well we aim at it.
		/// </summary>
		public float PullStrength { get; set; }

		public readonly Vector3 EndPoint
		{
			get
			{
				if ( !GameObject.IsValid() ) return LocalOffset;
				return GameObject.WorldTransform.PointToWorld( LocalOffset );
			}
		}

		/// <summary>
		/// <see cref="EndPoint"/> on the physics body. On a proxy the body chases the latest network transform
		/// while the object is drawn interpolated, so measure the body against this, not the drawn point.
		/// </summary>
		public readonly Vector3 BodyPoint
		{
			get
			{
				var physicsBody = Body?.PhysicsBody;
				if ( !physicsBody.IsValid() ) return EndPoint;
				return physicsBody.Transform.WithScale( GameObject.WorldScale ).PointToWorld( LocalOffset );
			}
		}

		public readonly Vector3 EndNormal
		{
			get
			{
				if ( !GameObject.IsValid() ) return LocalNormal;
				return GameObject.WorldTransform.NormalToWorld( LocalNormal );
			}
		}

		public readonly bool IsValid() => GameObject.IsValid();

		public readonly Rigidbody Body => GameObject?.GetComponent<Rigidbody>();

		public override readonly int GetHashCode()
		{
			var hash = new HashCode();
			hash.Add( Active );
			hash.Add( Pulling );
			hash.Add( GameObject );
			hash.Add( LocalOffset );
			hash.Add( LocalNormal );
			hash.Add( GrabOffset );
			hash.Add( GrabDistance );
			hash.Add( PullApex );
			hash.Add( PullStrength );
			return hash.ToHashCode();
		}
	}

	[Sync] public GrabState _state { get; set; } = default;

	[Sync] public GrabState _stateHovered { get; set; } = default;

	// Where the holder wants the body, from their own aim, so the host doesn't steer by the interpolated one
	[Sync( SyncFlags.Interpolate )] public Transform _holdTarget { get; set; }
	[Sync] public int _holdTargetId { get; set; }
	[Sync( SyncFlags.Interpolate )] public float _holdTargetIdLerped { get; set; }

	GameObject _holdTargetObject;
	bool _holdTargetCursorAim;
	Transform _holdStart;

	Transform _lastAimTransform;
	Transform CurrentAimTransform => HasOwner ? OwnerAimTransform : _lastAimTransform;

	bool IsCursorDragging => !IsProxy && InspectCursor.IsDragging;
	bool _cursorAim;

	Transform OwnerAimTransform
	{
		get
		{
			if ( IsProxy || !_cursorAim ) return Owner.EyeTransform;

			var ray = InspectCursor.DragRay;
			return new Transform( ray.Position, Rotation.LookAt( ray.Forward ) );
		}
	}

	Rotation FrameRotation => HasOwner ? Owner.EyeTransform.Rotation : _lastAimTransform.Rotation;

	bool _preventReselect = false;

	void Local.IPlayerEvents.OnMove( ref bool allowMovement )
	{
		if ( _isSpinning ) allowMovement = false;
	}

	void Local.IPlayerEvents.OnCarry( PlayerCarryEvent e )
	{
		if ( _state.IsValid() || _stateHovered.Pulling ) e.Cancelled = true;
	}

	bool _isSpinning;
	readonly HoldRotation _rotation = new();

	bool _launched;

	/// <summary>
	/// The force applied to pull objects to us.
	/// </summary>
	static float PullForce => 1000.0f;

	/// <summary>
	/// The force applied when launching grabbed objects.
	/// </summary>
	static float LaunchForce => 2000.0f;

	/// <summary>
	/// The distance at which we'll grab an object when pulling it towards us.
	/// </summary>
	static float PullDistance => 200.0f;

	/// <summary>
	/// How quickly W/S moves a held object while rotate mode is active.
	/// </summary>
	static float KeyboardMoveSpeed => 240.0f;

	public override void OnCameraMove( Player player, ref Angles angles )
	{
		base.OnCameraMove( player, ref angles );

		if ( _state.IsValid() && _isSpinning )
		{
			angles = default;
		}
	}

	bool _beamPlacedThisFrame;

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		base.PlaceViewModel( camera, view );

		UpdateBeamState();
		_beamPlacedThisFrame = true;
	}

	protected override void OnPreRender()
	{
		base.OnPreRender();
		UpdateGravityHoldSound();
		UpdateClawSounds();

		if ( _beamPlacedThisFrame )
		{
			_beamPlacedThisFrame = false;
			return;
		}

		UpdateBeamState();
	}

	void UpdateBeamState()
	{
		if ( _state.Active && !_state.Pulling )
		{
			var muzzle = HasOwner ? GetMuzzleTransform() : CurrentAimTransform;
			var end = _state.EndPoint;
			var endNormal = _stateHovered.EndNormal;

			if ( !_state.IsValid() && HasOwner && !IsProxy )
			{
				var aim = CurrentAimTransform;
				var tr = Scene.Trace.Ray( aim.Position, aim.Position + aim.Forward * Range )
					.IgnoreGameObjectHierarchy( GameObject.Root )
					.Run();

				end = tr.EndPosition;
				endNormal = tr.Normal;
			}

			UpdateBeam( muzzle, CurrentAimTransform.Forward, end, endNormal, _state.IsValid() );
		}
		else
		{
			CloseBeam();
		}
	}

	protected override void OnControl()
	{
		ControlGrab();

		// After the grab changes, so they go out together
		UpdateHoldTarget();
	}

	void ControlGrab()
	{
		var player = Owner;

		_lastAimTransform = AimTransform;

		UpdateViewmodelScreen();
		UpdateScreenGraph();

		if ( Scene.TimeScale == 0 )
			return;

		if ( Input.Pressed( "use" ) && _state.IsValid() )
		{
			ViewModel?.PlaySound( ButtonInSound );
		}
		else if ( Input.Released( "use" ) && _state.IsValid() )
		{
			ViewModel?.PlaySound( ButtonOutSound );
		}

		var spinMove = Vector3.Zero;
		_isSpinning = Input.Down( "use" ) && _state.Active && _state.IsValid();
		if ( _isSpinning )
		{
			spinMove = Input.AnalogMove;
			player.Controller.WishVelocity = Vector3.Zero;
			Input.Clear( "use" );
		}

		_rotation.UpdateSnapping();

		var isPulling = Input.Down( "attack2" ) && !_preventReselect;
		var primaryDown = Input.Down( "attack1" ) || IsCursorDragging;
		var cursorFreeze = InspectCursor.ConsumeFreeze();
		if ( !_state.IsValid() ) _cursorAim = IsCursorDragging;
		var aim = CurrentAimTransform;

		ViewModel?.RunEvent<ViewModel>( _updateViewModel ??= UpdateViewModel );

		var pullLock = _stateHovered;
		_stateHovered = default;

		if ( _state.IsValid() )
		{
			if ( Input.Pressed( "reload" ) )
			{
				UnfreezeAll( _state.Body );
				Input.Clear( "reload" );
			}

			if ( _state.Pulling )
			{
				if ( Input.Pressed( "attack1" ) )
				{
					var force = player.EyeTransform.Rotation.Forward * LaunchForce;
					Launch( _state.Body, force );
					PlayGravityLaunchSound();

					_state = default;
					_isSpinning = false;
					_preventReselect = true;
				}
				else if ( Input.Pressed( "attack2" ) )
				{
					PlayGravityDropSound();
					_state = default;
					_isSpinning = false;
					_preventReselect = true;
				}
			}
			else
			{
				if ( !primaryDown )
				{
					_state = default;
					_preventReselect = true;
					ViewModel?.PlaySound( ReleasedSound );
					return;
				}

				if ( Input.Down( "attack2" ) || cursorFreeze )
				{
					Freeze( _state.Body );
					_state = default;
					_preventReselect = true;
					ViewModel?.PlaySound( ReleasedSound );
					return;
				}

				if ( !Input.MouseWheel.IsNearZeroLength )
				{
					var state = _state;
					state.GrabDistance += Input.MouseWheel.y * 20.0f;
					state.GrabDistance = MathF.Max( 0.0f, state.GrabDistance );

					_state = default;
					_state = state;

					// stop processing this so inventory doesn't change
					Sandbox.Inventory.ConsumeMouseWheel();
				}
			}

			if ( _isSpinning )
			{
				var move = spinMove;
				var state = _state;

				// Only beam grabs allow W/S to adjust the holding distance.
				if ( !state.Pulling )
				{
					state.GrabDistance += move.x * KeyboardMoveSpeed * Time.Delta;
					state.GrabDistance = MathF.Max( 0.0f, state.GrabDistance );
				}

				// Gravity holds use W/S to pitch around the viewer's right axis.
				state.GrabOffset = _rotation.Rotate( HoldFrame, move, state.Pulling );

				// State needs to reset for sync to detect a change, bug or how it's meant to work?
				_state = default;
				_state = state;
			}

			return;
		}
		else
		{
			_state = default;
		}

		if ( _preventReselect )
		{
			if ( !primaryDown && !Input.Down( "attack2" ) )
				_preventReselect = false;

			return;
		}

		FindGrabbedBody( out var sh, aim, isPulling );
		if ( isPulling ) sh = UpdatePullLock( pullLock, sh, aim );
		_stateHovered = sh;

		if ( isPulling && Input.Pressed( "attack2" ) )
			PlayGravityShotSound( sh.IsValid() && sh.Body.IsValid() && sh.Body.MotionEnabled );

		if ( sh.IsValid() && sh.Pulling && sh.Body.MotionEnabled )
		{
			var eyePosition = aim.Position;
			var closest = FindClosestPoint( sh.Body, eyePosition );
			var distance = closest.Distance( eyePosition );

			if ( distance <= PullDistance )
			{
				_state = sh with { Active = true, Pulling = true, };
			}
		}

		if ( _state.Pulling || _stateHovered.Pulling )
			return;

		if ( primaryDown )
		{
			ViewModel?.RunEvent<ViewModel>( x => x.OnAttack() );

			var muzzle = WeaponModel?.MuzzleGameObject?.WorldTransform ?? player.EyeTransform;

			_state = _stateHovered with { Active = true, Pulling = false };

			if ( _state.GameObject is { } go )
			{
				go.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunPickup() );
			}

			if ( _state.IsValid() )
			{
				Unfreeze( _state.Body );
			}
		}
		else if ( Input.Released( "attack1" ) )
		{
			ViewModel?.PlaySound( ReleasedSound );
		}
		else if ( Input.Pressed( "reload" ) )
		{
			if ( _stateHovered.IsValid() )
			{
				UnfreezeAll( _stateHovered.Body );
			}
		}
		else
		{
			_state = default;
			_preventReselect = false;
		}
	}

	private bool _primaryControlDown;
	private bool _secondaryControlDown;

	protected override void OnPrimaryPressed()
	{
		if ( !_state.IsValid() || !_state.Pulling ) return;

		var force = AimTransform.Rotation.Forward * LaunchForce;
		Launch( _state.Body, force );
		PlayGravityLaunchSound();
		_state = default;
		_preventReselect = true;
	}

	protected override void OnPrimaryDown()
	{
		_primaryControlDown = true;

		if ( _state.IsValid() )
			return;

		if ( ConsumeReselectBlock() ) return;
		UpdateGrab();
	}

	protected override void OnPrimaryReleased()
	{
		_primaryControlDown = false;

		if ( _state.IsValid() )
		{
			if ( !_state.Pulling )
			{
				_state = default;
				_preventReselect = true;
				GameObject.PlaySound( ReleasedSound );
			}

			return;
		}

		if ( ConsumeReselectBlock() ) return;

		if ( _secondaryControlDown )
		{
			UpdateGrab();
		}
		else
		{
			_stateHovered = default;
			GameObject.PlaySound( ReleasedSound );
		}
	}

	protected override void OnSecondaryPressed()
	{
		if ( !_state.IsValid() )
		{
			if ( _preventReselect ) return;
			var aim = AimTransform;
			FindGrabbedBody( out var sh, aim, true );
			PlayGravityShotSound( sh.IsValid() && sh.Body.IsValid() && sh.Body.MotionEnabled );
			return;
		}

		if ( !_state.Pulling ) return;

		PlayGravityDropSound();
		_state = default;
		_preventReselect = true;
	}

	protected override void OnSecondaryDown()
	{
		_secondaryControlDown = true;

		if ( _state.IsValid() )
			return;

		if ( ConsumeReselectBlock() ) return;
		UpdateGrab();
	}

	protected override void OnSecondaryReleased()
	{
		_secondaryControlDown = false;

		if ( _state.IsValid() || ConsumeReselectBlock() ) return;

		if ( _primaryControlDown )
			UpdateGrab();
		else
			_stateHovered = default;
	}

	private bool ConsumeReselectBlock()
	{
		if ( !_preventReselect ) return false;
		if ( !_primaryControlDown && !_secondaryControlDown ) _preventReselect = false;
		return true;
	}

	private void AdjustGrabDistance( float amount )
	{
		if ( HasOwner || !Networking.IsHost || !_state.IsValid() || _state.Pulling ) return;

		var state = _state;
		state.GrabDistance = MathF.Max( 0f, state.GrabDistance + amount );
		_state = default;
		_state = state;
	}

	/// <summary>
	/// Increases the grab distance using a connected signal.
	/// </summary>
	[SignalInput( Id = nameof( ExtendInput ) ), Description( "#weapon.setting.extend.description" )]
	public void ExtendSignal( float amount ) => AdjustGrabDistance( amount * 200f * Time.Delta );

	/// <summary>
	/// Decreases the grab distance using a connected signal.
	/// </summary>
	[SignalInput( Id = nameof( RetractInput ) ), Description( "#weapon.setting.retract.description" )]
	public void RetractSignal( float amount ) => AdjustGrabDistance( -amount * 200f * Time.Delta );

	protected override void OnContraptionControl()
	{
		base.OnContraptionControl();

		if ( ExtendInput.Down() ) AdjustGrabDistance( 200f * Time.Delta );
		if ( RetractInput.Down() ) AdjustGrabDistance( -200f * Time.Delta );
	}

	private void UpdateGrab()
	{
		var aim = AimTransform;
		_lastAimTransform = aim;

		var pullLock = _stateHovered;
		_stateHovered = default;
		FindGrabbedBody( out var sh, aim, _secondaryControlDown );
		if ( _secondaryControlDown ) sh = UpdatePullLock( pullLock, sh, aim );
		_stateHovered = sh;

		if ( sh.IsValid() && sh.Pulling && sh.Body.MotionEnabled )
		{
			var closest = FindClosestPoint( sh.Body, aim.Position );
			if ( closest.Distance( aim.Position ) <= PullDistance )
			{
				_state = sh with { Active = true, Pulling = true };
			}
		}

		if ( _state.Pulling || _stateHovered.Pulling )
			return;

		if ( _primaryControlDown )
		{
			_state = _stateHovered with { Active = true, Pulling = false };

			if ( _state.GameObject is { } go )
			{
				go.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunPickup() );
			}

			if ( _state.IsValid() )
				Unfreeze( _state.Body );
		}
		else
		{
			_state = default;
		}
	}

	Action<ViewModel> _updateViewModel;

	private void UpdateViewModel( ViewModel model )
	{
		model.IsAttacking = _state.Active;
		model.Renderer?.Set( "stylus", ClawOpenAmount );
		model.Renderer?.Set( "b_button", _isSpinning );
		model.Renderer?.Set( "brake", _state.Active || _state.Pulling || _stateHovered.Pulling ? 1 : 0 );
	}

	readonly PhysicsHold _hold = new();

	/// <summary>
	/// The object our joint is moving
	/// </summary>
	GameObject _held;

	/// <summary>
	/// Tells the object we were moving that we let go of it
	/// </summary>
	void DropHeld()
	{
		var held = _held;
		_held = null;

		if ( !held.IsValid() ) return;

		held.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunDrop() );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		_hold.Release();
		DropHeld();
		CloseBeam();
		_gravityHoldSound?.Stop( 0.2f );
		_gravityHoldSound = null;

		_state = default;
		_gravityLookOpenSound?.Stop( 0.2f );
		_gravityLookOpenSound = null;
		_gravityLookCloseSound?.Stop( 0.2f );
		_gravityLookCloseSound = null;
		_clawsOpen = false;
		_clawSoundsSuppressed = false;
		_gravityLookCooldown = 0;
		_stateHovered = default;
		_launched = default;
		_primaryControlDown = false;
		_secondaryControlDown = false;
		_isSpinning = false;
		_cursorAim = false;
	}

	// Set every frame, a value from fixed update would be sent stamped with a later frame's time.
	// Also while hovering, so the host's interpolated target never starts a grab from where the last one let go.
	void UpdateHoldTarget()
	{
		var state = _state.Body.IsValid() ? _state : _stateHovered;
		var target = state.Body.IsValid() ? state.GameObject : null;
		_holdTarget = target.IsValid() ? GetHoldTarget( state ) : CurrentAimTransform;

		if ( target == _holdTargetObject && _cursorAim == _holdTargetCursorAim ) return;

		_holdTargetObject = target;
		_holdTargetCursorAim = _cursorAim;
		_holdTargetId++;
		_holdTargetIdLerped = _holdTargetId;
	}

	/// <summary>
	/// The holder's target once the interpolated one is all for what they hold, until then where we grabbed it.
	/// </summary>
	Transform GetProxyHoldTarget()
	{
		var caughtUp = _holdTargetIdLerped.AlmostEqual( _holdTargetId );
		return ClampToRange( caughtUp ? _holdTarget : _holdStart );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();

		// switched straight to another object, the joint still holds the old one
		if ( _held.IsValid() && _held != _state.GameObject )
		{
			_hold.Release();
			DropHeld();
		}

		if ( !CanMoveFor( _state ) )
		{
			_hold.Release();
			DropHeld();

			if ( _stateHovered.Pulling && CanMoveFor( _stateHovered ) )
				ApplyPullForce( _stateHovered );

			_launched = false;

			return;
		}

		// If we just launched, don't add a joint until state has let go.
		if ( _launched ) return;

		if ( !_hold.IsHolding )
			_holdStart = new Transform( _state.EndPoint, _state.Body.WorldRotation );

		_held = _state.GameObject;
		DamageBlame.Blame( _held, Owner );
		_hold.Hold( _state.Body, _state.EndPoint );
		_hold.Move( IsProxy ? GetProxyHoldTarget() : GetHoldTarget() );
	}

	/// <summary>
	/// The frame <see cref="GrabState.GrabOffset"/> is relative to: the full aim rotation for gravity
	/// holds, only the stabilized yaw for beam grabs.
	/// </summary>
	Rotation HoldFrame => _state.Pulling ? CurrentAimTransform.Rotation : PhysicsHold.Stabilize( FrameRotation );

	Transform GetHoldTarget() => GetHoldTarget( _state );

	Transform GetHoldTarget( in GrabState state )
	{
		var eyeTransform = CurrentAimTransform;
		var point = state.Body.IsProxy ? state.BodyPoint : state.EndPoint;
		var grabDistance = PhysicsHold.ClampGrabDistance( state.Body, point, eyeTransform, state.GrabDistance );
		var targetPosition = eyeTransform.Position + eyeTransform.Rotation.Forward * grabDistance;
		var frame = state.Pulling ? eyeTransform.Rotation : PhysicsHold.Stabilize( FrameRotation );
		return new Transform( targetPosition, frame * state.GrabOffset );
	}

	// The holder sends the target, keep it within reach of where we see them
	Transform ClampToRange( Transform target )
	{
		var eye = CurrentAimTransform.Position;
		return target.WithPosition( eye + (target.Position - eye).ClampLength( Range ) );
	}

	/// <summary>
	/// When true, the physgun aims where the seated player's camera looks.
	/// </summary>
	[Property, ClientEditable, Sync, Title( "#weapon.setting.camera_aim" ), Description( "#weapon.setting.camera_aim.description" )]
	public bool CanAim { get; set; } = true;

	public override bool IsTargetedAim => CanAim;

	Transform AimTransform
	{
		get
		{
			var ray = AimRay;
			return new Transform( ray.Position, Rotation.LookAt( ray.Forward ) );
		}
	}

	bool CanMove( GrabState state )
	{
		if ( !state.IsValid() ) return false;
		if ( !state.Body.IsValid() ) return false;

		// Only move the body if we own it.
		if ( state.Body.IsProxy ) return false;

		// Only move the body if it's dynamic.
		if ( !state.Body.MotionEnabled ) return false;
		if ( !state.Body.PhysicsBody.IsValid() ) return false;

		return true;
	}

	/// <summary>
	/// Ask the object if it allows being grabbed (anything can reject via IPhysgunEvent)
	/// </summary>
	bool AllowsGrab( GameObject go )
	{
		var grabEvent = new IPhysgunEvent.GrabEvent { Grabber = Network.Owner };
		go.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunGrab( grabEvent ) );
		return !grabEvent.Cancelled;
	}

	/// <summary>
	/// Is this physgun holding <paramref name="root"/> or pulling it in? The grab is synced, so this works everywhere.
	/// </summary>
	public bool IsGrabbing( GameObject root )
	{
		return Grabs( _state ) || (_stateHovered.Pulling && Grabs( _stateHovered ));

		bool Grabs( GrabState state ) => (state.Active || state.Pulling) && state.GameObject?.Root == root;
	}

	/// <summary>
	/// The holder picks what to grab on their machine, so check it again here before moving it for them.
	/// </summary>
	bool CanMoveFor( in GrabState state ) => CanMove( state ) && (!IsProxy || AllowsGrab( state.GameObject ));

	bool FindGrabbedBody( out GrabState state, Transform aim, bool isPulling )
	{
		state = default;

		var tr = Scene.Trace.Ray( aim.Position, aim.Position + aim.Forward * Range )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		state.LocalOffset = tr.EndPosition;
		state.LocalNormal = tr.Normal;
		state.Pulling = isPulling;

		var rb = tr.Hit && tr.Body is not null ? tr.Component as Rigidbody : null;

		if ( isPulling )
		{
			if ( !CanPull( rb ) )
				rb = FindPullTarget( aim, tr ) ?? rb;

			if ( GetRagdollRoot( rb ) is { } root )
				rb = root;
		}

		if ( !rb.IsValid() || !rb.PhysicsBody.IsValid() ) return false;

		var physicsBody = rb.PhysicsBody;
		var go = physicsBody.GameObject;
		if ( !go.IsValid() || go.IsDestroyed ) return false;

		if ( !AllowsGrab( go ) ) return false;

		// Trace hits physics, convert to local using scaled physics transform.
		var bodyTransform = physicsBody.Transform.WithScale( go.WorldScale );

		state.GameObject = go;
		state.LocalNormal = bodyTransform.NormalToLocal( tr.Normal );

		if ( isPulling )
		{
			// Scale is built into mass center, remove it.
			var bodyScale = new Transform( Vector3.Zero, Rotation.Identity, bodyTransform.Scale );
			state.LocalOffset = bodyScale.PointToLocal( physicsBody.LocalMassCenter );
			state.GrabDistance = 0;
			state.GrabOffset = aim.Rotation.Inverse * bodyTransform.Rotation;
		}
		else
		{
			state.LocalOffset = bodyTransform.PointToLocal( tr.HitPosition );
			state.GrabDistance = Vector3.DistanceBetween( aim.Position, tr.HitPosition );
			state.GrabDistance = PhysicsHold.ClampGrabDistance( state.Body, tr.HitPosition, aim, state.GrabDistance );
			state.GrabOffset = PhysicsHold.Stabilize( FrameRotation ).Inverse * bodyTransform.Rotation;
		}

		_rotation.Reset( state.GrabOffset );

		return true;
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void Freeze( Rigidbody body )
	{
		if ( !body.IsValid() ) return;

		var go = body.GameObject;
		if ( go.Tags.Has( "npc" ) )
			return;

		FreezeEffect.Play( go, true, IsProxy ? null : [go] );

		if ( body.IsProxy ) return;

		if ( Networking.IsHost && AllowsGrab( go ) )
		{
			body.MotionEnabled = false;
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Unfreeze( Rigidbody body ) => UnfreezeBody( body );

	void UnfreezeBody( Rigidbody body )
	{
		if ( !body.IsValid() ) return;
		if ( body.IsProxy ) return;
		if ( !AllowsGrab( body.GameObject ) ) return;

		body.MotionEnabled = true;
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void UnfreezeAll( Rigidbody body )
	{
		if ( !body.IsValid() ) return;

		var bodies = new HashSet<Rigidbody>();
		if ( !IsProxy || !body.IsProxy )
			PhysicsHold.GetConnectedBodies( body.GameObject, bodies );

		FreezeEffect.Play( body.GameObject, false, IsProxy ? null : bodies.Select( x => x.GameObject ) );

		if ( IsProxy && body.IsProxy ) return;

		if ( !IsProxy )
		{
			var count = bodies.Select( x => x.GameObject.Root ).Distinct().Count();
			var phrase = Game.Language.GetPhrase( "physgun.unfrozen", new() { { "count", count.ToString() } } );
			Sandbox.UI.Notices.AddNotice( "ac_unit", Color.Cyan, phrase );
		}

		if ( body.IsProxy || !Networking.IsHost ) return;

		foreach ( var rb in bodies )
		{
			UnfreezeBody( rb );
		}

		var models = bodies
			.Select( x => x.Components.GetInAncestorsOrSelf<ModelPhysics>() )
			.Where( x => x.IsValid() )
			.Distinct();

		foreach ( var mp in models )
		{
			UnfreezeModelPhysics( mp );
		}
	}

	void UnfreezeModelPhysics( ModelPhysics modelPhysics )
	{
		if ( !modelPhysics.IsValid() ) return;
		if ( modelPhysics.IsProxy ) return;
		if ( !AllowsGrab( modelPhysics.GameObject ) ) return;

		modelPhysics.MotionEnabled = true;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void Launch( Rigidbody body, Vector3 force )
	{
		if ( !body.IsValid() ) return;
		if ( body.IsProxy ) return;
		if ( !AllowsGrab( body.GameObject ) ) return;

		// We already launched.
		if ( _launched ) return;

		_hold.Release();
		DamageBlame.Blame( body.GameObject, Owner );

		var mass = body.Mass;
		body.ApplyImpulse( force.Normal * (mass * force.Length) );
		body.PhysicsBody?.ApplyAngularImpulse( Vector3.Random * (mass * force.Length) );

		_launched = true;
	}
}
