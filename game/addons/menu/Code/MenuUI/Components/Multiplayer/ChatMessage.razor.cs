using MenuProject.MenuUI.Popups;
using Sandbox;

namespace MenuProject.MenuUI.Components.Multiplayer;

/// <summary>
/// A chat message showing its sender and either text or a party invitation.
/// </summary>
public partial class ChatMessage : Panel
{
	/// <summary>
	/// The player who sent this message.
	/// </summary>
	[Parameter] public Friend Sender { get; set; }

	/// <summary>
	/// Message text shown when there is no party invitation.
	/// </summary>
	[Parameter] public string Text { get; set; }

	/// <summary>
	/// The invited party, or zero for a text message.
	/// </summary>
	[Parameter] public ulong PartyId { get; set; }

	void ShowSender( PanelEvent e ) => FriendCard.ShowFromClick( e, Sender );

	protected override int BuildHash() => HashCode.Combine( Sender.Id, Sender.Name, Text, PartyId );
}
