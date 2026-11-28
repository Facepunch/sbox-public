using Sandbox.SteamApi;
using System.Threading;

namespace Sandbox.Services;

/// <summary>
/// The user's Workshop favourites - added to and taken off, as on the item's page. A game server has none.
/// </summary>
internal static partial class Ugc
{
	/// <summary>
	/// Add this item to the user's favourites, or take it off. False if Steam didn't take it.
	/// </summary>
	internal static async Task<bool> SetFavouriteAsync( ulong id, bool favourite, CancellationToken token = default )
	{
		if ( !SteamClient.IsValid ) return false;

		var call = SteamNative.Workshop.SetFavourite( id, favourite );
		if ( call == 0 ) return false;

		var result = await SteamCalls.GetResultAsync( call, SteamNative.Workshop.GetFavouriteUpdateResult, token, Result.IOFailure );
		if ( result != Result.OK )
			Log.Warning( $"Steam didn't {(favourite ? "favourite" : "unfavourite")} workshop item {id} (result {result})" );

		return result == Result.OK;
	}
}
