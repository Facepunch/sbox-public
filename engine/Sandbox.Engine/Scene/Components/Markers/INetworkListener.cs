namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// A <see cref="Component"/> with this interface can react to network events.
	/// </summary>
	public interface INetworkListener
	{
		/// <summary>
		/// Called on the host to decide whether to accept a <see cref="Connection"/>. If any <see cref="Component"/>
		/// that implements this returns false, the connection will be denied.
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="reason">The reason to display to the client.</param>
		bool AcceptConnection( Connection channel, ref string reason )
		{
			return true;
		}

		/// <summary>
		/// Called when someone joins the server. This will only be called for the host.
		/// </summary>
		void OnConnected( Connection channel )
		{

		}

		/// <summary>
		/// Called when someone leaves the server. This will only be called for the host.
		/// </summary>
		void OnDisconnected( Connection channel )
		{

		}

		/// <summary>
		/// Called when someone is all loaded and entered the game. This will only be called for the host.
		/// </summary>
		void OnActive( Connection channel )
		{

		}

		/// <summary>
		/// The host left and you are the new host. The scene is already rebuilt from their snapshot;
		/// <see cref="OnDisconnected"/> for them follows.
		/// </summary>
		void OnBecameHost( Connection previousHost )
		{

		}

		/// <summary>
		/// Another player took over as host and the scene was rebuilt from their snapshot. The new
		/// host gets <see cref="OnBecameHost"/> instead.
		/// </summary>
		void OnHostChanged( Connection previousHost, Connection newHost )
		{

		}
	}
}
