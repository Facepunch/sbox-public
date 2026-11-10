using System.Threading;

namespace Sandbox.Twitch;

internal class TwitchClient
{
	internal const string EndpointURL = "wss://irc-ws.chat.twitch.tv:443";

	/// <summary>
	/// How long to wait for the IRC handshake (RPL_004) before giving up on a connect attempt and
	/// retrying. A server that opens the socket but never registers us would otherwise hang us forever.
	/// </summary>
	private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds( 10 );

	private Engine.WebSocket _webSocket;
	internal string Username;
	internal string UserId;
	private bool _reconnect;
	private bool _fullyConnected;
	private bool _connecting;
	private CancellationTokenSource _connectCancel;

	/// <summary>
	/// Origin ids of community/mystery gifts we've already surfaced as a group, mapped to how many
	/// individual subgift events we still expect to follow and swallow - so a community gift fires one
	/// group event rather than that plus N individual ones.
	/// </summary>
	private readonly Dictionary<string, int> _communityGiftRemaining = new();

	public async Task<bool> Connect()
	{
		// One connect sequence at a time. The reconnect path (WebSocket_OnDisconnected) can fire while
		// a sequence is already retrying - let the running one handle it rather than spawning a second.
		if ( _webSocket != null || _connecting )
			return false;

		_connecting = true;
		try
		{
			Username = Engine.Streamer.Username;
			UserId = Engine.Streamer.UserId;

			// A fresh cancellation source for this sequence. Disconnect() cancels it, which is how we
			// break out of the retry loop even while we're between attempts with no socket to dispose.
			_connectCancel?.Dispose();
			_connectCancel = new CancellationTokenSource();
			var cancel = _connectCancel.Token;

			while ( !cancel.IsCancellationRequested )
			{
				try
				{
					if ( await TryConnect( cancel ) )
						return true;
				}
				catch ( Exception e )
				{
					Log.Warning( e, $"Failed to connect to {EndpointURL}, trying again in 1 second" );
				}

				// This attempt didn't take - drop its socket and wait a moment before retrying,
				// unless a disconnect cancelled us in the meantime.
				CloseSocket();

				try
				{
					await Task.Delay( 1000, cancel );
				}
				catch ( OperationCanceledException )
				{
					break;
				}
			}

			return false;
		}
		finally
		{
			_connecting = false;
		}
	}

	/// <summary>
	/// Make a single connect attempt: open the socket, send the IRC handshake, and wait for the server
	/// to register us (RPL_004) within <see cref="HandshakeTimeout"/>. Returns true once fully
	/// connected; false if the attempt was cancelled, dropped, or timed out waiting for the handshake.
	/// </summary>
	private async Task<bool> TryConnect( CancellationToken cancel )
	{
		_fullyConnected = false;

		var socket = new Engine.WebSocket();
		socket.OnDisconnected += WebSocket_OnDisconnected;
		socket.OnMessageReceived += WebSocket_OnMessageReceived;
		_webSocket = socket;

		await socket.Connect( EndpointURL );

		await socket.Send( "CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership" );
		await socket.Send( $"PASS oauth:{Engine.Streamer.Token}" );
		await socket.Send( $"NICK {Username}" );

		// Wait for RPL_004 (which sets _fullyConnected). The linked source trips on either a disconnect
		// (cancel) or the handshake timeout - both mean this attempt failed.
		using var handshake = CancellationTokenSource.CreateLinkedTokenSource( cancel );
		handshake.CancelAfter( HandshakeTimeout );

		while ( !_fullyConnected )
		{
			// The socket dropped and WebSocket_OnDisconnected nulled it out from under us.
			if ( _webSocket != socket )
				return false;

			try
			{
				await Task.Delay( 100, handshake.Token );
			}
			catch ( OperationCanceledException )
			{
				return false;
			}
		}

		return true;
	}

	public void Disconnect()
	{
		Disconnect( false );
	}

	internal void Disconnect( bool reconnect )
	{
		_reconnect = reconnect;
		Username = null;

		// Cancel any in-flight connect first - it may be sitting in the retry delay with no socket yet,
		// so cancelling the token (not just disposing the socket) is what actually stops it. Guard the
		// narrow race where a concurrent reconnect has just disposed and replaced the source.
		try
		{
			_connectCancel?.Cancel();
		}
		catch ( ObjectDisposedException )
		{
		}

		CloseSocket();
	}

	/// <summary>
	/// Tear down the current socket, if any: unhook our handlers first so its disposal doesn't trigger
	/// a reconnect, then dispose it.
	/// </summary>
	private void CloseSocket()
	{
		var socket = _webSocket;
		if ( socket == null )
			return;

		_webSocket = null;
		socket.OnDisconnected -= WebSocket_OnDisconnected;
		socket.OnMessageReceived -= WebSocket_OnMessageReceived;
		socket.Dispose();
	}

	private void IRC_OnConnected()
	{
		_reconnect = true;
		_fullyConnected = true;
		JoinChannel( Username );
	}

	private async void JoinChannel( string channel )
	{
		if ( _webSocket == null )
			return;

		if ( string.IsNullOrEmpty( channel ) )
			return;

		await _webSocket.Send( $"JOIN #{channel.ToLower()}" );
	}

	private void IRC_OnMessage( IrcMessage ircMessage )
	{
		var message = ircMessage.Message;
		if ( string.IsNullOrWhiteSpace( message ) )
			return;

		var userId = ircMessage.GetTag( "user-id" );
		var displayName = ircMessage.GetTag( "display-name" );
		var color = ircMessage.GetTag( "color" );
		var badgesTag = ircMessage.GetTag( "badges" );
		var badges = string.IsNullOrEmpty( badgesTag ) ? null : badgesTag.Split( ',' );
		var bits = ParseInt( ircMessage.GetTag( "bits" ) );
		var isFirstMessage = ircMessage.GetTag( "first-msg" ) == "1";

		Engine.Streamer.OnUserMessage( ircMessage.Channel, ircMessage.User, userId, displayName, color, badges, message, bits, isFirstMessage );
	}

	private void IRC_OnNotice( IrcMessage message )
	{
		// Twitch reports a bad oauth token as a NOTICE - there's no point reconnecting on that.
		if ( message.Message is not null && message.Message.Contains( "authentication failed", StringComparison.OrdinalIgnoreCase ) )
			Disconnect( false );
	}

	private void IRC_OnReconnect()
	{
		// Twitch periodically asks clients to reconnect (server maintenance). Drop the socket and
		// reconnect immediately; CloseSocket unhooks the disconnect handler, so this won't also trip the
		// drop-detection reconnect in WebSocket_OnDisconnected.
		CloseSocket();
		_ = Connect();
	}

	/// <summary>
	/// USERNOTICE carries channel events - subs, resubs, gift subs, community gifts and raids - keyed
	/// by the "msg-id" tag, with the details in "msg-param-*" tags. We route each to the matching
	/// engine event. The acting user (subscriber / gifter / raider) is in the "login" / "display-name"
	/// tags; an anonymous gifter shows up with the "ananonymousgifter" login.
	/// </summary>
	private void IRC_OnUserNotice( IrcMessage message )
	{
		var msgId = message.GetTag( "msg-id" );
		if ( string.IsNullOrEmpty( msgId ) )
			return;

		var login = message.GetTag( "login" );
		var userId = message.GetTag( "user-id" );
		var displayName = message.GetTag( "display-name" );
		var color = message.GetTag( "color" );
		var badgesTag = message.GetTag( "badges" );
		var badges = string.IsNullOrEmpty( badgesTag ) ? null : badgesTag.Split( ',' );

		switch ( msgId )
		{
			case "sub":
			case "resub":
				Engine.Streamer.OnSubscribe(
					login, userId, displayName, color, badges,
					ParseSubTier( message.GetTag( "msg-param-sub-plan" ) ),
					ParseInt( message.GetTag( "msg-param-cumulative-months" ) ),
					ParseInt( message.GetTag( "msg-param-streak-months" ) ),
					msgId == "resub",
					message.Message );
				break;

			case "submysterygift":
			case "anonsubmysterygift":
				{
					var count = ParseInt( message.GetTag( "msg-param-mass-gift-count" ) );

					// A community gift is followed by one individual subgift per recipient, sharing this
					// origin id. Remember the batch so we can swallow those and surface only this group event.
					var originId = message.GetTag( "msg-param-origin-id" );
					if ( !string.IsNullOrEmpty( originId ) && count > 0 )
						_communityGiftRemaining[originId] = count;

					Engine.Streamer.OnGiftSubscriptions(
						login, userId, displayName, color, badges,
						ParseSubTier( message.GetTag( "msg-param-sub-plan" ) ),
						count,
						ParseInt( message.GetTag( "msg-param-sender-count" ) ),
						msgId == "anonsubmysterygift" || IsAnonymousGifter( login ) );
					break;
				}

			case "subgift":
			case "anonsubgift":
				// Skip the individual gifts that belong to a community gift we already surfaced as a group.
				if ( IsCommunityGiftChild( message ) )
					break;

				Engine.Streamer.OnGiftSubscribe(
					login, userId, displayName, color, badges,
					message.GetTag( "msg-param-recipient-user-name" ),
					message.GetTag( "msg-param-recipient-display-name" ),
					ParseSubTier( message.GetTag( "msg-param-sub-plan" ) ),
					ParseInt( message.GetTag( "msg-param-gift-months" ) ),
					msgId == "anonsubgift" || IsAnonymousGifter( login ) );
				break;

			case "raid":
				Engine.Streamer.OnRaid(
					message.GetTag( "msg-param-login" ),
					message.GetTag( "msg-param-displayName" ),
					ParseInt( message.GetTag( "msg-param-viewerCount" ) ) );
				break;
		}
	}

	/// <summary>
	/// Whether this subgift is one of the individual gifts that follow a community/mystery gift we've
	/// already surfaced as a group. Counts the batch down and forgets it once all its gifts are seen.
	/// </summary>
	private bool IsCommunityGiftChild( IrcMessage message )
	{
		var originId = message.GetTag( "msg-param-origin-id" );
		if ( string.IsNullOrEmpty( originId ) || !_communityGiftRemaining.TryGetValue( originId, out var remaining ) )
			return false;

		if ( remaining <= 1 )
			_communityGiftRemaining.Remove( originId );
		else
			_communityGiftRemaining[originId] = remaining - 1;

		return true;
	}

	private async void IRC_OnPing()
	{
		if ( _webSocket == null )
			return;

		await _webSocket.Send( "PONG" );
	}

	private void IRC_OnJoin( IrcMessage message )
	{
		Engine.Streamer.OnUserJoin( message.User );
	}

	private void IRC_OnNames( IrcMessage message )
	{
		// A NAMES reply (353) carries a space-separated list of users already in the channel,
		// sent when we join. Seed the roster with them so it's populated from the start.
		var names = message.Message;
		if ( string.IsNullOrWhiteSpace( names ) )
			return;

		Engine.Streamer.SeedViewers( names.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) );
	}

	private void IRC_OnPart( IrcMessage message )
	{
		Engine.Streamer.OnUserLeave( message.User );
	}

	private void HandleIRCMessage( IrcMessage message )
	{
		switch ( message.Command )
		{
			case IrcCommand.PrivMsg:
				IRC_OnMessage( message );
				break;
			case IrcCommand.Ping:
				IRC_OnPing();
				break;
			case IrcCommand.Notice:
				IRC_OnNotice( message );
				break;
			case IrcCommand.UserNotice:
				IRC_OnUserNotice( message );
				break;
			case IrcCommand.Reconnect:
				IRC_OnReconnect();
				break;
			case IrcCommand.Join:
				IRC_OnJoin( message );
				break;
			case IrcCommand.Part:
				IRC_OnPart( message );
				break;
			case IrcCommand.RPL_004:
				IRC_OnConnected();
				break;
			case IrcCommand.RPL_353:
				IRC_OnNames( message );
				break;
			case IrcCommand.Unknown:
			default:
				break;
		}
	}

	private void WebSocket_OnMessageReceived( string message )
	{
		// A single websocket frame can carry several CRLF-separated IRC lines - walk them with a
		// cursor, handing each line to IrcParser.Parse.
		var parse = new Parse( message );
		while ( !parse.IsEnd )
		{
			var line = parse.ReadUntilOrEnd( "\r\n", acceptNone: true );
			parse = parse.JumpToEndOfLine( afterNewline: true );

			if ( line.Length <= 1 )
				continue;

			HandleIRCMessage( IrcParser.Parse( line ) );
		}
	}

	private void WebSocket_OnDisconnected( int status, string reason )
	{
		if ( _webSocket == null )
			return;

		_webSocket = null;

		if ( reason == "Disposing" )
		{
			_reconnect = false;
		}

		if ( _reconnect )
		{
			_ = Connect();
		}
	}

	private static int ParseInt( string value ) => int.TryParse( value, out var n ) ? n : 0;

	private static bool IsAnonymousGifter( string login ) => string.Equals( login, "ananonymousgifter", StringComparison.OrdinalIgnoreCase );

	internal static Streamer.SubscriptionTier ParseSubTier( string subPlan ) => subPlan switch
	{
		"Prime" => Streamer.SubscriptionTier.Prime,
		"1000" => Streamer.SubscriptionTier.Tier1,
		"2000" => Streamer.SubscriptionTier.Tier2,
		"3000" => Streamer.SubscriptionTier.Tier3,
		_ => Streamer.SubscriptionTier.Tier1,
	};

}
