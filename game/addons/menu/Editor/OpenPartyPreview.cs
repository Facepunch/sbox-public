using Editor.Mcp;
using MenuProject.MenuUI.Front;

public static class OpenPartyPreview
{
	/// <summary>
	/// Toggle the home sidebar's mock open parties, with different games and party sizes.
	/// </summary>
	[McpTool( "menu_mock_open_parties" )]
	public static object Toggle( bool enabled = true )
	{
		LookingToPlay.MockOpenParties = enabled;
		return new { Enabled = LookingToPlay.MockOpenParties };
	}

	/// <summary>
	/// Show the compact strip or expanded open-party cards in the home sidebar.
	/// </summary>
	[McpTool( "menu_open_parties_collapsed" )]
	public static object SetCollapsed( bool collapsed = true )
	{
		LookingToPlay.Collapsed = collapsed;
		return new { Collapsed = LookingToPlay.Collapsed };
	}
}
