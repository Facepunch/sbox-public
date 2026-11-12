using Sandbox;
using Sandbox.Menu;

namespace MenuProject;

/// <summary>
/// Ready indicators and launching the party's saved game setup.
/// </summary>
public partial class PartyDeck
{
	bool StartingGame;
	bool ShowPlayControls => Draft is null && PartyView.Exists && PartyView.LeaderState == PartyRoom.OwnerJoinState.None;
	bool IsReady => PartyView.IsMemberReady( Me );

	string SetupProblem
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( PartyView.SelectedGameIdent ) ) return "#menu.party.choose_a_game_in_party_settings";
			if ( !PartyView.IsGameSelected( DisplayGamePackage ) ) return "#menu.party.loading_game_details";
			if ( DisplayGamePackage.Info.IsDedicatedServerOnly ) return "#menu.party.choose_a_server_from_the_game_s_server_list";
			if ( DisplayGamePackage.Info.IsVrOnly && !Application.IsVR ) return "#menu.party.this_game_requires_vr";
			if ( PartyView.SelectedGameSettings is not { } settings ) return "#menu.party.configure_the_game_in_party_settings";
			if ( settings.MaxPlayers < MemberCount ) return "#menu.party.add_more_server_slots_in_party_settings";
			if ( DisplayGamePackage.Info.MaxPlayers > 0 && DisplayGamePackage.Info.MaxPlayers < MemberCount ) return "#menu.party.this_game_cannot_fit_everyone_in_the_party";
			return null;
		}
	}

	bool CanStartGame => ShowPlayControls && PartyView.OwnerIsMe && !StartingGame && !LoadingScreen.IsVisible && SetupProblem is null;


	void ToggleReady()
	{
		if ( !ShowPlayControls || PartyView.OwnerIsMe || (!IsReady && SetupProblem is not null) ) return;
		PartyView.SetReady( !IsReady );
	}

	async Task StartGame()
	{
		if ( !CanStartGame ) return;

		// Preview controls never launch a real game.
		if ( PartyView.IsMocking ) return;

		StartingGame = true;
		SettingsError = null;

		try
		{
			await MenuHelpers.StartConfiguredGame( DisplayGamePackage, PartyView.SelectedGameSettings.Value, PartyRoom.Current );
		}
		catch ( Exception e )
		{
			LoadingScreen.IsVisible = false;
			if ( IsValid ) SettingsError = e.Message;
		}
		finally
		{
			StartingGame = false;
			if ( IsValid ) StateHasChanged();
		}
	}
}
