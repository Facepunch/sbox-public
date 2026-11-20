using Sandbox.UI;

/// <summary>
/// This component has a kill icon that can be used in the killfeed, or somewhere else.
/// </summary>
[Title( "#spawnmenu.tab.dupes" ), Order( 3000 ), Icon( "✌️" )]
public class DupesPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddHeader( "#spawnmenu.section.workshop" );
		AddOption( "🎖️", "#spawnmenu.dupes.popular", () => new DupesWorkshop() { SortOrder = WorkshopSortMode.Popular } );
		AddOption( "🐣", "#spawnmenu.dupes.newest", () => new DupesWorkshop() { SortOrder = WorkshopSortMode.Newest } );

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
		AddOption( "📂", "#spawnmenu.dupes.local", () => new DupesLocal() );
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
	[Icon( "🚗" )]
	[Title( "#ui.dupe.category.vehicle" )]
	Vehicle,
	/// <summary>
	/// Robot duplication category.
	/// </summary>
	[Icon( "🤖" )]
	[Title( "#ui.dupe.category.robot" )]
	Robot,
	/// <summary>
	/// Plane duplication category.
	/// </summary>
	[Icon( "✈️" )]
	[Title( "#ui.dupe.category.plane" )]
	Plane,
	/// <summary>
	/// Pose duplication category.
	/// </summary>
	[Icon( "🕺🏼" )]
	[Title( "#ui.dupe.category.pose" )]
	Pose,
	/// <summary>
	/// Weapon duplication category.
	/// </summary>
	[Icon( "🏹" )]
	[Title( "#ui.dupe.category.weapon" )]
	Weapon,
	/// <summary>
	/// Art duplication category.
	/// </summary>
	[Icon( "🖼️" )]
	[Title( "#ui.dupe.category.art" )]
	Art,
	/// <summary>
	/// Scene duplication category.
	/// </summary>
	[Icon( "🏠" )]
	[Title( "#ui.dupe.category.scene" )]
	Scene,
	/// <summary>
	/// Game duplication category.
	/// </summary>
	[Icon( "🎳" )]
	[Title( "#ui.dupe.category.game" )]
	Game,
	/// <summary>
	/// Spaceship duplication category.
	/// </summary>
	[Icon( "🛸" )]
	[Title( "#ui.dupe.category.spaceship" )]
	Spaceship,
	/// <summary>
	/// Machine duplication category.
	/// </summary>
	[Icon( "🎰" )]
	[Title( "#ui.dupe.category.machine" )]
	Machine,
	/// <summary>
	/// Toys duplication category.
	/// </summary>
	[Icon( "🧸" )]
	[Title( "#ui.dupe.category.toys" )]
	Toys,
	/// <summary>
	/// Trap duplication category.
	/// </summary>
	[Icon( "🪤" )]
	[Title( "#ui.dupe.category.trap" )]
	Trap,
	/// <summary>
	/// Boat duplication category.
	/// </summary>
	[Icon( "⛵" )]
	[Title( "#ui.dupe.category.boat" )]
	Boat,
	/// <summary>
	/// Other duplication category.
	/// </summary>
	[Icon( "📂" )]
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
