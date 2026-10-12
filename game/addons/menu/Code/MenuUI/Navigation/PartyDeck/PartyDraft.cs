using Sandbox;
using Sandbox.Modals;

namespace MenuProject;

/// <summary>
/// Local party choices, kept off Steam until the player presses Create.
/// </summary>
public sealed class PartyDraft
{
	internal string Name = PartyView.NameForNewParty;
	internal int MaxMembers = PartyDeck.MAX_MEMBERS;
	internal bool IsPublic;
	internal Package Game;
	internal CreateGameResults? GameSettings;
	internal bool Creating;
	internal bool Cancelled;
	internal TaskCompletionSource<bool> Completion = new();

	internal void Cancel()
	{
		Cancelled = true;
		Completion.TrySetResult( false );
	}

	internal async Task Create()
	{
		if ( Creating || Cancelled ) return;
		if ( PartyRoom.Current is not null ) throw new InvalidOperationException( Language.GetPhrase( "menu.settings.you_are_already_in_a_party" ) );

		var name = Name?.Trim();
		if ( string.IsNullOrWhiteSpace( name ) || name.Length > PartyRoom.MaxNameLength || name.Any( char.IsControl ) )
			throw new InvalidOperationException( Language.GetPhrase( "menu.party.enter_a_party_name" ) );

		Creating = true;
		PartyRoom party = null;

		try
		{
			// Configure the lobby before making a public party discoverable.
			party = await PartyRoom.Create( MaxMembers, name, false );
			if ( party is null ) throw new InvalidOperationException( Language.GetPhrase( "menu.party.could_not_create_the_party_please_try_again" ) );
			if ( Cancelled )
			{
				party.Leave();
				return;
			}

			if ( Game is not null )
			{
				party.SelectGame( Game );
				if ( GameSettings is { } settings ) party.ConfigureGame( party.SelectedGameIdent, settings );
			}

			if ( IsPublic ) party.SetPublic( true );
			ConsoleSystem.SetValue( "party_name", name );
			Completion.TrySetResult( true );
		}
		catch
		{
			party?.Leave();
			throw;
		}
		finally
		{
			Creating = false;
		}
	}
}
