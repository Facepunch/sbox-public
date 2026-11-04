using Sandbox;
using Chat = MenuProject.Multiplayer.GlobalChatService;

namespace MenuProject.MenuUI.Components.Multiplayer;

/// <summary>
/// Writes global chat messages and lets the party leader share an invitation.
/// </summary>
public partial class ChatComposer : Panel
{
	/// <summary>
	/// Called after sending a text message so the conversation can scroll to the latest message.
	/// </summary>
	[Parameter] public Action OnMessageSent { get; set; }

	TextEntry input;

	void Submit()
	{
		if ( Chat.Send( input.Text ) )
		{
			Chat.Draft = "";
			input.Text = "";
			OnMessageSent?.Invoke();
		}

		input.Focus();
	}

	protected override int BuildHash() => HashCode.Combine( Chat.CanSend, Chat.CanInviteParty, PartyRoom.Current?.Id, PartyRoom.Current?.MemberCount, PartyRoom.Current?.Owner.Id );
}
