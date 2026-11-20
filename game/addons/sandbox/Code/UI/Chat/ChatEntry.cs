namespace Sandbox;

public enum ChatEntryKind
{
	Player,
	System
}

/// <summary>
/// Chat message. Comes from the platform's <see cref="ChatMessageEvent"/>
/// </summary>
public sealed class ChatEntry
{
	/// <summary>
	/// Colour of the platform's "player joined" notice
	/// </summary>
	public static readonly Color JoinedColor = new Color( 0.66f, 0.875f, 0f ); // A8DF00

	/// <summary>
	/// Colour of the platform's "player left" notice
	/// </summary>
	public static readonly Color LeftColor = new Color( 1f, 0.42f, 0.24f ); // FF6B3D

	const string JoinedSuffix = " has joined the game";
	const string LeftSuffix = " left the game";
	const string Wave = "👋";

	public ChatEntryKind Kind { get; init; }

	/// <summary>
	/// The player's name, empty for <see cref="ChatEntryKind.System"/>
	/// </summary>
	public string Name { get; init; } = "";

	public string Text { get; init; } = "";

	/// <summary>
	/// Whose avatar to show, zero for none
	/// </summary>
	public long SteamId { get; init; }

	/// <summary>
	/// A system message's own cvolor
	/// </summary>
	public Color? TextColor { get; init; }

	public RealTimeSince Age = 0;

	public static ChatEntry System( string text, Color? color = null )
	{
		return new ChatEntry { Kind = ChatEntryKind.System, Text = text ?? "", TextColor = color };
	}

	public static ChatEntry From( ChatMessageEvent e )
	{
		var message = e.Message ?? "";

		if ( e.Sender is { } sender )
			return new ChatEntry { Kind = ChatEntryKind.Player, Name = sender.DisplayName, SteamId = (long)sender.SteamId, Text = message };

		var color = SystemColor( message );

		// the platform puts a wave in front of those, just trim it
		if ( color is not null && message.StartsWith( Wave ) )
			message = message[Wave.Length..].Trim();

		return System( message, color );
	}

	static Color? SystemColor( string message )
	{
		if ( message.EndsWith( JoinedSuffix ) ) return JoinedColor;
		if ( message.EndsWith( LeftSuffix ) ) return LeftColor;

		return null;
	}
}
