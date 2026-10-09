using Sandbox;
using Sandbox.Modals;
using Sandbox.UI;
using MenuProject.Modals;
using MenuProject.Modals.GameModalComponents;

namespace MenuProject;

/// <summary>
/// Party capacity, game selection, and a summary linking to the shared game setup editor.
/// </summary>
public partial class PartySettings
{
	/// <summary>
	/// Local choices for a new party. Null edits the existing party.
	/// </summary>
	[Parameter] public PartyDraft Draft { get; set; }

	object editingParty;
	bool CanEdit => Draft is not null ? !Draft.Cancelled && !Draft.Creating : PartyView.OwnerIsMe;
	int MemberCount => Draft is not null ? 1 : PartyView.MemberCount;
	int MaxMembers => Draft?.MaxMembers ?? PartyView.MaxMembers;
	string SelectedGameIdent => Draft is not null ? Draft.Game?.FullIdent : PartyView.SelectedGameIdent;
	string SelectedGameTitle => Draft is not null ? Draft.Game?.Title : PartyView.SelectedGameTitle;
	CreateGameResults? SelectedGameSettings => Draft is not null ? Draft.GameSettings : PartyView.SelectedGameSettings;
	bool IsGameSelected => Draft is not null ? SelectedPackage is not null && SelectedPackage.FullIdent == Draft.Game?.FullIdent : PartyView.IsGameSelected( SelectedPackage );
	bool CanCreate => CanEdit && Draft?.Creating != true && !GameBusy && !GameTooSmall && (!HasGame || SelectedPackage is not null) && !string.IsNullOrWhiteSpace( Name ) && Name.Length <= PartyRoom.MaxNameLength && !Name.Any( char.IsControl );

	void SelectGame( Package package )
	{
		if ( Draft is not null )
		{
			if ( Draft.Game?.FullIdent != package?.FullIdent ) Draft.GameSettings = null;
			Draft.Game = package;
		}
		else
		{
			PartyView.SelectGame( package );
		}
	}

	async Task CreateParty()
	{
		if ( Draft is null || !CanCreate ) return;
		try
		{
			Error = null;
			await Draft.Create();
		}
		catch ( Exception e )
		{
			if ( IsValid ) Error = e.Message;
		}
	}

	Package SelectedPackage;
	Texture GameThumbnail;
	string loadedGameIdent;
	bool GameBusy;
	CreateGameResults? gameSettings;
	GameSetup GameSummary;

	bool HasGame => !string.IsNullOrEmpty( SelectedGameIdent );
	bool GameTooSmall => SelectedPackage is { } package && package.Info.MaxPlayers > 0 && package.Info.MaxPlayers < MemberCount;
	bool CanShowGameSettings => IsGameSelected && !GameBusy && !SelectedPackage.Info.IsDedicatedServerOnly;
	string PartyId => MenuUI.Front.RailPresence.PartyId;

	void SetMaxMembers( float value )
	{
		try
		{
			if ( Draft is not null )
			{
				Draft.MaxMembers = Math.Clamp( (int)value, 1, PartyDeck.MAX_MEMBERS );
			}
			else
			{
				PartyView.SetMaxMembers( (int)value );
			}
			Error = null;
		}
		catch ( Exception e )
		{
			Error = e.Message;
		}
	}

	void ChooseGame()
	{
		if ( !CanEdit ) return;
		var partyId = PartyId;
		var draft = Draft;

		Game.Overlay.ShowPackageSelector( "type:game sort:trending +multiplayer", package =>
		{
			if ( !IsValid || PartyId != partyId || Draft != draft || !CanEdit ) return;

			try
			{
				if ( package.Info.IsDedicatedServerOnly )
				{
					SelectGame( package );
					loadedGameIdent = null;
				}
				else
				{
					ModalSystem.Instance.OpenGameSetup( package, draft: Draft );
				}
				Error = null;
			}
			catch ( Exception e )
			{
				Error = e.Message;
			}
		} );
	}

	void ClearGame()
	{
		try
		{
			SelectGame( null );
			Error = null;
		}
		catch ( Exception e )
		{
			Error = e.Message;
		}
	}

	void ConfigureGame()
	{
		if ( !CanShowGameSettings || Draft?.Creating == true ) return;
		ModalSystem.Instance.OpenGameSetup( SelectedPackage, draft: Draft );
	}

	/// <summary>
	/// Follow the party's game choice, including changes made by another leader.
	/// </summary>
	public override void Tick()
	{
		base.Tick();
		object party = Draft ?? (object)PartyId;
		if ( !Equals( editingParty, party ) )
		{
			editingParty = party;
			Name = Draft?.Name ?? PartyView.Name;
			Error = null;
			loadedGameIdent = null;
		}

		var ident = SelectedGameIdent;
		if ( loadedGameIdent == ident )
		{
			if ( !GameBusy && SelectedPackage is not null )
			{
				var shared = SelectedGameSettings;
				if ( !GameSetup.SettingsEqual( gameSettings, shared ) )
				{
					RefreshGameSummary();
				}
			}
			return;
		}

		loadedGameIdent = ident;
		SelectedPackage = null;
		gameSettings = null;
		GameSummary = null;
		GameThumbnail = null;
		GameBusy = !string.IsNullOrEmpty( ident );
		if ( GameBusy ) _ = LoadGame( ident );
	}

	async Task LoadGame( string ident )
	{
		try
		{
			var package = await Package.FetchAsync( ident, false );
			if ( !IsValid || loadedGameIdent != ident ) return;

			SelectedPackage = package;
			GameThumbnail = string.IsNullOrEmpty( package?.Thumb ) ? null : Texture.Load( package.Thumb );
			Error = package is null ? "Could not load this game's details. Choose the game again to retry." : null;

			RefreshGameSummary();
		}
		catch ( Exception e )
		{
			if ( IsValid && loadedGameIdent == ident ) Error = $"Could not load game details: {e.Message}";
		}
		finally
		{
			if ( IsValid && loadedGameIdent == ident )
			{
				GameBusy = false;
				StateHasChanged();
			}
		}
	}

	void RefreshGameSummary()
	{
		gameSettings = SelectedGameSettings;
		GameSummary = SelectedPackage is not null && gameSettings is { } settings
			? new GameSetup( SelectedPackage, initialSettings: settings ) : null;
	}
}
