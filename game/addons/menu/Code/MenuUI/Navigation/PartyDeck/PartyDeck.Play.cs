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
	int ReadyCount => Members.Count( x => x.Id != PartyView.Owner.Id && PartyView.IsMemberReady( x ) );
	int FollowersCount => Members.Count( x => x.Id != PartyView.Owner.Id );

	string SetupProblem
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( PartyView.SelectedGameIdent ) ) return "Choose a game in party settings";
			if ( !PartyView.IsGameSelected( DisplayGamePackage ) ) return "Loading game details";
			if ( DisplayGamePackage.Info.IsDedicatedServerOnly ) return "Choose a server from the game's server list";
			if ( DisplayGamePackage.Info.IsVrOnly && !Application.IsVR ) return "This game requires VR";
			if ( PartyView.SelectedGameSettings is not { } settings ) return "Configure the game in party settings";
			if ( settings.MaxPlayers < MemberCount ) return "Add more server slots in party settings";
			if ( DisplayGamePackage.Info.MaxPlayers > 0 && DisplayGamePackage.Info.MaxPlayers < MemberCount ) return "This game cannot fit everyone in the party";
			return null;
		}
	}

	bool CanStartGame => ShowPlayControls && PartyView.OwnerIsMe && !StartingGame && !LoadingScreen.IsVisible && SetupProblem is null;

	string PlayHint => StartingGame ? "Starting game…" : SetupProblem ?? (FollowersCount == 0 ? "Your game is ready to start" : $"{ReadyCount} / {FollowersCount} ready");

	void ToggleReady()
	{
		if ( !ShowPlayControls || PartyView.OwnerIsMe || (!IsReady && SetupProblem is not null) ) return;
		PartyView.SetReady( !IsReady );
	}

	void ConfigureGame()
	{
		if ( !PartyView.IsGameSelected( DisplayGamePackage ) || DisplayGamePackage.Info.IsDedicatedServerOnly )
		{
			OpenSettings();
			return;
		}

		ModalSystem.Instance.OpenGameSetup( DisplayGamePackage );
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
