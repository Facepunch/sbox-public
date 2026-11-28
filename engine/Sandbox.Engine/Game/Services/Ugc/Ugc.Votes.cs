using Sandbox.SteamApi;
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
		var call = SteamNative.Workshop.GetVote( id );
		var vote = await SteamCalls.GetResultAsync( call, SteamNative.Workshop.GetVoteResult, token );
		return vote switch
		{
			WorkshopVote.Up => true,
			WorkshopVote.Down => false,
			_ => null
		};
	}

	/// <summary>
	/// Vote this item up or down. False if Steam didn't take it.
	/// </summary>
	internal static async Task<bool> SetVoteAsync( ulong id, bool up, CancellationToken token = default )
	{
		if ( !SteamClient.IsValid ) return false;

		var call = SteamNative.Workshop.SetVote( id, up );
		if ( call == 0 ) return false;

		var result = await SteamCalls.GetResultAsync( call, SteamNative.Workshop.GetVoteUpdateResult, token, Result.IOFailure );
		if ( result != Result.OK )
			Log.Warning( $"Steam didn't take the vote on workshop item {id} (result {result})" );

		return result == Result.OK;
	}
}
