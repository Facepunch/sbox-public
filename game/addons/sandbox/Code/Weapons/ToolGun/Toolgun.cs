public partial class Toolgun : ScreenWeapon
{
	public Toolgun()
	{
		// Tools don't use ammo (the engine default is on, which would show an ammo indicator).
		UsesAmmo = false;
	}

	public override void OnCameraMove( Player player, ref Angles angles )
	{
		base.OnCameraMove( player, ref angles );

		var currentMode = GetCurrentMode();
		if ( currentMode is { AbsorbMouseInput: true } )
		{
			angles = default;
		}

		currentMode?.OnCameraMove( player, ref angles );
	}

	protected override void OnAdded( Sandbox.BaseInventoryComponent inventory )
	{
		base.OnAdded( inventory );

		CreateToolComponents();
	}

	/// <summary>
	/// Adds missing tool modes and selects Rope if the gun has no active mode.
	/// </summary>
	public void CreateToolComponents()
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "CreateToolComponents should be called on the host" );
			return;
		}

		foreach ( var mode in Game.TypeLibrary.GetTypes<ToolMode>() )
		{
			if ( mode.IsAbstract ) continue;

			if ( Components.Get( mode.TargetType, FindMode.EverythingInSelf ) is null )
			{
				Components.Create( mode, false );
			}
		}

		if ( GetCurrentMode() is null )
			GetMode<RopeTool>().Enabled = true;

		Network.Refresh( GameObject );
	}

	protected override void OnControl()
	{
		var currentMode = GetCurrentMode();
		if ( currentMode == null )
			return;

		UpdateToolLook();
		currentMode.UpdateSnapTarget();
		currentMode.OnControl();

		UpdateViewmodelScreen();

		ApplyCoilSpin();
	}

	Angles? _lastCursorAim;
	internal Angles ToolLook { get; private set; }

	void UpdateToolLook()
	{
		ToolLook = Input.AnalogLook;
		if ( !InspectCursor.IsOpen )
		{
			_lastCursorAim = null;
			return;
		}

		// The camera stays still in the menu. Tools that rotate or resize use the cursor's change in aim.
		var aim = Rotation.LookAt( InspectCursor.Ray.Forward ).Angles();
		ToolLook = (aim - (_lastCursorAim ?? aim)).Normal;
		_lastCursorAim = aim;
	}

	/// <summary>
	/// Draws the active tool mode's HUD, its crosshair.
	/// </summary>
	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( !Hud.IsVisible( HudElement.Crosshair ) ) return;

		var currentMode = GetCurrentMode();
		currentMode?.DrawHud( painter, crosshair );
	}

	/// <summary>
	/// Returns the enabled tool mode, or null if the gun has no active mode or has been removed.
	/// </summary>
	public ToolMode GetCurrentMode()
	{
		if ( GameObject is null || GameObject.IsDestroyed ) return null;

		return Components.FirstOrDefault( x => x is ToolMode { Enabled: true } ) as ToolMode;
	}

	public T GetMode<T>() where T : ToolMode
	{
		return GetComponent<T>( true );
	}

	[Rpc.Host]
	public void SetToolMode( string name )
	{
		if ( !Rpc.Caller.IsHost && Rpc.Caller != Network.Owner ) return;

		var targetMode = Game.TypeLibrary.GetType<ToolMode>( name );
		if ( targetMode == null )
		{
			Log.Warning( $"Unknown Mode {name}" );
			return;
		}

		var newMode = GetComponents<ToolMode>( true ).Where( x => x.GetType() == targetMode.TargetType ).FirstOrDefault();
		if ( newMode == null )
		{
			// Newly hotloaded tools aren't on existing toolguns yet. Create the requested
			// mode disabled, then switch normally so only one mode remains active.
			newMode = Components.Create( targetMode, false ) as ToolMode;
			if ( newMode is null ) return;
		}

		var currentMode = GetCurrentMode();

		if ( newMode == currentMode )
			return;

		currentMode?.Enabled = false;
		newMode.Enabled = true;

		GameObject.Enabled = true;
		Network.Refresh( GameObject );

		using ( Rpc.FilterInclude( Rpc.Caller ) )
		{
			BroadcastSwitchToolMode();
		}
	}

	[Rpc.Host]
	internal static void SetSpawner( string type, string path, string metadata = null )
	{
		var player = Player.FindForConnection( Rpc.Caller );
		if ( player is null ) return;

		var inventory = player.GetComponent<PlayerInventory>();
		if ( !inventory.IsValid() ) return;

		ISpawner s = ISpawner.Create( type, path, metadata: metadata );
		if ( s is null ) return;

		if ( !inventory.HasWeapon<Toolgun>() )
		{
			inventory.Pickup( "weapons/toolgun/toolgun.prefab", false );
		}

		var toolgun = inventory.GetWeapon<Toolgun>();
		if ( !toolgun.IsValid() ) return;

		var mode = toolgun.GetMode<SpawnerTool>();
		if ( !mode.IsValid() ) return;

		mode.SetSpawner( s );
		inventory.SetToolMode( mode.TypeDescription.ClassName );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BroadcastSwitchToolMode()
	{
		SwitchToolMode();
	}
}
