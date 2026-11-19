public sealed partial class Player : IPhysgunEvent
{
	[Title( "Physgun Players" ), Group( "Gameplay" ), ServerSettings.Section( "Players" ), Order( 5 )]
	[ConVar( "sandbox.allow.physgunplayers", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Players can grab other players with the physgun." )]
	public static bool AllowPhysgunPlayers { get; set; } = true;

	[Permission, Title( "Physgun Players" ), Description( "Grab other players with the physgun when it's off for everyone." )]
	public const string PhysgunPlayers = "sandbox.physgunplayers";

	public static bool CanPhysgunPlayers( Connection connection ) => Permissions.Allows( connection, AllowPhysgunPlayers, PhysgunPlayers );

	/// <summary>
	/// The physgun colour this player picked, as text, empty for the weapon's own. Their own client keeps it
	/// up to date from their preferences, so everyone draws their physgun in it.
	/// </summary>
	[Sync] public string PhysgunColor { get; private set; }

	/// <summary>
	/// The colour this player's physgun glows while gravity pulling, like <see cref="PhysgunColor"/>
	/// </summary>
	[Sync] public string PhysgunPullColor { get; private set; }

	void UpdatePhysgunColors()
	{
		PhysgunColor = GamePreferences.PhysgunColor;
		PhysgunPullColor = GamePreferences.PhysgunPullColor;
	}

	void IPhysgunEvent.OnPhysgunGrab( IPhysgunEvent.GrabEvent e )
	{
		if ( !CanPhysgunPlayers( e.Grabber ) )
			e.Cancelled = true;
	}
}
