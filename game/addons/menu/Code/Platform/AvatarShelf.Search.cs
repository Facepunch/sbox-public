using Sandbox;
using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// The Avatar page's search - on the bar in place of the tabs or a slot's heading, over what's being looked at
/// </summary>
partial class AvatarShelf
{
	bool Searching => Head?.Searching ?? false;

	string Query;

	bool HasQuery => !string.IsNullOrWhiteSpace( Query );

	bool Matches( Clothing clothing ) => !HasQuery || (clothing.Title?.Contains( Query.Trim(), StringComparison.OrdinalIgnoreCase ) ?? false);

	string SearchHint => Category is { } category ? $"Search {category.Title?.ToLowerInvariant()}"
		: On == Tab.Workshop ? "Search the Workshop"
		: On == Tab.Inventory ? "Search your items"
		: "Search the shop";

	void CloseSearch()
	{
		Head?.CloseSearch();
		Query = null;
		ScrollToTop();
	}

	string typed;
	RealTimeSince sinceTyped;

	void TickSearch()
	{
		if ( typed != Query )
		{
			typed = Query;
			sinceTyped = 0;
			ScrollToTop();
		}

		if ( OnTabs && On == Tab.Workshop && sinceTyped > 0.4f )
		{
			var query = (Query ?? "").Trim();
			if ( query != workshopQuery )
			{
				workshopQuery = query;
				if ( Picked is not null ) PutDown();
				_ = LoadWorkshop();
			}
		}
	}
}
