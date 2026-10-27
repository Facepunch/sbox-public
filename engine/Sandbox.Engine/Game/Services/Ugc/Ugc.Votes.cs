using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// The user's votes on workshop items - thumbs up or down, as on the item's page. Steam doesn't let a vote be taken back,
/// only changed. A game server has no votes.
/// </summary>
internal static partial class Ugc
{
	/// <summary>
	/// How the user's voted on this item - true for up, false for down, null if they haven't. Null too if Steam couldn't be asked.
	/// </summary>
	internal static async Task<bool?> GetVoteAsync( ulong id, CancellationToken token = default )
	{
		using var vote = NativeEngine.CUgcVote.GetVote( id );
		if ( vote.IsNull ) return null;

		while ( !vote.m_complete )
		{
			await Task.Delay( 50, token );
		}

		if ( !vote.m_success ) return null;
		return vote.m_votedUp ? true : vote.m_votedDown ? false : null;
	}

	/// <summary>
	/// Vote this item up or down. False if Steam didn't take it.
	/// </summary>
	internal static async Task<bool> SetVoteAsync( ulong id, bool up, CancellationToken token = default )
	{
		using var vote = NativeEngine.CUgcVote.SetVote( id, up );
		if ( vote.IsNull ) return false;

		while ( !vote.m_complete )
		{
			await Task.Delay( 50, token );
		}

		if ( !vote.m_success )
			Log.Warning( $"Steam didn't take the vote on workshop item {id} (result {vote.m_resultCode})" );

		return vote.m_success;
	}
}
