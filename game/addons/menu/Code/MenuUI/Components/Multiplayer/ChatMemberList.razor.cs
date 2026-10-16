using Sandbox;
using Chat = MenuProject.Multiplayer.GlobalChatService;

namespace MenuProject.MenuUI.Components.Multiplayer;

/// <summary>
/// The players in global chat, sorted by name, with access to their profile cards.
/// </summary>
public partial class ChatMemberList : Panel
{
	IEnumerable<object> Members => (Chat.Channel?.Members ?? Enumerable.Empty<Friend>())
		.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase ).Cast<object>();

	protected override int BuildHash() => HashCode.Combine( Chat.Revision, Chat.Channel?.MemberCount );
}
