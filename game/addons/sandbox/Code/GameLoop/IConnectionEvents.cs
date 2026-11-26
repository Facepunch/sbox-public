/// <summary>
/// Data passed to <see cref="Global.IConnectionEvents.OnConnecting"/>. Set <see cref="Cancelled"/> to true to turn
/// the connection away, with <see cref="Reason"/> shown to them.
/// </summary>
public class ConnectingEvent
{
	public Connection Client { get; init; }
	public string Reason { get; set; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Allows listening to players joining and leaving the server. Implement this on a <see cref="Component"/>.
/// All of these run on the host.
/// </summary>
public static partial class Global
{
	public interface IConnectionEvents : ISceneEvent<IConnectionEvents>
	{
		/// <summary>
		/// Called when someone tries to join, before they load in. Use it for whitelists, passwords or reserved slots.
		/// Bans are checked separately.
		/// </summary>
		void OnConnecting( ConnectingEvent e ) { }

		/// <summary>
		/// Called when someone has joined and their <see cref="PlayerData"/> exists, just before their first spawn.
		/// </summary>
		void OnPlayerJoined( PlayerData playerData ) { }

		/// <summary>
		/// Called when someone has left, before their <see cref="PlayerData"/> is destroyed. Their player may already be gone.
		/// </summary>
		void OnPlayerLeft( PlayerData playerData ) { }
	}
}
