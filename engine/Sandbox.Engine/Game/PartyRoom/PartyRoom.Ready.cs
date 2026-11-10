namespace Sandbox;

/// <summary>
/// Shared ready votes for the party's next game.
/// </summary>
public partial class PartyRoom
{
	string localReadyRevision;

	/// <summary>
	/// Whether this member is ready for the current game setup. The leader starts the game instead.
	/// </summary>
	public bool IsMemberReady( Friend member )
	{
		if ( member.Id == Owner.Id ) return false;

		var revision = steamLobby.GetData( "ready_revision" );
		if ( string.IsNullOrEmpty( revision ) ) return false;

		var ready = member.IsMe ? localReadyRevision : steamLobby.GetMemberData( member.Internal, "ready_revision" );
		return ready == revision;
	}

	/// <summary>
	/// Share whether the local member is ready for the selected game and its saved settings.
	/// Changing the setup clears everyone's readiness.
	/// </summary>
	public void SetReady( bool ready )
	{
		if ( Current != this || Owner.IsMe ) return;
		if ( ready && (JoinState != OwnerJoinState.None || string.IsNullOrEmpty( SelectedGameIdent ) || SelectedGameSettings is null) ) return;

		localReadyRevision = ready ? steamLobby.GetData( "ready_revision" ) : "";
		steamLobby.SetMemberData( "ready_revision", localReadyRevision );
	}

	void ResetReadiness()
	{
		// A new token prevents old ready votes from applying if the owner changes settings back.
		steamLobby.SetData( "ready_revision", Guid.NewGuid().ToString( "N" ) );
	}
}
