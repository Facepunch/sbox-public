public enum AdminAction
{
	Kick,
	Ban,
	Unban,
	ChangeSetting,
	ChangeMap
}

/// <summary>
/// Data passed to <see cref="Global.IAdminEvents"/>. In <see cref="Global.IAdminEvents.OnAdminAction"/>, set
/// <see cref="Cancelled"/> to true to stop it, or change <see cref="Value"/> to change a reason or map.
/// </summary>
public class AdminActionEvent
{
	public AdminAction Action { get; init; }

	/// <summary>
	/// Who did it - the admin who sent it, or the host itself for its own menus and console.
	/// </summary>
	public Connection Caller { get; init; } = Rpc.Calling ? Rpc.Caller : Connection.Local;

	/// <summary>
	/// The player kicked, banned or unbanned. They might not be connected.
	/// </summary>
	public SteamId SteamId { get; init; }

	/// <summary>
	/// The setting changed, for <see cref="AdminAction.ChangeSetting"/>.
	/// </summary>
	public string Setting { get; init; }

	/// <summary>
	/// The kick or ban reason, the map, or the setting's new value (which can't be changed here).
	/// </summary>
	public string Value { get; set; }

	public bool Cancelled { get; set; }

	/// <summary>
	/// Fire <see cref="Global.IAdminEvents.OnAdminAction"/>, returns false if a listener cancelled it.
	/// </summary>
	internal bool Run()
	{
		Game.ActiveScene?.RunEvent<Global.IAdminEvents>( x => x.OnAdminAction( this ) );
		return !Cancelled;
	}

	internal void Done() => Game.ActiveScene?.RunEvent<Global.IAdminEvents>( x => x.OnAdminActionDone( this ) );
}

/// <summary>
/// Allows listening to and stopping admin actions - kicks, bans, server settings and map changes. Use it for
/// audit logs or protecting admins from each other. All of these run on the host.
/// </summary>
public static partial class Global
{
	public interface IAdminEvents : ISceneEvent<IAdminEvents>
	{
		void OnAdminAction( AdminActionEvent e ) { }
		void OnAdminActionDone( AdminActionEvent e ) { }
	}
}
