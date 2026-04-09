using System.Collections.Concurrent;
using NativeEngine;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

			InitHandle( Glue.Networking.CreateSocket( virtualPort ) );
			address = Glue.Networking.GetIdentity();
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

			InitHandle( Glue.Networking.CreateIpBasedSocket( Networking.Port, Networking.HideAddress ) );
			address = Glue.Networking.GetSocketAddress( handle );
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
			pollGroup = Glue.Networking.CreatePollGroup();
			sockets[h] = this;
		}

		internal override void Dispose()
		{
			sockets.Remove( handle );

			Glue.Networking.CloseSocket( handle );
			handle = default;

			Glue.Networking.DestroyPollGroup( pollGroup );
			pollGroup = default;
		}

		internal void OnConnected( HSteamNetConnection connection )
		{
			Assert.False( Connections.ContainsKey( connection.Id ) );

			var c = new SocketConnection( this, connection );
			Connections[connection.Id] = c;
			OnClientConnect?.Invoke( c );

			Glue.Networking.SetPollGroup( connection, pollGroup );

			Log.Info( $"{this}: Connection ({c})" );
		}

		internal void OnDisconnected( HSteamNetConnection connection )
		{
			if ( !Connections.Remove( connection.Id, out var c ) )
				return;

			OnClientDisconnect?.Invoke( c );
			Log.Info( $"{this}: Disconnection ({c})" );
			c.Close( 0, "Disconnect" );
		}

		private Channel<OutgoingSteamMessage> OutgoingMessages { get; } = Channel.CreateUnbounded<OutgoingSteamMessage>();
		private Channel<IncomingSteamMessage> IncomingMessages { get; } = Channel.CreateUnbounded<IncomingSteamMessage>();

		internal void SendMessage( HSteamNetConnection connection, in byte[] data, int flags )
		{
			OutgoingMessages.Writer.TryWrite( new OutgoingSteamMessage
			{
				Connection = connection,
				Data = data,
				Flags = flags
			} );
		}

		private const int MaxBatchSize = 256;
		private readonly IntPtr[] _messageBatch = new IntPtr[MaxBatchSize];

		/// <summary>
		/// Send any queued outgoing messages and process any incoming messages to be queued for handling
		/// on the main thread.
		/// </summary>
		internal override void ProcessMessagesInThread()
		{
			var net = Steam.SteamNetworkingSockets();
			if ( !net.IsValid ) return;

			int batchCount = 0;
			var maxOutgoing = Networking.MaxOutgoingMessagesPerTick;
			var outgoingCount = 0;

			while ( OutgoingMessages.Reader.TryRead( out var msg ) )
			{
				// Let Steam allocate the native buffer so there's no need to pin managed arrays.
				var steamMsgPtr = Glue.Networking.AllocateMessage( msg.Connection, msg.Data.Length, msg.Flags );
				if ( steamMsgPtr == IntPtr.Zero )
					continue;

				var nativeBuffer = Glue.Networking.GetMessageDataBuffer( steamMsgPtr );
				Marshal.Copy( msg.Data, 0, nativeBuffer, msg.Data.Length );

				_messageBatch[batchCount++] = steamMsgPtr;

				if ( batchCount >= MaxBatchSize )
				{
					FlushBatch( ref batchCount );
				}

				if ( maxOutgoing > 0 && ++outgoingCount >= maxOutgoing )
					break;
			}

			// Send any remaining messages
			if ( batchCount > 0 )
			{
				FlushBatch( ref batchCount );
			}

			ProcessIncomingMessages( net );
		}

		private unsafe void FlushBatch( ref int count )
		{
			fixed ( IntPtr* pMessages = _messageBatch )
			{
				Glue.Networking.SendMessages( (IntPtr)pMessages, count );
			}

			count = 0;
		}

		/// <summary>
		/// Process any incoming messages from Steam networking and enqueue them to be
		/// handled by the main thread.
		/// </summary>
		/// <param name="net"></param>
		private unsafe void ProcessIncomingMessages( in ISteamNetworkingSockets net )
		{
			var ptr = stackalloc IntPtr[Networking.ReceiveBatchSize];
			var maxIncoming = Networking.ReceiveBatchSizePerTick;
			var totalReceived = 0;

			while ( true )
			{
				var count = Glue.Networking.GetPollGroupMessages( pollGroup, (IntPtr)ptr, Networking.ReceiveBatchSize );
				if ( count == 0 ) return;

				for ( var i = 0; i < count; i++ )
				{
					var msg = Unsafe.Read<SteamNetworkMessage>( (void*)ptr[i] );

					var data = GC.AllocateUninitializedArray<byte>( msg.Size );
					Marshal.Copy( (IntPtr)msg.Data, data, 0, data.Length );

					var m = new IncomingSteamMessage
					{
						Connection = msg.Connection,
						Data = data
					};

					IncomingMessages.Writer.TryWrite( m );
					net.ReleaseMessage( ptr[i] );
				}

				totalReceived += count;

				if ( maxIncoming > 0 && totalReceived >= maxIncoming )
					return;
			}
		}

		internal override void GetIncomingMessages( NetworkSystem.MessageHandler handler )
		{
			while ( IncomingMessages.Reader.TryRead( out var msg ) )
			{
				if ( !Connections.TryGetValue( msg.Connection.Id, out var connection ) )
					continue;

				connection.OnRawPacketReceived( msg.Data, handler );
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
