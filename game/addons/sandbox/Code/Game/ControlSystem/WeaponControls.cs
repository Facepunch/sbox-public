/// <summary>
/// Lets a seat or a contraption fire an engine weapon the sandbox did not write, such as a mounted game's: the same
/// inputs and signals <see cref="BaseSandboxWeapon"/> has, driving the weapon's FirePrimary and FireSecondary.
/// </summary>
[Title( "Weapon Controls" ), Group( "Sandbox" ), Icon( "sports_esports" )]
public sealed class WeaponControls : Component, IPlayerControllable
{
	/// <summary>The input that fires the primary attack when this weapon is controlled via a seat.</summary>
	[Property, ClientEditable, Group( "#entity.group.input" ), Title( "#input.action.attack1" ), Description( "#weapon.setting.primary.description" )]
	public ClientInput ShootInput { get; set; }

	/// <summary>The input that fires the secondary attack when this weapon is controlled via a seat.</summary>
	[Property, ClientEditable, Group( "#entity.group.input" ), Title( "#input.action.attack2" ), Description( "#weapon.setting.secondary.description" )]
	public ClientInput SecondaryInput { get; set; }

	BaseCombatWeapon Weapon => GetComponent<BaseCombatWeapon>();

	internal static void Attach( BaseCombatWeapon weapon )
	{
		if ( weapon is not BaseSandboxWeapon )
			weapon.GetOrAddComponent<WeaponControls>();
	}

	internal static void PrepareDroppedWeapon( BaseCombatWeapon weapon, Connection owner )
	{
		if ( !weapon.IsValid() || weapon is BaseSandboxWeapon )
			return;

		Attach( weapon );
		Ownable.Set( weapon.GameObject, owner );
		weapon.GameObject.Tags.Add( "removable" );
		weapon.GameObject.Network.Refresh();
	}

	/// <summary>Wired into a contraption: no pickup prompt, no touch pickup.</summary>
	public bool IsWired => ShootInput.IsEnabled || SecondaryInput.IsEnabled || GameObject.Root.GetComponentsInChildren<ManualLink>( true ).Any();

	/// <summary>A seated player can only control this while not holding a weapon of their own.</summary>
	public bool CanControl( Player player )
	{
		var inventory = player.GetComponent<PlayerInventory>();
		return inventory is null || !inventory.ActiveWeapon.IsValid();
	}

	[SignalInput( Id = nameof( ShootInput ), Default = true ), Description( "#weapon.setting.primary.description" )]
	public void ShootSignal( SignalEvent input ) => FireSignal( input, primary: true );

	[SignalInput( Id = nameof( SecondaryInput ) ), Description( "#weapon.setting.secondary.description" )]
	public void SecondarySignal( SignalEvent input ) => FireSignal( input, primary: false );

	void FireSignal( SignalEvent input, bool primary )
	{
		if ( !Networking.IsHost ) return;

		var weapon = Weapon;
		if ( !weapon.IsValid() ) return;

		var automatic = primary ? weapon.PrimaryAutomatic : weapon.SecondaryAutomatic;
		if ( automatic ? input.Down : input.Pressed )
			Fire( weapon, input.Instigator, primary );
	}

	void IPlayerControllable.OnControl()
	{
		var weapon = Weapon;
		if ( !weapon.IsValid() ) return;

		var player = ControlContext.Player;
		if ( weapon.PrimaryAutomatic ? ShootInput.Down() : ShootInput.Pressed() )
			Fire( weapon, player, primary: true );

		if ( weapon.IsValid() && (weapon.SecondaryAutomatic ? SecondaryInput.Down() : SecondaryInput.Pressed()) )
			Fire( weapon, player, primary: false );
	}

	void Fire( BaseCombatWeapon weapon, Player player, bool primary )
	{
		if ( weapon.IsHeld ) return;

		var attacker = player.IsValid() ? player.GameObject : null;
		if ( primary ) weapon.FirePrimary( attacker );
		else weapon.FireSecondary( attacker );
	}
}
