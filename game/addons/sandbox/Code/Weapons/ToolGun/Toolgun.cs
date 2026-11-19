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

	public void CreateToolComponents()
	{
		if ( !Networking.IsHost )
		{
			Log.Warning( "CreateToolComponents should be called on the host" );
			return;
		}

		bool enabled = true;

		foreach ( var mode in Game.TypeLibrary.GetTypes<ToolMode>() )
		{
			if ( mode.IsAbstract ) continue;

			// the prefab has some already, holding their data
			var component = Components.Get( mode.TargetType, FindMode.EverythingInSelf ) ?? Components.Create( mode, enabled );
			enabled &= !component.Enabled;
		}

		Network.Refresh( GameObject );
	}

	protected override void OnControl()
	{
		var currentMode = GetCurrentMode();
		if ( currentMode == null )
			return;

		currentMode.OnControl();

		UpdateViewmodelScreen();

		ApplyCoilSpin();
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

	public ToolMode GetCurrentMode()
	{
		if ( GameObject.IsDestroyed ) return null;
		return Components.FirstOrDefault( x => x is ToolMode { Enabled: true } ) as ToolMode;
	}

	public T GetMode<T>() where T : ToolMode
	{
		return GetComponent<T>( true );
	}

	/// <summary>
	/// Switches the local player's toolgun to a random tool other than the current one.
	/// </summary>
	[ConCmd( "sandbox.toolgun.random", Help = "Switch the toolgun to a random tool" )]
	public static void RandomToolCommand()
	{
		var inventory = Player.FindLocalPlayer()?.GetComponent<PlayerInventory>();
		if ( !inventory.IsValid() ) return;

		var current = inventory.GetWeapon<Toolgun>()?.GetCurrentMode()?.GetType();
		var modes = Game.TypeLibrary.GetTypes<ToolMode>()
			.Where( x => !x.IsAbstract && !x.HasAttribute<HideAttribute>() && x.TargetType != current )
			.ToArray();

		if ( modes.Length == 0 ) return;

		inventory.SetToolMode( Random.Shared.FromArray( modes ).ClassName );
	}

	[Rpc.Host]
	public void SetToolMode( string name )
	{
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
