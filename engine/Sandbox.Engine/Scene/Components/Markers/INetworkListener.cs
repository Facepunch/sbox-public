namespace Sandbox;

// TODO: where does this go?
public struct ConnectionRequestResult
{
	public bool IsAccepted;
	public string Reason;

	public static ConnectionRequestResult Accept() => new()
	{
		IsAccepted = true,
	};

	public static ConnectionRequestResult Reject( string reason ) => new()
	{
		IsAccepted = false,
		Reason = reason,
	};

	public static implicit operator bool( ConnectionRequestResult result ) => result.IsAccepted;
}

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
		[Obsolete( $"Use the other overload" )]
		bool AcceptConnection( Connection channel, ref string reason )
		{
			return true;
		}

		/// <summary>
		/// Called on the host to decide whether to accept a <see cref="Connection"/>.
		/// If any <see cref="Component"/> that implements this returns a rejection, the connection will be denied.
		/// </summary>
		/// <param name="channel"></param>
		/// <returns></returns>
		async Task<ConnectionRequestResult> AcceptConnection( Connection channel )
		{
			return ConnectionRequestResult.Accept();
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
