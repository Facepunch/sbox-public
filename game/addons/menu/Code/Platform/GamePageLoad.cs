using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// Loads the Workshop details for a menu page, or the project on disk for a local game or map.
/// </summary>
public sealed class GamePageLoad( string ident, Package start = null, bool map = false )
{
	/// <summary>
	/// The Workshop ID or package ident from the menu link.
	/// </summary>
	public string Ident => ident;

	/// <summary>
	/// The local project opened by this link, if any.
	/// </summary>
	public Package Local { get; private set; }

	/// <summary>
	/// The published item displayed on this page.
	/// </summary>
	public WorkshopItemDetails WorkshopItem { get; private set; }

	/// <summary>
	/// Whether the menu is still looking up the item.
	/// </summary>
	public bool Loading { get; private set; } = true;

	/// <summary>
	/// Resolves maps by Workshop ID or package ident, including a local map project. Games also support package links.
	/// </summary>
	public async Task Run()
	{
		try
		{
			var link = map ? await MenuUtility.ResolveMapAsync( ident ) : ident;
			if ( map ) Local = MenuUtility.FindMapProject( link );

			if ( ulong.TryParse( link, out var id ) )
			{
				var item = await MenuUtility.FetchWorkshopItemAsync( id );
				if ( !map || item?.Tags.Contains( "map", StringComparer.OrdinalIgnoreCase ) == true )
					WorkshopItem = item;

				return;
			}

			if ( map ) return;

			var package = start;
			if ( package is null ) Package.TryGetCached( ident, out package );

			if ( package?.TypeName == "map" ) return;

			if ( package is { IsRemote: false } )
			{
				Local = package;
				if ( package.WorkshopId == 0 ) return;
			}

			id = Local?.WorkshopId ?? 0;
			if ( id == 0 && Package.TryParseIdentParts( ident, out var parts ) )
			{
				// Old menu links identify the item by the package key its publisher put on Steam.
				var query = new Storage.Query();
				query.TagsExcluded.Add( "map" );
				query.KeyValues["package"] = Package.FormatIdent( parts.org, parts.package );
				var result = await query.Run();
				if ( result.TotalCount != 1 || result.Items.FirstOrDefault() is not { } match ) return;
				id = match.Id;
			}

			WorkshopItem = await MenuUtility.FetchWorkshopItemAsync( id );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't load Workshop details for {ident}: {e.Message}" );
		}
		finally
		{
			Loading = false;
		}
	}
}
