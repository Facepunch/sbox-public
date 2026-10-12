using System;

namespace Sandbox;

/// <summary>
/// Utils for uploading assets to the Steam Workshop. Clothing uploads a stub so it can be sold as a
/// steam inventory item, maps upload all of their files.
/// </summary>
static class WorkshopUtils
{
	/// <summary>
	/// Is this allowed to be uploaded?
	/// </summary>
	private static bool IsAllowed( Asset asset )
	{
		return asset.AssetType.FileExtension == "clothing";
	}

	static bool g_bNeedsLegalAgreement = false;

	/// <summary>
	/// Do we need to fill out the legal agreement in Steam?
	/// </summary>
	/// <returns></returns>
	public static bool NeedsLegalAgreement()
	{
		return g_bNeedsLegalAgreement;
	}

	/// <summary>
	/// Uploads a clothing asset to the steam workshop and embeds the published id in the asset's metadata
	/// </summary>
	public static async Task<bool> UploadAsset( Asset asset )
	{
		if ( !IsAllowed( asset ) )
			return false;

		// TODO: this should be generic to grab the name instead of getting a clothing resource.
		if ( !asset.TryLoadResource<Clothing>( out var clothing ) )
			return false;

		var addon = asset.Publishing.CreateTemporaryProject();

		// The content is just a json file with data in it
		DeleteTemporaryFolder( asset.Name );
		Editor.FileSystem.Transient.CreateDirectory( asset.Name );
		Editor.FileSystem.Transient.WriteJson( $"{asset.Name}/item.json", new WorkshopItemMetaData()
		{
			PackageIdent = addon.Config.FullIdent,
			Title = addon.Config.Title,
			WorkshopId = asset.MetaData.Get( "WorkshopId", 0UL )
		} );

		try
		{
			var itemId = await Upload( asset, asset.MetaData.Get( "WorkshopId", 0UL ), Editor.FileSystem.Transient.GetFullPath( asset.Name ), true, item =>
			{
				item.SetTitle( clothing.Title );

				if ( !string.IsNullOrEmpty( clothing.Icon.Path ) )
				{
					item.SetPreviewImage( FileSystem.Mounted.GetFullPath( clothing.Icon.Path ) );
				}
			} );

			return itemId != 0;
		}
		finally
		{
			DeleteTemporaryFolder( asset.Name );
		}
	}

	/// <summary>
	/// Delete a folder we've built a workshop item's content in
	/// </summary>
	internal static void DeleteTemporaryFolder( string name )
	{
		if ( Editor.FileSystem.Transient.DirectoryExists( name ) )
		{
			Editor.FileSystem.Transient.DeleteDirectory( name, true );
		}
	}

	/// <summary>
	/// Upload a folder as the content of this asset's workshop item, creating one if <paramref name="itemId"/>
	/// is 0, and remember the item's id in the asset's metadata. Returns the item id, or 0 if it failed.
	/// </summary>
	internal static async Task<ulong> Upload( Asset asset, ulong itemId, string contentFolder, bool microtransaction, Action<Services.Ugc.UgcPublisher> configure, string changeNote = "" )
	{
		var item = itemId > 0 ? Services.Ugc.OpenItem( itemId )
			: microtransaction ? await Services.Ugc.CreateMtxItem()
			: await Services.Ugc.CreateCommunityItem();

		if ( item is null )
		{
			Log.Warning( $"Couldn't create a workshop item for {asset.Path}" );
			return 0;
		}

		configure?.Invoke( item );
		item.SetContentFile( contentFolder );

		var result = await item.Submit( changeNote );

		g_bNeedsLegalAgreement = item.NeedsLegalAgreement;

		if ( !result )
		{
			Log.Warning( $"Couldn't upload {asset.Path} to workshop item {item.ItemId}" );
			return 0;
		}

		// Save workshop id now we've successfully uploaded to the workshop
		asset.MetaData.Set( "WorkshopId", item.ItemId );
		return item.ItemId;
	}
}
