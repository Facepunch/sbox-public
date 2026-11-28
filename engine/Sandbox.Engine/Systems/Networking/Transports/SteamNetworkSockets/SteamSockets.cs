using System.Collections.Concurrent;
using NativeEngine;
using System.Threading.Channels;

namespace Sandbox.Network;

internal static partial class SteamNetwork
{
	/// <summary>
	/// A socket that listens on a SteamId and virtual port.
	/// </summary>
	internal class IdListenSocket : Socket
	{
		readonly string address;
		public IdListenSocket( int virtualPort )
		{
			SteamNetwork.Initialize();

			InitHandle( SteamNative.NetworkingSockets.CreateListenSocketP2P( virtualPort ) );
			address = SteamNative.NetworkingSockets.GetIdentity();
		}

		public override string ToString() => $"SteamIdSocket - {address}";
	}

	/// <summary>
	/// A socket that listens on an IP address and port.
	/// </summary>
	internal class IpListenSocket : Socket
	{
		readonly string address;

		public IpListenSocket()
		{
			SteamNetwork.Initialize();

			InitHandle( Networking.HideAddress
				? SteamNative.NetworkingSockets.CreateListenSocketP2PFakeIP()
				: SteamNative.NetworkingSockets.CreateListenSocketIP( Networking.Port ) );
			address = SteamNative.NetworkingSockets.GetListenSocketAddress( handle );
		}

		public override string ToString() => $"SteamIpSocket - {address}";
	}

	/// <summary>
	/// A listen socket, one socket to many. We should really use this just for dedicated servers.
	/// </summary>
	internal abstract class Socket : NetworkSocket, IValid
	{
		protected HSteamListenSocket handle;
		protected HSteamNetPollGroup pollGroup;

		public bool IsValid => handle.Id != 0;

		public ConcurrentDictionary<uint, Connection> Connections { get; } = new();

		protected void InitHandle( HSteamListenSocket h )
		{
			handle = h;
			pollGroup = SteamNative.NetworkingSockets.CreatePollGroup();
			sockets[h] = this;
		}

		internal override void Dispose()
		{
			sockets.Remove( handle );
			OutgoingSteamMessage.Close( OutgoingMessages );
			IncomingSteamMessage.Close( IncomingMessages );

			SteamNative.NetworkingSockets.CloseListenSocket( handle );
			handle = default;

			SteamNative.NetworkingSockets.DestroyPollGroup( pollGroup );
			pollGroup = default;
		}

		internal void OnConnected( HSteamNetConnection connection )
		{
			Assert.False( Connections.ContainsKey( connection.Id ) );

			var c = new SocketConnection( this, connection );
			Connections[connection.Id] = c;
			OnClientConnect?.Invoke( c );

			SteamNative.NetworkingSockets.SetConnectionPollGroup( connection, pollGroup );
		}

		internal void OnDisconnected( HSteamNetConnection connection )
		{
			if ( !Connections.Remove( connection.Id, out var c ) )
				return;

			OnClientDisconnect?.Invoke( c );
			c.Close( 0, "Disconnect" );
		}

		private Channel<OutgoingSteamMessage> OutgoingMessages { get; } = Channel.CreateUnbounded<OutgoingSteamMessage>();
		private Channel<IncomingSteamMessage> IncomingMessages { get; } = Channel.CreateUnbounded<IncomingSteamMessage>();

		internal void SendMessage( HSteamNetConnection connection, NetPacket packet, NetFlags flags )
		{
			OutgoingSteamMessage.Enqueue( OutgoingMessages.Writer, connection, packet, flags );
		}

		internal override void ProcessMessagesInThread()
		{
			if ( !SteamNative.NetworkingSockets.IsAvailable() )
				return;

			OutgoingSteamMessage.SendQueued( OutgoingMessages.Reader );
			IncomingSteamMessage.Receive( IncomingMessages.Writer, pollGroup, SteamNative.NetworkingSockets.ReceiveMessagesOnPollGroup );
		}

		internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
		{
			while ( IncomingMessages.Reader.TryRead( out var msg ) )
			{
				Connections.TryGetValue( msg.Connection.Id, out var connection );
				msg.Dispatch( connection, handler );
			}
		}

		internal override void SetData( string key, string value )
		{
			DedicatedServer.SetData( key, value );
		}

		internal override void SetServerName( string name )
		{
			DedicatedServer.Name = name;
		}

		internal override void SetMapName( string name )
		{
			DedicatedServer.MapName = name;
		}
	}
}
