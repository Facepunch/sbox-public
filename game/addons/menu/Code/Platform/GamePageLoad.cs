using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// Loads the Workshop details for a menu page, or the project on disk for an unpublished local game.
/// </summary>
public sealed class GamePageLoad( string ident, Package start )
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
	/// Loads a Workshop item by ID, or resolves a link that still uses a package ident.
	/// </summary>
	public async Task Run()
	{
		try
		{
			if ( ulong.TryParse( ident, out var id ) )
			{
				WorkshopItem = await MenuUtility.FetchWorkshopItemAsync( id );
				return;
			}

			var package = start;
			if ( package is null ) Package.TryGetCached( ident, out package );

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
