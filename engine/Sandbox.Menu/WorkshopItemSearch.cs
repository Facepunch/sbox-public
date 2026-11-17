namespace Sandbox;

/// <summary>
/// A paged search of Workshop games, maps or addons.
/// </summary>
public sealed class WorkshopItemSearch
{
	readonly Workshop.Query query;
	Workshop.QueryResult page;

	internal WorkshopItemSearch( Workshop.Query query )
	{
		this.query = query;
	}

	/// <summary>
	/// There's another page to ask for
	/// </summary>
	public bool HasMore { get; private set; } = true;

	/// <summary>
	/// The next page's items - none once there aren't any more, or the Workshop couldn't be asked.
	/// </summary>
	public async Task<WorkshopItemDetails[]> NextAsync()
	{
		if ( !HasMore ) return [];

		page = page is null ? await query.RunAsync() : await page.GetNextResults();
		HasMore = page?.HasMoreResults() ?? false;

		return await MenuUtility.DetailsOfItemsAsync( page?.Items ?? [] );
	}
}
