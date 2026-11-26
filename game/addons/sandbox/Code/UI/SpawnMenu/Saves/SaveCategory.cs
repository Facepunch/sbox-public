namespace Sandbox;

/// <summary>
/// Workshop categories for saved worlds.
/// </summary>
public enum SaveCategory
{
	/// <summary>
	/// Buildings and other constructed environments.
	/// </summary>
	[Icon( "construction" ), Title( "#spawnmenu.saves.category.build" )]
	Build,

	/// <summary>
	/// Working machines and physics creations.
	/// </summary>
	[Icon( "precision_manufacturing" ), Title( "#spawnmenu.saves.category.contraption" )]
	Contraption,

	/// <summary>
	/// Staged scenes and posed characters.
	/// </summary>
	[Icon( "landscape" ), Title( "#ui.dupe.category.scene" )]
	Scene,

	/// <summary>
	/// Courses, puzzles and other playable challenges.
	/// </summary>
	[Icon( "flag" ), Title( "#spawnmenu.saves.category.challenge" )]
	Challenge,

	/// <summary>
	/// Saves outside the other categories.
	/// </summary>
	[Icon( "folder_open" ), Title( "#ui.dupe.category.other" )]
	Other
}
