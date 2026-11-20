using Sandbox.Rendering;

public partial class BaseSandboxWeapon : Sandbox.BaseCombatWeapon, IKillIcon, IPlayerControllable
{
	/// <summary>
	/// Used for overriding the display icon
	/// </summary>
	public virtual string InventoryIconOverride => null;

	/// <summary>
	/// If true the game should hide the hud when holding this weapon. Useful for cameras, or scopes.
	/// </summary>
	public virtual bool WantsHideHud => false;

	/// <summary>
	/// Which parts of the HUD <see cref="WantsHideHud"/> hides. Everything by default.
	/// </summary>
	public virtual HudElement HiddenHudElements => HudElement.All;

	/// <summary>
	/// The owner of this carriable
	/// </summary>
	// Hides the engine BaseCombatWeapon.Owner (a PlayerController) with the game's Player-based owner.
	// Engine-internal code keeps using its own PlayerController owner; game code sees the Player.
	public new Player Owner
	{
		get
		{
			return GetComponentInParent<Player>( true );
		}
	}

	public bool HasOwner => Owner.IsValid();

	/// <summary>
	/// When true, seated aim uses the scene camera direction instead of the weapon's muzzle direction.
	/// Override in weapons that support player-directed aim (e.g. RPG tracked mode, Physgun aim mode).
	/// </summary>
	public virtual bool IsTargetedAim => false;

	/// <summary>
	/// Controlled from a seat with targeted aim fires where the camera looks; otherwise the engine's
	/// muzzle-based unheld aim applies.
	/// </summary>
	protected override Ray UnheldAimRay
	{
		get
		{
			var seated = ControlContext.Player;
			if ( seated.IsValid() && IsTargetedAim && Scene.Camera.IsValid() )
				return Scene.Camera.Transform.World.ForwardRay;

			return base.UnheldAimRay;
		}
	}

	/// <summary>
	/// The root GameObject to ignore when tracing from AimRay.
	/// </summary>
	public GameObject AimIgnoreRoot => Holder?.GameObject ?? GameObject;

	/// <summary>
	/// Who gets credit for this weapon's damage (the engine's <see cref="Sandbox.BaseCombatWeapon.Attacker"/>).
	/// The owning player if held, the seated player if controlled from a contraption seat, otherwise
	/// the nearest kill source, the engine's holder (an NPC), or the weapon itself.
	/// </summary>
	protected override GameObject Attacker
	{
		get
		{
			if ( HasOwner ) return Owner.GameObject;

			var seatedPlayer = ControlContext.Player;
			if ( seatedPlayer.IsValid() ) return seatedPlayer.GameObject;

			if ( GetComponentInParent<IKillSource>( true ) is Component killSource ) return killSource.GameObject;

			return base.Attacker;
		}
	}

	private Vector3 GetPrefabWorldScale()
	{
		if ( !string.IsNullOrEmpty( GameObject.PrefabInstanceSource ) )
		{
			var prefab = GameObject.GetPrefab( GameObject.PrefabInstanceSource );
			if ( prefab.IsValid() ) return prefab.LocalScale;
		}

		return WorldScale;
	}

	/// <summary>
	/// Runs on the host when added to an inventory. The engine base seeds the magazines and the
	/// shared reserve pool - only when they've never been filled, so a dropped gun keeps its rounds
	/// through a pickup.
	/// </summary>
	protected override void OnAdded( Sandbox.BaseInventoryComponent inventory )
	{
		IsDropped = false;

		base.OnAdded( inventory );
	}

	/// <summary>
	/// The input that fires the primary attack when this weapon is controlled via a seat.
	/// </summary>
	[Property, ClientEditable, Group( "#entity.group.input" ), Title( "#input.action.attack1" ), Description( "#weapon.setting.primary.description" )]
	public ClientInput ShootInput { get; set; }

	/// <summary>
	/// The input that fires the secondary attack when this weapon is controlled via a seat.
	/// </summary>
	[Property, ClientEditable, Group( "#entity.group.input" ), Title( "#input.action.attack2" ), Description( "#weapon.setting.secondary.description" )]
	public ClientInput SecondaryInput { get; set; }

	/// <summary>Weapons wired into a contraption stay put - no pickup prompt, no Touch pickup.</summary>
	protected override bool OnCanPickup( Sandbox.BaseInventoryComponent inventory )
	{
		var isLinked = GameObject.Root.GetComponentsInChildren<ManualLink>( true ).Any();
		return !isLinked && !ShootInput.IsEnabled && !SecondaryInput.IsEnabled;
	}

	/// <summary>A seated player can only control this while not holding a weapon of their own.</summary>
	public virtual bool CanControl( Player player )
	{
		var inventory = player.GetComponent<PlayerInventory>();
		return inventory is null || !inventory.ActiveWeapon.IsValid();
	}

	/// <summary>
	/// Dispatches a primary action from a connected signal.
	/// </summary>
	[SignalInput( Id = nameof( ShootInput ), Default = true ), Description( "#weapon.setting.primary.description" )]
	public void ShootSignal( SignalEvent input )
	{
		if ( HasOwner || !Networking.IsHost ) return;
		DispatchPrimary( input.Pressed, input.Down, input.Released );
	}

	/// <summary>
	/// Dispatches a secondary action from a connected signal.
	/// </summary>
	[SignalInput( Id = nameof( SecondaryInput ) ), Description( "#weapon.setting.secondary.description" )]
	public void SecondarySignal( SignalEvent input )
	{
		if ( HasOwner || !Networking.IsHost ) return;
		DispatchSecondary( input.Pressed, input.Down, input.Released );
	}

	void IPlayerControllable.OnControl()
	{
		if ( HasOwner ) return;
		OnContraptionControl();
	}

	protected virtual void OnContraptionControl()
	{
		DispatchPrimary( ShootInput.Pressed(), ShootInput.Down(), ShootInput.Released() );
		DispatchSecondary( SecondaryInput.Pressed(), SecondaryInput.Down(), SecondaryInput.Released() );
	}

	private void DispatchPrimary( bool pressed, bool down, bool released )
	{
		if ( pressed ) OnPrimaryPressed();
		if ( down ) OnPrimaryDown();
		if ( released ) OnPrimaryReleased();
	}

	private void DispatchSecondary( bool pressed, bool down, bool released )
	{
		if ( pressed ) OnSecondaryPressed();
		if ( down ) OnSecondaryDown();
		if ( released ) OnSecondaryReleased();
	}

	protected virtual void OnPrimaryPressed() { }
	protected virtual void OnPrimaryDown() => FirePrimary();
	protected virtual void OnPrimaryReleased() { }

	protected virtual void OnSecondaryPressed() { }
	protected virtual void OnSecondaryDown() => FireSecondary();
	protected virtual void OnSecondaryReleased() { }

	/// <summary>
	/// Called when setting up the camera - use this to apply effects on the camera based on this carriable
	/// </summary>
	/// <param name="player"></param>
	/// <param name="camera"></param>
	public virtual void OnCameraSetup( Player player, Sandbox.CameraComponent camera )
	{
	}

	/// <summary>
	/// Can directly influence the player's eye angles here
	/// </summary>
	/// <param name="player"></param>
	/// <param name="angles"></param>
	public virtual void OnCameraMove( Player player, ref Angles angles )
	{
	}

	// The view the gun is placed at - captured before the camera bone offsets the view, so authored
	// camera animation moves the camera around the gun, not the gun with it.
	CameraView _viewModelView;

	/// <summary>
	/// Extra view-space offset applied when placing the view model.
	/// </summary>
	protected virtual Vector3 ViewModelOffset => Vector3.Zero;

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );

		_viewModelView = view;

		ViewModel?.GetComponentInChildren<ViewModel>()?.UpdateCameraBone( ref view );
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		var model = ViewModel?.GetComponentInChildren<ViewModel>();
		if ( !model.IsValid() ) return;

		var placed = _viewModelView;
		placed.Position += placed.Rotation * ViewModelOffset;
		placed.Rotation = LookAtCursor( placed.Rotation );

		model.Place( placed );
		model.FollowCameraEffects( camera, view );
	}

	Rotation _cursorLook = Rotation.Identity;

	/// <summary>
	/// Turns the view model toward the mouse cursor while the inspect menu is open, and back when it closes.
	/// </summary>
	Rotation LookAtCursor( Rotation view )
	{
		var target = InspectCursor.IsOpen ? view.Inverse * Rotation.LookAt( InspectCursor.Ray.Forward, view.Up ) : Rotation.Identity;
		_cursorLook = Rotation.Slerp( _cursorLook, target, Time.Delta * 10f );
		return view * _cursorLook;
	}

	/// <summary>
	/// Drops this weapon into the world as a pickup - the same live object, so it keeps its state
	/// (ammo, tool modes). Unparents it, drops network ownership, enables the dropped components and
	/// tags it cleanable. Host only.
	/// </summary>
	public void DropIntoWorld( Vector3 position, Vector3 velocity, Connection owner = null )
	{
		if ( !Networking.IsHost )
			return;

		Network.DropOwnership();

		GameObject.SetParent( null, true );
		WorldScale = GetPrefabWorldScale();
		GameObject.Enabled = true;
		WorldPosition = position;
		Slot = -1;

		Ownable.Set( GameObject, owner );
		GameObject.Tags.Add( "removable" );

		IsDropped = true;

		if ( GetComponent<Rigidbody>( true ) is { } body )
		{
			body.Velocity = velocity;
			body.AngularVelocity = Vector3.Random * 8.0f;
		}
	}

	/// <summary>
	/// The engine inventory is dropping this - throw the live object out in front of the dropper.
	/// </summary>
	protected override bool OnDrop()
	{
		var player = Owner;

		if ( player.IsValid() )
		{
			var eye = player.EyeTransform;
			var velocity = (player.Controller.IsValid() ? player.Controller.Velocity : Vector3.Zero)
				+ eye.Forward * 200f + Vector3.Up * 100f;

			DropIntoWorld( eye.Position + eye.Forward * 48f, velocity, player.Network.Owner );
		}
		else
		{
			DropIntoWorld( WorldPosition, Vector3.Up * 50f );
		}

		return true;
	}

	/// <summary>
	/// Is this item currently being used? When true, prevents auto-switching away on item pickup etc.
	/// </summary>
	public virtual bool IsInUse()
	{
		return false;
	}

	public virtual void OnPlayerDeath( PlayerDiedParams args )
	{
	}

	/// <summary>
	/// The player carrying us just jumped. Forwards to the view model so its anim graph can react.
	/// </summary>
	public virtual void OnJump()
	{
		ViewModel?.RunEvent<ViewModel>( x => x.Renderer.Set( "b_jump", true ) );
	}

	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( Hud.IsVisible( HudElement.Crosshair ) )
			DrawCrosshair( painter, crosshair );
	}
}
