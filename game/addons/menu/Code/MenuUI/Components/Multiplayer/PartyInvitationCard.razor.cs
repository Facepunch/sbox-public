using MenuProject.Multiplayer;
using Sandbox;

namespace MenuProject.MenuUI.Components.Multiplayer;

/// <summary>
/// A shared party invitation with its current status and join action.
/// </summary>
public partial class PartyInvitationCard : Panel
{
	/// <summary>
	/// The party this invitation lets the player join.
	/// </summary>
	[Parameter] public ulong PartyId { get; set; }

	GlobalChatService.PartyInvitation Invitation => GlobalChatService.GetInvitation( PartyId );
	bool IsCurrentParty => PartyRoom.Current?.Id.ValueUnsigned == PartyId;
	bool JoinDisabled => Invitation is not { Available: true, IsFull: false } || PartyService.Busy || PartyRoom.Current is not null;
	string JoinTooltip => PartyRoom.Current is not null && !IsCurrentParty ? "Leave your current party before joining another" : "";

	Task Join() => PartyService.Join( PartyId );

	protected override int BuildHash() => HashCode.Combine( PartyId, Invitation, PartyService.Busy, PartyRoom.Current?.Id );
}
