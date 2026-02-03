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
		[Obsolete( "Use RejectConnection instead" )]
		bool AcceptConnection( Connection channel, ref string reason )
		{
			return true;
		}

		/// <summary>
		/// Called on the host to decide whether to accept a <see cref="Connection"/>.
		/// If any <see cref="Component"/> that implements this returns a reason to reject it,
		/// the connection will be denied.
		/// </summary>
		/// <param name="channel"></param>
		/// <returns>The reason to display to the client.</returns>
		async Task<string> RejectConnection( Connection channel )
		{
			return null;
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
		/// Called when the host of the game has left - and you are now the new host.
		/// </summary>
		void OnBecameHost( Connection previousHost )
		{

		}
	}
}
