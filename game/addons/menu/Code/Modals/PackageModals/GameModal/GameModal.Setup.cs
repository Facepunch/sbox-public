using Sandbox;
using Sandbox.Modals;
using MenuProject.Modals.GameModalComponents;

namespace MenuProject.Modals;

/// <summary>
/// Applies a local game setup to a new party, an existing party, or a game launch.
/// </summary>
public partial class GameModal
{
	bool partyActions;
	PartyDraft partyDraft;
	string editingPartyId;
	bool openedInParty;
	string originalGameIdent;
	CreateGameResults? originalSettings;

	internal bool SetupBusy { get; private set; }
	internal string SetupError { get; private set; }
	internal bool PartySetup => partyActions && (partyDraft is not null || openedInParty);
	internal bool EditingPartyDraft => partyDraft is not null;
	internal bool SetupReadOnly => SetupBusy || ContextProblem is not null || (PartySetup && partyDraft is null && !PartyView.OwnerIsMe);
	internal bool CanCreateParty => partyActions && !PartySetup && Setup?.IsMultiplayer == true;
	internal bool CanApplySetup => Setup is not null && !SetupReadOnly && CapacityProblem is null;
	internal bool CanStartSetup => CanApplySetup && !PartySetup && (!Setup.Package.Info.IsVrOnly || Application.IsVR);
	internal string StartSetupText => SetupBusy ? "#menu.games.starting" : "#menu.games.start_game";
	internal string SaveSetupText => partyDraft is not null ? "#menu.common.continue" : "#menu.games.save_to_party";

	string ContextProblem
	{
		get
		{
			if ( !partyActions ) return MenuHelpers.HasAuthority ? null : "#menu.games.only_the_party_leader_can_change_the_game_setup";
			if ( partyDraft is { Cancelled: true } or { Creating: true } ) return "#menu.games.this_party_draft_is_no_longer_being_edited_return_to_the_party";
			if ( openedInParty != PartyView.Exists || editingPartyId != MenuUI.Front.RailPresence.PartyId ) return "#menu.games.your_party_has_changed_reopen_the_game_setup_to_continue";
			if ( openedInParty && PartyView.LeaderState != PartyRoom.OwnerJoinState.None ) return "#menu.games.the_party_is_joining_a_game_return_to_the_party_to_follow_its_progress";
			if ( !PartySetup ) return null;

			var ident = partyDraft is not null ? partyDraft.Game?.FullIdent : PartyView.SelectedGameIdent;
			var settings = partyDraft is not null ? partyDraft.GameSettings : PartyView.SelectedGameSettings;
			if ( ident != originalGameIdent || !GameSetup.SettingsEqual( settings, originalSettings ) )
				return "#menu.games.the_party_s_game_setup_has_changed_reopen_it_to_see_the_latest_settings";

			return null;
		}
	}

	string CapacityProblem => PartySetup && Setup is { } setup && setup.MaxPlayers < (partyDraft is not null ? 1 : PartyView.MemberCount)
		? "#menu.games.add_enough_server_slots_for_everyone_in_the_party" : null;

	internal string SetupNotice
	{
		get
		{
			if ( ContextProblem is { } problem ) return problem;
			if ( PartySetup && partyDraft is null && !PartyView.OwnerIsMe ) return "#menu.games.only_the_party_leader_can_change_these_settings";
			if ( CapacityProblem is { } capacity ) return capacity;
			if ( !PartySetup ) return null;
			var selected = partyDraft is not null ? partyDraft.Game?.FullIdent == Setup?.Package.FullIdent : PartyView.IsGameSelected( Setup?.Package );
			if ( !selected && !string.IsNullOrEmpty( originalGameIdent ) ) return "#menu.games.saving_will_replace_the_party_s_selected_game_with_this_one";
			return partyDraft is not null ? "#menu.games.choose_your_game_settings_then_continue_to_create_the_party" : "#menu.games.changes_stay_here_until_you_save_changing_the_setup";
		}
	}

	void InitializePartySetup( bool enabled, PartyDraft draft )
	{
		partyActions = enabled;
		partyDraft = draft;
		openedInParty = PartyView.Exists;
		editingPartyId = MenuUI.Front.RailPresence.PartyId;
		originalGameIdent = draft is not null ? draft.Game?.FullIdent : PartyView.SelectedGameIdent;
		originalSettings = draft is not null ? draft.GameSettings : PartyView.SelectedGameSettings;
		SetupError = null;
		SetupBusy = false;
	}

	void ApplyPartySetup( CreateGameResults settings )
	{
		if ( partyDraft is not null )
		{
			partyDraft.Game = Setup.Package;
			partyDraft.GameSettings = settings;
		}
		else
		{
			PartyView.SelectGame( Setup.Package );
			PartyView.ConfigureGame( PartyView.SelectedGameIdent, settings );
		}
	}

	internal void SavePartySetup()
	{
		if ( !PartySetup || !CanApplySetup ) return;
		try
		{
			ApplyPartySetup( Setup.Finish() );
			ReturnToParty();
		}
		catch ( Exception e )
		{
			SetupError = e.Message;
		}
	}

	internal void CreatePartyFromSetup()
	{
		if ( !CanCreateParty || !CanApplySetup ) return;
		try
		{
			PartyDeck.OpenDraftWithGame( Setup.Package, Setup.Finish() );
			CloseModal( true );
		}
		catch ( Exception e )
		{
			SetupError = e.Message;
		}
	}

	internal void CancelSetup()
	{
		if ( PartySetup )
		{
			ReturnToParty();
		}
		else
		{
			CloseSetup();
		}
	}

	void ReturnToParty()
	{
		CloseSetup();
		CloseModal( false );
		PartyDeck.Instance?.Window?.Restore();
	}
}
