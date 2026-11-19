using Sandbox;
using Sandbox.UI;

namespace MenuProject;

/// <summary>
/// A party's name and capacity. Its game is the one the platform's on.
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
	string SelectedGameIdent => Draft is not null ? null : PartyView.SelectedGameIdent;
	bool CanCreate => CanEdit && Draft?.Creating != true && !GameBusy && !GameTooSmall && (!HasGame || SelectedPackage is not null) && !string.IsNullOrWhiteSpace( Name ) && Name.Length <= PartyRoom.MaxNameLength && !Name.Any( char.IsControl );

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
	string loadedGameIdent;
	bool GameBusy;

	bool HasGame => !string.IsNullOrEmpty( SelectedGameIdent );
	bool GameTooSmall => SelectedPackage is { } package && package.Info.MaxPlayers > 0 && package.Info.MaxPlayers < MemberCount;
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
		if ( loadedGameIdent == ident ) return;

		loadedGameIdent = ident;
		SelectedPackage = null;
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
			Error = package is null ? "#menu.party.could_not_load_this_game_s_details_choose_the_game_again_to_retry" : null;
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
}
