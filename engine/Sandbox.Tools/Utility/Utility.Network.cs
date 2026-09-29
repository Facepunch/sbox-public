using Sandbox.Network;
using System;
namespace Editor;

public static partial class EditorUtility
{
	public static partial class Network
	{
		/// <summary>
		/// True if the network system is active
		/// </summary>
		public static bool Active => Networking.System is not null;

		/// <summary>
		/// True if the network system is active and we're the host
		/// </summary>
		public static bool Hosting => Networking.System?.IsHost ?? false;

		/// <summary>
		/// True if the network system is active and we're the host
		/// </summary>
		public static bool Client => Networking.System?.IsClient ?? false;

		/// <summary>
		/// Determines who can join a lobby hosted from the editor. Should only be set
		/// before creating a lobby. Persists between lobbies.
		/// </summary>
		public static LobbyPrivacy HostPrivacy
		{
			get => Networking.EditorLobbyPrivacy;
			set => Networking.EditorLobbyPrivacy = value;
		}

		/// <summary>
		/// Disconnect from the current network session
		/// </summary>
		public static void Disconnect() => Networking.Disconnect();

		public static int InProcessClientCount => LocalClients.Count;

		public static void AddInProcessClient() => LocalClients.Add();

		public static void RemoveInProcessClients() => LocalClients.RemoveAll();

		/// <summary>
		/// Connenct to a network address
		/// </summary>
		public static void Connect( string address ) => Networking.Connect( address );

		/// <summary>
		/// Start hosting a lobby. If we're not already in play mode, we'll enter play mode first.
		/// </summary>
		public static void StartHosting()
		{
			if ( !Game.IsPlaying )
			{
				EditorScene.Play();
			}

			Networking.CreateLobby( new LobbyConfig() );
		}

		/// <summary>
		/// Return all of the channels on this connection. 
		/// If you're the host, it should return all client connections.
		/// If you're the client, it should return empty - unless you're in a p2p session (lobby).
		/// Returns empty if you're not connected
		/// </summary>
		public static Connection[] Channels
		{
			get
			{
				if ( Networking.System is null ) return Array.Empty<Connection>();

				return Networking.System.Connections.ToArray();
			}
		}

		/// <summary>
		/// Return all of the sockets on this connection. 
		/// If you're the host, it should return all active sockets.
		/// If you're the client, it should return empty - unless you're in a p2p session (lobby).
		/// Returns empty if you're not connected
		/// </summary>
		public static NetworkSocket[] Sockets
		{
			get
			{
				if ( Networking.System is null ) return Array.Empty<NetworkSocket>();

				return Networking.System.Sockets.ToArray();
			}
		}

	}
}
