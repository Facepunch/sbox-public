namespace Sandbox.Twitch;

/// <summary>
/// A parsed Twitch IRC line, produced by <see cref="IrcParser.Parse"/>. Holds only the pieces we
/// consume - everything else on the line is dropped while parsing.
/// </summary>
internal readonly struct IrcMessage
{
	/// <summary>
	/// The command this line carries, or <see cref="IrcCommand.Unknown"/> if it's one we don't handle.
	/// </summary>
	public IrcCommand Command { get; }

	/// <summary>
	/// The nick the line came from, or null for server-originated lines (e.g. PING, NOTICE).
	/// </summary>
	public string User { get; }

	/// <summary>
	/// The target channel without its leading '#', or null if the line isn't channel-scoped.
	/// </summary>
	public string Channel { get; }

	/// <summary>
	/// The trailing text - a chat message body, the NAMES list, a NOTICE's text - or null if absent.
	/// </summary>
	public string Message { get; }

	/// <summary>
	/// IRCv3 tags keyed by name, or null if the line carried none. Read values via <see cref="GetTag"/>.
	/// </summary>
	readonly Dictionary<string, string> _tags;

	public IrcMessage( IrcCommand command, string user, string channel, string message, Dictionary<string, string> tags )
	{
		Command = command;
		User = user;
		Channel = channel;
		Message = message;
		_tags = tags;
	}

	/// <summary>
	/// Get an IRCv3 tag value by key (e.g. "display-name", "color", "badges"), or null if the line
	/// had no tags or didn't carry that one.
	/// </summary>
	public string GetTag( string key )
		=> _tags is not null && _tags.TryGetValue( key, out var value ) ? value : null;
}
