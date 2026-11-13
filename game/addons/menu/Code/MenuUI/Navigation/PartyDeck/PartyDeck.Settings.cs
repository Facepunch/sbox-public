using Sandbox;
using Sandbox.Modals;

namespace MenuProject;

/// <summary>
/// Party access controls and the settings view within the party window.
/// </summary>
public partial class PartyDeck
{
	bool SettingsOpen;
	string SettingsError;
	string settingsPartyId;
	PartyDraft Draft;

	bool IsPublic => Draft?.IsPublic ?? PartyView.IsPublic;

	/// <summary>
	/// A new party's being set up in the window, not made yet
	/// </summary>
	internal static bool Drafting => Instance?.Draft is not null;

	/// <summary>
	/// Clicked off on the platform menu - a party you're in put away, a new one's setup given up on (not once it's being
	/// made)
	/// </summary>
	internal static void PutAwayOnPlatform()
	{
		ShownOnPlatform = false;

		if ( Instance?.Draft is { Creating: false } draft )
			draft.Cancel();
	}
	bool CanChangeSettings => Draft is not null ? !Draft.Creating : PartyView.OwnerIsMe;

	async Task<bool> BeginCreate( bool isPublic, PartyDraft initialDraft = null, bool createImmediately = false )
	{
		if ( PartyRoom.Current is not null ) return true;
		Draft ??= initialDraft ?? new PartyDraft { IsPublic = isPublic };
		var draft = Draft;
		SettingsOpen = true;
		SettingsError = null;
		Window?.Restore();
		StateHasChanged();

		if ( createImmediately )
		{
			try
			{
				await draft.Create();
			}
			catch ( Exception e )
			{
				SettingsError = e.Message;
				if ( IsValid ) StateHasChanged();
				return false;
			}
		}

		var created = await draft.Completion.Task;
		if ( Draft == draft )
		{
			Draft = null;
			SettingsOpen = false;
			settingsPartyId = MenuUI.Front.RailPresence.PartyId;
			if ( IsValid ) StateHasChanged();
		}

		return created;
	}

	void UpdateSettings()
	{
		if ( Draft is { } draft )
		{
			if ( PartyRoom.Current is not null && !draft.Creating ) draft.Cancel();
			return;
		}

		KeepPlatformGame();

		var partyId = MenuUI.Front.RailPresence.PartyId;
		if ( settingsPartyId == partyId ) return;

		settingsPartyId = partyId;
		SettingsOpen = false;
		SettingsError = null;
	}

	string PrivacyTooltip => CanChangeSettings
		? IsPublic ? "#menu.party.public_party_click_to_make_invitation_only" : "#menu.party.private_party_click_to_let_anyone_join"
		: IsPublic ? "#menu.party.public_party_anyone_can_join_only_the_leader_can_change_this" : "#menu.party.private_party_invitation_only_only_the_leader_can_change_this";

	/// <summary>
	/// On the platform menu the party plays the game you're on - the game switcher picks it, so leading a party, its
	/// game follows yours. Not while you're in a game, nor a project that isn't on the Workshop.
	/// </summary>
	static void KeepPlatformGame()
	{
		if ( !PartyView.Exists || !PartyView.OwnerIsMe ) return;
		if ( PartyView.LeaderState != PartyRoom.OwnerJoinState.None ) return;
		if ( Platform.PlatformMenu.Current is not { IsRemote: true } current || PartyView.IsGameSelected( current ) ) return;

		PartyView.SelectGame( current );
	}

	void ToggleSettings()
	{
		if ( Draft is not null ) return;
		SettingsOpen = !SettingsOpen;
		SettingsError = null;
	}

	void TogglePrivacy()
	{
		if ( !CanChangeSettings ) return;

		try
		{
			if ( Draft is not null )
			{
				Draft.IsPublic = !Draft.IsPublic;
			}
			else
			{
				PartyView.SetPublic( !PartyView.IsPublic );
			}
			SettingsError = null;
		}
		catch ( Exception e )
		{
			SettingsError = e.Message;
		}
	}
}
