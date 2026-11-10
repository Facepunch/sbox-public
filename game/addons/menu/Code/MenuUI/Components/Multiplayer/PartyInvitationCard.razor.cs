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
	bool JoinDisabled => IsCurrentParty || Invitation is not { Available: true, IsFull: false } || PartyService.Busy || PartyRoom.Current is not null;
	string JoinTooltip => PartyRoom.Current is not null && !IsCurrentParty ? "#menu.party.leave_your_current_party_before_joining_another" : "";

	/// <summary>
	/// What it's doing, for its look - checking, open, in a game, full, yours, or gone
	/// </summary>
	string State => Invitation switch
	{
		null => "checking",
		{ Available: false } => "gone",
		_ when IsCurrentParty => "yours",
		{ IsFull: true } => "full",
		{ IsPlaying: true } => "playing",
		_ => "open"
	};

	string StatusText => State switch
	{
		"checking" => "#menu.party.checking_party",
		"gone" => "Party ended",
		"full" => "Party full",
		"playing" => "In a game",
		_ => "Looking for players"
	};

	string JoinText => PartyService.Busy && !JoinDisabled ? "Joining…" : "#menu.party.join_party";

	/// <summary>
	/// The game it's on's art, once it's been fetched
	/// </summary>
	string Thumb => MenuHelpers.CachedPackage( Invitation?.Package )?.Thumb;

	string Art => MenuHelpers.BackgroundImage( Thumb );

	string fetchedPackage;

	public override void Tick()
	{
		base.Tick();

		// Its game's art, the first time it's known
		if ( Invitation?.Package is { Length: > 0 } package && package != fetchedPackage )
		{
			fetchedPackage = package;
			_ = MenuHelpers.FetchPackages( [package] );
		}
	}

	void Join()
	{
		if ( JoinDisabled ) return;
		_ = PartyService.Join( PartyId );
	}

	protected override int BuildHash() => HashCode.Combine( PartyId, Invitation, PartyService.Busy, PartyRoom.Current?.Id, Thumb );
}
