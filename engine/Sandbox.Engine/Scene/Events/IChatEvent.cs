namespace Sandbox;

/// <summary>
/// A chat message event. Handlers can set <see cref="Suppress"/> to true
/// to prevent the message from being delivered, or set <see cref="RecipientFilter"/>
/// to control per-connection visibility.
/// </summary>
public class ChatMessageEvent
{
	/// <summary>
	/// The chat message text.
	/// </summary>
	public string Message { get; set; }

	/// <summary>
	/// The connection that sent this message. Null for system messages.
	/// </summary>
	public Connection Sender { get; init; }

	/// <summary>
	/// Set to true to prevent this message from being delivered.
	/// </summary>
	public bool Suppress { get; set; } = false;

	/// <summary>
	/// Optional per-connection visibility predicate. Return false to
	/// prevent a specific connection from receiving the message.
	/// </summary>
	public Func<Connection, bool> RecipientFilter { get; set; }
}

public interface IChatEvent : ISceneEvent<IChatEvent>
{
	/// <summary>
	/// Called when a chat message is received. Set <paramref name="e"/>.Suppress to true
	/// to prevent the message from being shown or relayed, or set <paramref name="e"/>.RecipientFilter
	/// to control per-connection visibility.
	/// </summary>
	void OnChatMessage( ChatMessageEvent e ) { }
}
