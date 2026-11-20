using Sandbox.UI;

/// <summary>
/// This component has a kill icon that can be used in the killfeed, or somewhere else.
/// </summary>
[Title( "#spawnmenu.tab.dupes" ), Order( 3000 ), Icon( "content_copy" )]
public class DupesPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddHeader( "#spawnmenu.section.workshop" );
		AddOption( "trending_up", "#spawnmenu.dupes.popular", () => new DupesWorkshop() { SortOrder = WorkshopSortMode.Popular } );
		AddOption( "fiber_new", "#spawnmenu.dupes.newest", () => new DupesWorkshop() { SortOrder = WorkshopSortMode.Newest } );

		AddHeader( "#spawnmenu.section.categories" );

		foreach ( var entry in TypeLibrary.GetEnumDescription( typeof( DupeCategory ) ) )
		{
			AddOption( entry.Icon, entry.Title, () => new DupesWorkshop()
			{
				SortOrder = WorkshopSortMode.Popular,
				Category = entry.Name.ToString()
			} );
		}

		AddHeader( "#spawnmenu.section.local" );
		AddOption( "folder_open", "#spawnmenu.dupes.local", () => new DupesLocal() );
	}

	protected override void OnMenuFooter( Panel footer )
	{
		footer.AddChild<DupesFooter>();
	}
}

public enum DupeCategory
{
	/// <summary>
	/// Vehicle duplication category.
	/// </summary>
	[Icon( "directions_car" )]
	[Title( "#ui.dupe.category.vehicle" )]
	Vehicle,
	/// <summary>
	/// Robot duplication category.
	/// </summary>
	[Icon( "smart_toy" )]
	[Title( "#ui.dupe.category.robot" )]
	Robot,
	/// <summary>
	/// Plane duplication category.
	/// </summary>
	[Icon( "flight" )]
	[Title( "#ui.dupe.category.plane" )]
	Plane,
	/// <summary>
	/// Pose duplication category.
	/// </summary>
	[Icon( "accessibility_new" )]
	[Title( "#ui.dupe.category.pose" )]
	Pose,
	/// <summary>
	/// Weapon duplication category.
	/// </summary>
	[Icon( "gps_fixed" )]
	[Title( "#ui.dupe.category.weapon" )]
	Weapon,
	/// <summary>
	/// Art duplication category.
	/// </summary>
	[Icon( "palette" )]
	[Title( "#ui.dupe.category.art" )]
	Art,
	/// <summary>
	/// Scene duplication category.
	/// </summary>
	[Icon( "landscape" )]
	[Title( "#ui.dupe.category.scene" )]
	Scene,
	/// <summary>
	/// Game duplication category.
	/// </summary>
	[Icon( "sports_esports" )]
	[Title( "#ui.dupe.category.game" )]
	Game,
	/// <summary>
	/// Spaceship duplication category.
	/// </summary>
	[Icon( "rocket_launch" )]
	[Title( "#ui.dupe.category.spaceship" )]
	Spaceship,
	/// <summary>
	/// Machine duplication category.
	/// </summary>
	[Icon( "precision_manufacturing" )]
	[Title( "#ui.dupe.category.machine" )]
	Machine,
	/// <summary>
	/// Toys duplication category.
	/// </summary>
	[Icon( "toys" )]
	[Title( "#ui.dupe.category.toys" )]
	Toys,
	/// <summary>
	/// Trap duplication category.
	/// </summary>
	[Icon( "warning_amber" )]
	[Title( "#ui.dupe.category.trap" )]
	Trap,
	/// <summary>
	/// Boat duplication category.
	/// </summary>
	[Icon( "sailing" )]
	[Title( "#ui.dupe.category.boat" )]
	Boat,
	/// <summary>
	/// Other duplication category.
	/// </summary>
	[Icon( "folder_open" )]
	[Title( "#ui.dupe.category.other" )]
	Other
}

public enum DupeMovement
{
	/// <summary>
	/// Static movement classification.
	/// </summary>
	[Title( "#ui.dupe.movement.static" )]
	Static,
	/// <summary>
	/// Wheeled movement classification.
	/// </summary>
	[Title( "#ui.dupe.movement.wheeled" )]
	Wheeled,
	/// <summary>
	/// Flying movement classification.
	/// </summary>
	[Title( "#ui.dupe.movement.flying" )]
	Flying,
	/// <summary>
	/// Walking movement classification.
	/// </summary>
	[Title( "#ui.dupe.movement.walking" )]
	Walking,
	/// <summary>
	/// Water movement classification.
	/// </summary>
	[Title( "#ui.dupe.movement.water" )]
	Water,
	/// <summary>
	/// Tracked movement classification.
	/// </summary>
	[Title( "#ui.dupe.movement.tracked" )]
	Tracked
}
