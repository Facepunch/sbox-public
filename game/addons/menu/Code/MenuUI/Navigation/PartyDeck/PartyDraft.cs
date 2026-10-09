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
		if ( PartyRoom.Current is not null ) throw new InvalidOperationException( "You are already in a party." );

		var name = Name?.Trim();
		if ( string.IsNullOrWhiteSpace( name ) || name.Length > PartyRoom.MaxNameLength || name.Any( char.IsControl ) )
			throw new InvalidOperationException( "Enter a party name." );

		Creating = true;
		PartyRoom party = null;

		try
		{
			// Configure the lobby before making a public party discoverable.
			party = await PartyRoom.Create( MaxMembers, name, false );
			if ( party is null ) throw new InvalidOperationException( "Could not create the party. Please try again." );
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
