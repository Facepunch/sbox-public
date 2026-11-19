using Microsoft.AspNetCore.Components;

namespace MenuProject.Platform;

/// <summary>
/// Your games' rows on the shelf - a page needs a route of its own, and a panel has the one
/// </summary>
[Route( PlatformShelf.YourGames )]
public class YourGamesShelf : GameRowsShelf
{
}

[Route( PlatformShelf.Discover )]
public class DiscoverShelf : GameRowsShelf
{
	protected override bool IsDiscover => true;
}
