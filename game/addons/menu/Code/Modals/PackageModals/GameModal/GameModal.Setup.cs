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
	internal string StartSetupText => SetupBusy ? "Starting…" : "Start game";
	internal string SaveSetupText => partyDraft is not null ? "Continue" : "Save to party";

	string ContextProblem
	{
		get
		{
			if ( !partyActions ) return MenuHelpers.HasAuthority ? null : "Only the party leader can change the game setup.";
			if ( partyDraft is { Cancelled: true } or { Creating: true } ) return "This party draft is no longer being edited. Return to the party.";
			if ( openedInParty != PartyView.Exists || editingPartyId != MenuUI.Front.RailPresence.PartyId ) return "Your party has changed. Reopen the game setup to continue.";
			if ( openedInParty && PartyView.LeaderState != PartyRoom.OwnerJoinState.None ) return "The party is joining a game. Return to the party to follow its progress.";
			if ( !PartySetup ) return null;

			var ident = partyDraft is not null ? partyDraft.Game?.FullIdent : PartyView.SelectedGameIdent;
			var settings = partyDraft is not null ? partyDraft.GameSettings : PartyView.SelectedGameSettings;
			if ( ident != originalGameIdent || !GameSetup.SettingsEqual( settings, originalSettings ) )
				return "The party's game setup has changed. Reopen it to see the latest settings.";

			return null;
		}
	}

	string CapacityProblem => PartySetup && Setup is { } setup && setup.MaxPlayers < (partyDraft is not null ? 1 : PartyView.MemberCount)
		? "Add enough server slots for everyone in the party." : null;

	internal string SetupNotice
	{
		get
		{
			if ( ContextProblem is { } problem ) return problem;
			if ( PartySetup && partyDraft is null && !PartyView.OwnerIsMe ) return "Only the party leader can change these settings.";
			if ( CapacityProblem is { } capacity ) return capacity;
			if ( !PartySetup ) return null;
			var selected = partyDraft is not null ? partyDraft.Game?.FullIdent == Setup?.Package.FullIdent : PartyView.IsGameSelected( Setup?.Package );
			if ( !selected && !string.IsNullOrEmpty( originalGameIdent ) ) return "Saving will replace the party's selected game with this one.";
			return partyDraft is not null ? "Choose your game settings, then continue to create the party." : "Changes stay here until you save. Changing the setup resets everyone's ready status.";
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
