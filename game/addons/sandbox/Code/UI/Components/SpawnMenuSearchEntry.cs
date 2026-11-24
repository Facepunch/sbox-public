using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Search field used by spawn menu toolbars.
/// </summary>
public sealed class SpawnMenuSearchEntry : TextEntry
{
	/// <summary>
	/// Sets the toolbar styling and the default search prompt.
	/// </summary>
	public SpawnMenuSearchEntry()
	{
		AddClass( "filter menu-input menu-control" );
		Placeholder = "#spawnmenu.common.search";
	}
}
