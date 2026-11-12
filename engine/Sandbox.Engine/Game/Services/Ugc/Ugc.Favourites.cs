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
		using var request = NativeEngine.CUgcFavourite.SetFavourite( id, favourite );
		if ( request.IsNull ) return false;

		while ( !request.m_complete )
		{
			await Task.Delay( 50, token );
		}

		if ( !request.m_success )
			Log.Warning( $"Steam didn't {(favourite ? "favourite" : "unfavourite")} workshop item {id} (result {request.m_resultCode})" );

		return request.m_success;
	}
}
