using Sandbox;

namespace MenuProject.Multiplayer;

/// <summary>
/// Discovers, creates and joins parties from the menu and shared invitations.
/// </summary>
internal static class PartyService
{
	internal static bool Busy { get; private set; }
	internal static string Error { get; private set; }

	/// <summary>
	/// Open parties still gathering players, shared by Multiplayer and the home sidebar.
	/// </summary>
	internal static async Task<PartyRoom.Entry[]> FindOpenParties()
	{
		var parties = await PartyRoom.Find();
		return parties.Where( x => !x.IsPlaying && !x.IsFull && x.OwnerId != 0 && !new Friend( x.OwnerId ).IsBlocked )
			.OrderByDescending( x => x.Members ).ToArray();
	}

	internal static Task Create() => Run( async () =>
	{
		if ( PartyRoom.Current is not null ) return;

		await PartyDeck.EnsurePartyExists( true );
	} );

	internal static Task Join( PartyRoom.Entry party ) => Join( party.Id );

	internal static Task Join( ulong id ) => Run( async () =>
	{
		if ( PartyRoom.Current is { } current )
		{
			if ( current.Id.ValueUnsigned == id ) return;

			throw new InvalidOperationException( "Leave your current party before joining another." );
		}

		await PartyRoom.Join( id );
	} );

	static async Task Run( Func<Task> action )
	{
		if ( Busy ) return;

		Busy = true;
		Error = null;

		try
		{
			await action();
		}
		catch ( Exception e )
		{
			Error = e.Message;
		}
		finally
		{
			Busy = false;
		}
	}
}
