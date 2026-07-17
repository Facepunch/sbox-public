namespace Sandbox.Twitch;

/// <summary>
/// Parses raw Twitch IRC lines into an <see cref="IrcMessage"/>. The line format is IRCv3:
/// <c>[@tags] [:prefix] COMMAND [params] [:trailing]</c> - each section optional and space
/// separated. Scanning is done with <see cref="Parse"/>, our shared cursor-based text reader.
/// </summary>
internal static class IrcParser
{
	/// <summary>
	/// Parse a single IRC line (without its trailing CRLF).
	/// </summary>
	public static IrcMessage Parse( string raw )
	{
		var cursor = new Parse( raw );

		// @tag1=value1;tag2=value2 ...
		Dictionary<string, string> tags = null;
		if ( cursor.Is( '@' ) )
		{
			cursor.Pointer++;
			tags = ParseTags( cursor.ReadUntilOrEnd( " ", acceptNone: true ) );
			cursor.SkipWhitespaceAndNewlines();
		}

		// :nick!user@host
		string user = null;
		if ( cursor.Is( ':' ) )
		{
			cursor.Pointer++;
			user = ExtractNick( cursor.ReadUntilOrEnd( " ", acceptNone: true ) );
			cursor.SkipWhitespaceAndNewlines();
		}

		var command = ParseCommand( cursor.ReadUntilOrEnd( " ", acceptNone: true ) );
		cursor.SkipWhitespaceAndNewlines();

		// What's left is "[params] [:trailing]". The trailing text begins at the first ':', which
		// in Twitch's dialect can only ever be the trailing marker.
		var channel = ExtractChannel( cursor.ReadUntilOrEnd( ":", acceptNone: true ) );

		string message = null;
		if ( cursor.Current == ':' )
		{
			cursor.Pointer++;
			message = cursor.ReadRemaining( acceptNone: true );
		}

		return new IrcMessage( command, user, channel, message, tags );
	}

	/// <summary>
	/// Extract the nick from a "nick!user@host" prefix - everything before the '!', or the whole
	/// thing if there's no '!' (server-originated lines).
	/// </summary>
	static string ExtractNick( string prefix )
	{
		var cursor = new Parse( prefix );
		return cursor.ReadUntilOrEnd( "!", acceptNone: true );
	}

	/// <summary>
	/// Pull the channel out of the param list - the token starting with '#', minus the '#'.
	/// Returns null when there isn't one (e.g. PING, NOTICE).
	/// </summary>
	static string ExtractChannel( string parameters )
	{
		if ( string.IsNullOrEmpty( parameters ) )
			return null;

		var cursor = new Parse( parameters );
		if ( cursor.ReadUntil( "#" ) is null )
			return null;

		cursor.Pointer++; // skip '#'
		return cursor.ReadUntilWhitespaceOrNewlineOrEnd();
	}

	/// <summary>
	/// Parse the "key1=value1;key2=value2" tag block. Valueless tags map to "1" per IRCv3.
	/// Returns null if the block is empty.
	/// </summary>
	static Dictionary<string, string> ParseTags( string block )
	{
		if ( string.IsNullOrEmpty( block ) )
			return null;

		var tags = new Dictionary<string, string>();

		var cursor = new Parse( block );
		while ( !cursor.IsEnd )
		{
			var pair = cursor.ReadUntilOrEnd( ";", acceptNone: true );
			if ( cursor.Current == ';' )
				cursor.Pointer++;

			if ( string.IsNullOrEmpty( pair ) )
				continue;

			var kv = new Parse( pair );
			var key = kv.ReadUntilOrEnd( "=", acceptNone: true );
			if ( string.IsNullOrEmpty( key ) )
				continue;

			// A bare key with no '=' is a valueless tag.
			var value = "1";
			if ( kv.Current == '=' )
			{
				kv.Pointer++;
				value = kv.ReadRemaining( acceptNone: true );
			}

			tags[key] = value;
		}

		return tags;
	}

	static IrcCommand ParseCommand( string command ) => command switch
	{
		"PRIVMSG" => IrcCommand.PrivMsg,
		"PING" => IrcCommand.Ping,
		"NOTICE" => IrcCommand.Notice,
		"USERNOTICE" => IrcCommand.UserNotice,
		"RECONNECT" => IrcCommand.Reconnect,
		"JOIN" => IrcCommand.Join,
		"PART" => IrcCommand.Part,
		"004" => IrcCommand.RPL_004,
		"353" => IrcCommand.RPL_353,
		_ => IrcCommand.Unknown,
	};
}
