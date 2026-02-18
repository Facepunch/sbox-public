using System.Collections.Concurrent;
using NativeEngine;
using Sandbox.Internal;
using Steamworks;
using Steamworks.Data;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Sandbox.Network;

internal static partial class SteamNetwork
{
	internal class AuthenticatedConnection : Connection
	{
		private HAuthTicket AuthTicketHandle { get; set; }

		internal override bool OnReceiveServerInfo( ref UserInfo userInfo, ServerInfo info )
		{
			AuthTicketHandle = Services.Auth.GetAuthTicket( 0, out var authTicket );

			if ( authTicket != null && authTicket.Length != 0 )
			{
				userInfo.AuthTicket = authTicket;
				return true;
			}

			IMenuSystem.ShowServerError( "Disconnected", "Invalid Authentication Ticket" );
			Log.Warning( "Disconnecting - Invalid Authentication Ticket" );

			return false;
		}

		internal override void InternalClose( int closeCode, string closeReason )
		{
			base.InternalClose( closeCode, closeReason );

			if ( AuthTicketHandle.Value <= 0 )
				return;

			Services.Auth.CancelAuthTicket( AuthTicketHandle );
			AuthTicketHandle = default;
		}
	}

	/// <summary>
	/// A connection to a SteamId. This can be a SteamId of an individual Steam user
	/// or an assigned SteamId for a game server.
	/// </summary>
	internal class IdConnection : AuthenticatedConnection
	{
		public override string Address => ConnectedSteamId.ToString();
		public override bool IsHost => true;

		private ulong ConnectedSteamId { get; set; }

		public IdConnection( ulong steamId, int virtualPort )
		{
			Initialize();

			var handle = Glue.Networking.ConnectToSteamId( steamId, virtualPort );
			InitHandle( handle );
			ConnectedSteamId = steamId;
		}
	}

	/// <summary>
	/// A connection to an IP and port.
	/// </summary>
	internal class IpConnection : AuthenticatedConnection
	{
		public override string Address => ConnectedIpAddress;
		public override bool IsHost => true;

		private string ConnectedIpAddress { get; set; }

		public IpConnection( string address )
		{
			Initialize();

			var handle = Glue.Networking.ConnectToIpAddress( address );
			InitHandle( handle );
			ConnectedIpAddress = address;
		}
	}

	/// <summary>
	/// A connection from a listen socket.
	/// </summary>
	internal class SocketConnection : Connection
	{
		private Socket Socket { get; set; }

		public SocketConnection( Socket socket, HSteamNetConnection handle )
		{
			Socket = socket;
			InitHandle( handle );
		}

		private ulong AuthenticatedSteamId { get; set; }

		public override bool HasPermission( string permission )
		{
			// On a dedicated server, we'll allow the host to dictate user permissions.
			return AuthenticatedSteamId > 0 && UserPermission.Has( AuthenticatedSteamId, permission );
		}

		internal override bool OnReceiveUserInfo( UserInfo info )
		{
			if ( info.AuthTicket == null || info.AuthTicket.Length == 0 )
			{
				Close( (int)NetConnectionEnd.Misc_SteamConnectivity, "Invalid Auth Ticket" );
				return false;
			}

			var currentPlayerCount = All.Count( c => c.State >= ChannelState.Welcome );
			var maxPlayers = System.Config.MaxPlayers;

			if ( currentPlayerCount >= maxPlayers )
			{
				Close( (int)NetConnectionEnd.App_Generic, "Server Full" );
				return false;
			}

			// Let's end any existing auth session with this user first. Maybe they crashed while
			// the session was active. It doesn't hurt.
			Services.Auth.EndAuthSession( info.SteamId );

			// We need to check if the client has a valid authentication ticket.
			var result = Services.Auth.BeginAuthSession( info.SteamId, info.AuthTicket );
			if ( result != BeginAuthResult.OK )
			{
				Close( 0, result.ToString() );
				return false;
			}

			// Let's see if we have another connection with this Steam Id. They might have
			// crashed, so let's kick the old one.
			var existingConnection = All.FirstOrDefault( c => c.Id != Id && c.SteamId == info.SteamId );
			existingConnection?.Close( 0, "Expired Session" );

			AuthenticatedSteamId = info.SteamId;
			return true;
		}

		internal override void InternalSend( ByteStream stream, NetFlags flags )
		{
			if ( !Socket.IsValid() )
				return;

			byte[] data = Networking.EncodeStream( stream );
			Socket.SendMessage( Handle, data, flags.ToSteamFlags() );
		}

		internal override void InternalClose( int closeCode, string closeReason )
		{
			base.InternalClose( closeCode, closeReason );

			if ( AuthenticatedSteamId > 0 )
			{
				Services.Auth.EndAuthSession( AuthenticatedSteamId );
			}
		}
	}

	internal unsafe class Connection : Sandbox.Connection, IValid
	{
		public bool IsValid => handle.Id > 0;

		protected HSteamNetConnection Handle => handle;
		HSteamNetConnection handle;

		protected void InitHandle( HSteamNetConnection h )
		{
			handle = h;
		}

		public string Description => Glue.Networking.GetConnectionDescription( handle );
		public string Identity => $"{Glue.Networking.GetConnectionSteamId( handle )}";

		ConnectionStatus Status => (ConnectionStatus)Glue.Networking.GetConnectionState( handle );

		public override string Name => Identity;

		public enum ConnectionStatus
		{
			None = 0,
			Connecting = 1,
			FindingRoute = 2,
			Connected = 3,
			ClosedByPeer = 4,
			ProblemDetectedLocally = 5,
			FinWait = -1,
			Linger = -2,
			Dead = -3
		}

		public override void Kick( string reason )
		{
			if ( !System.IsHost )
				return;

			if ( string.IsNullOrWhiteSpace( reason ) )
				reason = "Kicked";

			Close( (int)NetConnectionEnd.App_Generic, reason );
		}

		private RealTimeUntil timeUntilFetchStats;
		private ConnectionStats cachedStats;

		public override ConnectionStats Stats
		{
			get
			{
				if ( !timeUntilFetchStats )
					return cachedStats;

				var net = Steam.SteamNetworkingSockets();
				var info = net.GetConnectionInfo( Handle );

				cachedStats = new( DisplayName )
				{
					Ping = info.m_nPing,
					InBytesPerSecond = info.m_flInBytesPerSec,
					InPacketsPerSecond = info.m_flInPacketsPerSec,
					OutBytesPerSecond = info.m_flOutBytesPerSec,
					OutPacketsPerSecond = info.m_flOutPacketsPerSec,
					SendRateBytesPerSecond = info.m_nSendRateBytesPerSecond,
					ConnectionQuality = info.m_flConnectionQualityLocal
				};

				timeUntilFetchStats = 0.5f;

				return cachedStats;
			}
		}

		private Channel<OutgoingSteamMessage> OutgoingMessages { get; } = Channel.CreateUnbounded<OutgoingSteamMessage>();
		private Channel<IncomingSteamMessage> IncomingMessages { get; } = Channel.CreateUnbounded<IncomingSteamMessage>();

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

			while ( OutgoingMessages.Reader.TryRead( out var msg ) )
			{
				var gcHandle = GCHandle.Alloc( msg.Data, GCHandleType.Pinned );
				var dataPtr = gcHandle.AddrOfPinnedObject();

				PinnedBuffers[dataPtr] = gcHandle;

				var steamMsgPtr = Glue.Networking.AllocateMessageWithManagedBuffer( msg.Connection, dataPtr, msg.Data.Length, msg.Flags );
				if ( steamMsgPtr == IntPtr.Zero )
				{
					// Allocation failed, clean up immediately
					PinnedBuffers.TryRemove( dataPtr, out _ );
					gcHandle.Free();
					continue;
				}

				_messageBatch[batchCount++] = steamMsgPtr;

				if ( batchCount >= MaxBatchSize )
				{
					FlushBatch( ref batchCount );
				}
			}

			// Send any remaining messages
			if ( batchCount > 0 )
			{
				FlushBatch( ref batchCount );
			}

			ProcessIncomingMessages( net );
		}

		/// <summary>
		/// Process any incoming messages from Steam networking and enqueue them to be
		/// handled by the main thread.
		/// </summary>
		/// <param name="net"></param>
		private void ProcessIncomingMessages( ISteamNetworkingSockets net )
		{
			var ptr = stackalloc IntPtr[Networking.MaxIncomingMessages];

			while ( true )
			{
				var count = Glue.Networking.GetConnectionMessages( handle, (nint)ptr, Networking.MaxIncomingMessages );
				if ( count == 0 ) return;

				for ( var i = 0; i < count; i++ )
				{
					var msg = Unsafe.Read<SteamNetworkMessage>( (void*)ptr[i] );
					var data = new byte[msg.Size];
					Marshal.Copy( (IntPtr)msg.Data, data, 0, data.Length );

					var m = new IncomingSteamMessage { Connection = msg.Connection, Data = data };

					IncomingMessages.Writer.TryWrite( m );
					net.ReleaseMessage( ptr[i] );

					MessagesRecieved++;
				}
			}
		}

		private void FlushBatch( ref int count )
		{
			fixed ( IntPtr* pMessages = _messageBatch )
			{
				Glue.Networking.SendMessages( (IntPtr)pMessages, count );
			}

			MessagesSent += count;
			count = 0;
		}

		internal override void InternalSend( ByteStream stream, NetFlags flags )
		{
			byte[] output = Networking.EncodeStream( stream );

			var message = new OutgoingSteamMessage
			{
				Connection = handle,
				Data = output,
				Flags = flags.ToSteamFlags()
			};

			OutgoingMessages.Writer.TryWrite( message );
		}

		internal override void InternalRecv( NetworkSystem.MessageHandler handler )
		{
			while ( IncomingMessages.Reader.TryRead( out var msg ) )
			{
				Span<byte> output = Networking.DecodeStream( msg.Data );

				using ByteStream stream = ByteStream.CreateReader( output );

				var nwm = new NetworkSystem.NetworkMessage
				{
					Data = stream,
					Source = this
				};

				handler( nwm );
			}
		}

		internal override void InternalClose( int closeCode, string closeReason )
		{
			// If we have no handle then we're already closed.
			if ( handle.Id == 0 )
				return;

			closeCode = Math.Clamp( closeCode, (int)NetConnectionEnd.App_Min, (int)NetConnectionEnd.Misc_Max );
			Glue.Networking.CloseConnection( handle, closeCode, closeReason );

			handle = default;
		}
	}
}
