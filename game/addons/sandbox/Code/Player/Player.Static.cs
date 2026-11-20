public sealed partial class Player : Component, Component.IDamageable, PlayerController.IEvents, Global.ISaveEvents, IKillSource
{
	private static Player LocalPlayer { get; set; }
	public static Player FindLocalPlayer() => LocalPlayer;
	public static T FindLocalWeapon<T>() where T : BaseSandboxWeapon => FindLocalPlayer()?.GetComponentInChildren<T>( true );
	public static T FindLocalToolMode<T>() where T : ToolMode => FindLocalPlayer()?.GetComponentInChildren<T>( true );

	/// <summary>
	/// Find a player for this connection
	/// </summary>
	public static Player FindForConnection( Connection c )
	{
		return Game.ActiveScene.GetAll<Player>().FirstOrDefault( x => x.Network.Owner == c );
	}

	/// <summary>
	/// Get player from a connection id
	/// </summary>
	public static Player For( Guid playerId )
	{
		return Game.ActiveScene.GetAll<Player>().FirstOrDefault( x => x.Network.Owner?.Id == playerId );
	}
}
