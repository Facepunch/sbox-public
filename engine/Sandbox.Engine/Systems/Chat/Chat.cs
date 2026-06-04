namespace Sandbox.Platform;

/// <summary>
/// Platform-level text chat system. Provides an API for sending and receiving chat messages to users.
/// </summary>
[Expose]
public static class Chat
{
	/// <summary>
	/// The rate limit between chat messages per user.
	/// </summary>
	internal static float RateLimit => 0.66f;

	/// <summary>
	/// Whether chat is enabled for the current game.
	/// </summary>
	public static bool Enabled => ProjectSettings.Platform.ChatEnabled;

	/// <summary>
	/// Whether the built-in chat UI overlay should be shown.
	/// </summary>
	public static bool ShowUI => ProjectSettings.Platform.ChatShowUI;

	/// <summary>
	/// Maximum length of a single chat message.
	/// </summary>
	public static int MaxMessageLength => ProjectSettings.Platform.ChatMaxMessageLength;

	/// <summary>
	/// Fired locally when a chat message is received or sent. Used by the menu overlay system.
	/// Game code should use <see cref="IChatEvent"/> scene events instead.
	/// </summary>
	internal static event Action<ChatMessageEvent> OnMessage;

	/// <summary>
	/// Says a message in chat. This will be sent to the host, validated, then filtered to clients.
	/// </summary>
	public static void Say( string message )
	{
		if ( !Enabled ) return;

		if ( string.IsNullOrWhiteSpace( message ) ) return;

		message = SanitizeMessage( message );

		if ( string.IsNullOrWhiteSpace( message ) ) return;

		if ( message.Length > MaxMessageLength )
			message = message[..MaxMessageLength];

		if ( Networking.IsHost )
		{
			OnHostReceive( new ChatMsg { Message = message }, Connection.Host, Connection.Host.Id );
			return;
		}

		Connection.Host?.SendMessage( new ChatMsg { Message = message } );
	}

	/// <summary>
	/// Dispatch a message locally. Applies Steam chat filter, fires the scene event, then the callback.
	/// </summary>
	internal static void Message( Connection sender, string message )
	{
		if ( sender is not null )
		{
			//
			// Check if the sender is blocked
			//
			var friend = new Friend( sender.SteamId );
			if ( friend.IsBlocked )
				return;
		}

		message = Utility.Steam.FilterChat( message, sender?.SteamId );

		var e = new ChatMessageEvent
		{
			Message = message,
			Sender = sender
		};

		DispatchSceneEvent( e );

		if ( e.Suppress )
			return;

		Log.Info( $"{sender?.Name ?? "Server"}: {e.Message}" );

		OnMessage?.Invoke( e );
	}

	/// <summary>
	/// Add a notification to the chat. This is local-only and has no sender.
	/// </summary>
	public static void AddText( string message )
	{
		if ( !Enabled ) return;
		if ( string.IsNullOrWhiteSpace( message ) ) return;

		var e = new ChatMessageEvent { Message = message };
		DispatchSceneEvent( e );

		if ( e.Suppress )
			return;

		Log.Info( $"{e.Message}" );

		OnMessage?.Invoke( e );
	}

	/// <summary>
	/// Broadcast a system notification to all connected clients. Host-only.
	/// </summary>
	internal static void BroadcastText( string message )
	{
		if ( !Networking.IsHost ) return;
		if ( string.IsNullOrWhiteSpace( message ) ) return;

		var broadcast = new ChatBroadcastMsg
		{
			Message = message,
			SenderId = Guid.Empty
		};

		Networking.System?.Broadcast( broadcast );

		// Show locally on the host too
		AddText( message );
	}

	/// <summary>
	/// Register network message handlers on the given network system.
	/// Called once during NetworkSystem construction.
	/// </summary>
	internal static void NetworkInitialize( SceneNetworkSystem system )
	{
		system.AddHandler<ChatMsg>( OnHostReceive );
		system.AddHandler<ChatBroadcastMsg>( OnClientReceive );
	}

	/// <summary>
	/// Strip control characters, zero-width, bidi, and other invisible/naughty characters.
	/// I wonder if this should maybe be a general utility method?
	/// </summary>
	static string SanitizeMessage( string message )
	{
		if ( string.IsNullOrEmpty( message ) )
			return message;

		var sb = new System.Text.StringBuilder( message.Length );

		foreach ( var c in message )
		{
			// Control chars (U+0000–U+001F, U+007F)
			if ( c <= '\u001F' || c == '\u007F' ) continue;

			// Zero-width and bidi overrides (U+200B–U+200F, U+202A–U+202E)
			if ( c >= '\u200B' && c <= '\u200F' ) continue;
			if ( c >= '\u202A' && c <= '\u202E' ) continue;

			// Line/paragraph separators (U+2028–U+2029)
			if ( c == '\u2028' || c == '\u2029' ) continue;

			// BOM (U+FEFF)
			if ( c == '\uFEFF' ) continue;

			sb.Append( c );
		}

		return sb.ToString().Trim();
	}

	/// <summary>
	/// Received on the host — validates and broadcasts to all other clients (given a filter)
	/// </summary>
	static void OnHostReceive( ChatMsg msg, Connection source, Guid guid )
	{
		if ( !Enabled ) return;
		if ( string.IsNullOrWhiteSpace( msg.Message ) ) return;

		if ( source is not null )
		{
			var lastChat = source.Get<RealTimeSince>( "LastTimeTalked" );
			if ( lastChat < RateLimit ) return;
			source.Set<RealTimeSince>( "LastTimeTalked", 0 );
		}

		var message = SanitizeMessage( msg.Message );
		if ( string.IsNullOrWhiteSpace( message ) ) return;

		if ( message.Length > MaxMessageLength )
			message = message[..MaxMessageLength];

		var e = new ChatMessageEvent
		{
			Message = message,
			Sender = source
		};

		DispatchSceneEvent( e );

		if ( e.Suppress )
			return;

		var broadcast = new ChatBroadcastMsg
		{
			Message = e.Message,
			SenderId = source?.Id ?? Guid.Empty
		};

		Connection.Filter? filter = e.RecipientFilter is not null
			? new Connection.Filter( Connection.Filter.FilterType.Include, c => e.RecipientFilter( c ) )
			: null;

		Networking.System?.Broadcast( broadcast, filter: filter );

		// Dedicated servers don't receive the broadcast, so log it to console here
		if ( Application.IsDedicatedServer )
		{
			Log.Info( $"{source?.Name ?? "Server"}: {e.Message}" );
		}

		OnMessage?.Invoke( e );
	}

	/// <summary>
	/// Received on clients — dispatches the message locally.
	/// </summary>
	static void OnClientReceive( ChatBroadcastMsg msg, Connection source, Guid guid )
	{
		var sender = Connection.Find( msg.SenderId );
		Message( sender, msg.Message );
	}

	/// <summary>
	/// Dispatch IChatEvent to the active game scene.
	/// </summary>
	static void DispatchSceneEvent( ChatMessageEvent e )
	{
		var scene = Application.GetActiveScene();
		if ( !scene.IsValid() ) return;

		scene.RunEvent<IChatEvent>( x => x.OnChatMessage( e ) );
	}
}

/// <summary>
/// Net message sent from a client to the host when they want to chat.
/// </summary>
[Expose]
internal struct ChatMsg
{
	public string Message { get; set; }
}

/// <summary>
/// Net message broadcast from the host to all clients with a validated chat message.
/// </summary>
[Expose]
internal struct ChatBroadcastMsg
{
	public string Message { get; set; }
	public Guid SenderId { get; set; }
}
