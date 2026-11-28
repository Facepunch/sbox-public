using NativeEngine;
using Sandbox.Engine;
using Sandbox.SteamApi;
using System.Threading.Channels;

namespace Sandbox.Network;

internal static partial class SteamNetwork
{
	internal class AuthenticatedConnection : Connection
	{
		private uint AuthTicketHandle { get; set; }

		internal override bool OnReceiveServerInfo( ref UserInfo userInfo, ServerInfo info )
		{
			AuthTicketHandle = Services.Auth.GetAuthTicket( 0, out var authTicket );

			if ( authTicket != null && authTicket.Length != 0 )
			{
				userInfo.AuthTicket = authTicket;
				return true;
			}

			IGameInstanceDll.Current.Disconnect( "Invalid Authentication Ticket" );
			return false;
		}

		internal override void InternalClose( int closeCode, string closeReason )
		{
			base.InternalClose( closeCode, closeReason );

			if ( AuthTicketHandle == 0 )
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

			var handle = SteamNative.NetworkingSockets.ConnectP2P( steamId, virtualPort );
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

			var handle = SteamNative.NetworkingSockets.ConnectByIPAddress( address );
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

		// Steam keys validation callbacks by user, so only one socket can own their native session.
		static readonly Dictionary<ulong, SocketConnection> authSessions = new();
		ulong? authSessionId;
		TaskCompletionSource<(AuthResponse Result, ulong Owner)> awaitingTicket;

		internal static void OnValidateAuthTicket( ulong steamId, AuthResponse response, ulong owner )
		{
			if ( !authSessions.TryGetValue( steamId, out var socket ) ) return;
			if ( socket.awaitingTicket?.TrySetResult( (response, owner) ) == true ) return;

			// Steam also reports a ticket being revoked after the handshake has succeeded.
			if ( response != AuthResponse.OK )
				socket.Close( 0, $"Steam authentication ended: {response}" );
		}

		public override bool HasPermission( string permission )
		{
			// On a dedicated server, we'll allow the host to dictate user permissions.
			return AuthenticatedSteamId > 0 && UserPermission.Has( AuthenticatedSteamId, permission );
		}

		internal override async Task<bool> OnReceiveUserInfo( UserInfo info )
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

			if ( !await ValidateTicketAsync( info ) ) return false;

			// Let's see if we have another connection with this Steam Id. They might have
			// crashed, so let's kick the old one.
			var existingConnection = All.FirstOrDefault( c => c.Id != Id && c.SteamId == info.SteamId );
			existingConnection?.Close( 0, "Expired Session" );

			AuthenticatedSteamId = info.SteamId;
			return await base.OnReceiveUserInfo( info );
		}

		private async Task<bool> ValidateTicketAsync( UserInfo info )
		{
			if ( !IsValid ) return false;

			if ( awaitingTicket is not null || authSessionId.HasValue )
			{
				Close( 0, "Authentication already started on this connection" );
				return false;
			}

			// A reconnect takes over the previous socket's native session before starting its own.
			if ( authSessions.TryGetValue( info.SteamId, out var previous ) )
			{
				if ( previous.awaitingTicket is not null )
				{
					Close( 0, "Authentication already in progress" );
					return false;
				}

				previous.ReleaseAuthSession();
			}

			var completion = new TaskCompletionSource<(AuthResponse Result, ulong Owner)>( TaskCreationOptions.RunContinuationsAsynchronously );
			awaitingTicket = completion;
			authSessions.Add( info.SteamId, this );

			try
			{
				var result = Services.Auth.BeginAuthSession( info.SteamId, info.AuthTicket );
				if ( result != BeginAuthResult.OK )
				{
					authSessions.Remove( info.SteamId );
					Close( 0, result.ToString() );
					return false;
				}

				authSessionId = info.SteamId;
				var response = await completion.Task.WaitAsync( TimeSpan.FromSeconds( 30 ) );
				if ( !IsValid ) return false;

				if ( response.Result != AuthResponse.OK )
				{
					Close( 0, $"Auth Failed: {response.Result}" );
					return false;
				}

				OwnerSteamId = response.Owner;
				return true;
			}
			catch ( TimeoutException )
			{
				Close( 0, "Steam authentication timed out" );
				return false;
			}
			finally
			{
				awaitingTicket = null;
			}
		}

		private void ReleaseAuthSession()
		{
			if ( authSessionId is not ulong steamId ) return;

			authSessionId = null;
			authSessions.Remove( steamId );
			Services.Auth.EndAuthSession( steamId );
			awaitingTicket?.TrySetResult( (AuthResponse.AuthTicketCanceled, 0) );
		}

		internal override void InternalSend( NetPacket packet, NetFlags flags )
		{
			if ( !Socket.IsValid() )
				return;

			Socket.SendMessage( Handle, packet, flags );
		}

		internal override void InternalClose( int closeCode, string closeReason )
		{
			base.InternalClose( closeCode, closeReason );

			ReleaseAuthSession();
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

				cachedStats = SteamNative.NetworkingSockets.GetConnectionStats( Handle ).ForConnection( DisplayName );

				timeUntilFetchStats = 0.5f;

				return cachedStats;
			}
		}

		private Channel<OutgoingSteamMessage> OutgoingMessages { get; } = Channel.CreateUnbounded<OutgoingSteamMessage>();
		private Channel<IncomingSteamMessage> IncomingMessages { get; } = Channel.CreateUnbounded<IncomingSteamMessage>();

		internal override void ProcessMessagesInThread()
		{
			if ( !SteamNative.NetworkingSockets.IsAvailable() )
				return;

			MessagesSent += OutgoingSteamMessage.SendQueued( OutgoingMessages.Reader );
			MessagesRecieved += IncomingSteamMessage.Receive( IncomingMessages.Writer, handle, SteamNative.NetworkingSockets.ReceiveMessagesOnConnection );
		}

		internal override void InternalSend( NetPacket packet, NetFlags flags )
		{
			OutgoingSteamMessage.Enqueue( OutgoingMessages.Writer, handle, packet, flags );
		}

		internal override void InternalRecv( NetworkSystem.MessageHandler handler )
		{
			while ( IncomingMessages.Reader.TryRead( out var msg ) )
			{
				msg.Dispatch( this, handler );
			}
		}

		internal override void InternalClose( int closeCode, string closeReason )
		{
			// If we have no handle then we're already closed.
			if ( handle.Id == 0 )
				return;

			closeCode = Math.Clamp( closeCode, (int)NetConnectionEnd.App_Min, (int)NetConnectionEnd.Misc_Max );
			OutgoingSteamMessage.Close( OutgoingMessages );
			IncomingSteamMessage.Close( IncomingMessages );
			SteamNative.NetworkingSockets.CloseConnection( handle, closeCode, closeReason );

			handle = default;
		}
	}
}
