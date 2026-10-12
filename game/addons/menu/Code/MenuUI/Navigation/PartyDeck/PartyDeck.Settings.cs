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
	bool CanChangeSettings => Draft is not null ? !Draft.Creating : PartyView.OwnerIsMe;

	internal static void OpenDraftWithGame( Package package, CreateGameResults settings )
	{
		if ( !Instance.IsValid() ) throw new InvalidOperationException( Language.GetPhrase( "menu.party.party_settings_are_not_available_yet" ) );
		if ( PartyView.Exists ) throw new InvalidOperationException( Language.GetPhrase( "menu.settings.you_are_already_in_a_party" ) );
		if ( Instance.Draft?.Creating == true ) throw new InvalidOperationException( Language.GetPhrase( "menu.settings.your_party_is_being_created" ) );

		var draft = Instance.Draft ?? new PartyDraft();
		draft.Game = package;
		draft.GameSettings = settings;
		_ = Instance.BeginCreate( draft.IsPublic, draft );
	}

	async Task<bool> BeginCreate( bool isPublic, PartyDraft initialDraft = null )
	{
		if ( PartyRoom.Current is not null ) return true;
		Draft ??= initialDraft ?? new PartyDraft { IsPublic = isPublic };
		var draft = Draft;
		SettingsOpen = true;
		SettingsError = null;
		Window?.Restore();
		StateHasChanged();

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

		var partyId = MenuUI.Front.RailPresence.PartyId;
		if ( settingsPartyId == partyId ) return;

		settingsPartyId = partyId;
		SettingsOpen = false;
		SettingsError = null;
	}

	string PrivacyTooltip => CanChangeSettings
		? IsPublic ? "#menu.party.public_party_click_to_make_invitation_only" : "#menu.party.private_party_click_to_let_anyone_join"
		: IsPublic ? "#menu.party.public_party_anyone_can_join_only_the_leader_can_change_this" : "#menu.party.private_party_invitation_only_only_the_leader_can_change_this";

	void OpenSettings()
	{
		if ( !CanChangeSettings ) return;
		SettingsOpen = true;
		SettingsError = null;
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
