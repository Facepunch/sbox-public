using Sandbox.SteamApi;

namespace Sandbox.Services;

/// <summary>
/// Implements SteamUgc, lets us edit/create workshop items
/// </summary>
internal static partial class Ugc
{
	internal static Task<UgcPublisher> CreateCommunityItem()
	{
		return FinishCreatingItem( SteamClient.IsValid ? SteamNative.WorkshopUpdate.CreateCommunityItem() : 0 );
	}

	internal static Task<UgcPublisher> CreateMtxItem()
	{
		return FinishCreatingItem( SteamClient.IsValid ? SteamNative.WorkshopUpdate.CreateMtxItem() : 0 );
	}

	static async Task<UgcPublisher> FinishCreatingItem( ulong call )
	{
		var item = await SteamCalls.GetResultAsync( call, SteamNative.WorkshopUpdate.GetCreatedItem );
		return item.IsNull ? null : new UgcPublisher( item );
	}

	internal static UgcPublisher OpenItem( ulong itemId )
	{
		var item = SteamNative.WorkshopUpdate.OpenCommunityItem( itemId );
		return new UgcPublisher( item );
	}

}
