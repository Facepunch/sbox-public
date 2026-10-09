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
		if ( !Instance.IsValid() ) throw new InvalidOperationException( "Party settings are not available yet." );
		if ( PartyView.Exists ) throw new InvalidOperationException( "You are already in a party." );
		if ( Instance.Draft?.Creating == true ) throw new InvalidOperationException( "Your party is being created." );

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
		? IsPublic ? "Public party · Click to make invitation-only" : "Private party · Click to let anyone join"
		: IsPublic ? "Public party · Anyone can join. Only the leader can change this." : "Private party · Invitation only. Only the leader can change this.";

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
