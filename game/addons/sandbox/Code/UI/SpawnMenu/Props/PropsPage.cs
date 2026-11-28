
/// <summary>
/// This component has a kill icon that can be used in the killfeed, or somewhere else.
/// </summary>
[Title( "#spawnmenu.tab.spawnlists" ), Order( 0 ), Icon( "view_list" )]
public class PropsPage : SpawnlistsPage
{
	protected override void AddProceduralSpawnlists()
	{
		var builder = AddOption( "carpenter", "Building Props", () => new BuilderPage() );
		builder.Key = "builder";
		foreach ( var category in BuilderCatalog.Categories )
		{
			AddOption( builder, new SpawnMenuOption
			{
				Name = category,
				Key = $"builder/{category}",
				Icon = "category",
				PanelCreator = () => new BuilderPage { Category = category }
			} );
		}

		// ready-made building pieces live in spawnlists kept out of the sidebar, shown here instead
		AddBuildingSet( builder, "robotics", "spawnlists/sandbox/robotics.spawn" );
		AddBuildingSet( builder, "steel_structures", "spawnlists/sandbox/steel_structures.spawn" );
		AddBuildingSet( builder, "gears", "spawnlists/sandbox/gears.spawn" );
		AddBuildingSet( builder, "wheels", "spawnlists/sandbox/wheels.spawn" );
	}

	void AddBuildingSet( SpawnMenuOption builder, string key, string path )
	{
		var list = ResourceLibrary.Get<SpawnlistResource>( path );
		if ( list is null )
			return;

		AddOption( builder, new SpawnMenuOption
		{
			Name = list.DisplayName,
			Key = $"builder/{key}",
			Icon = list.Icon,
			IconTexture = list.IconImage,
			Tokenize = false,
			PanelCreator = () => new SpawnlistView { Resource = list }
		} );
	}

	protected override void Rebuild()
	{
		AddSpawnlistOptions();

		AddHeader( "#spawnmenu.section.workshop" );
		AddOption( "apps", "#spawnmenu.props.all", () => new SpawnPageCloud() );

		foreach ( var category in WorkshopCategories.OrderBy( x => LocalizedText.Resolve( x.Label ), StringComparer.CurrentCultureIgnoreCase ) )
		{
			AddOption( category.Icon, category.Label, () => new SpawnPageCloud { Category = category.Slug } );
		}
	}

	static readonly (string Icon, string Label, string Slug)[] WorkshopCategories =
	[
		("pets", "#spawnmenu.props.animal", "animal"),
		("house", "#spawnmenu.props.architecture", "building"),
		("broken_image", "#spawnmenu.props.debris", "debris"),
		("fence", "#spawnmenu.props.fence_wall", "fence"),
		("restaurant", "#spawnmenu.props.food", "food"),
		("chair", "#spawnmenu.props.furniture", "furniture"),
		("person", "#spawnmenu.props.humans", "human"),
		("lightbulb", "#spawnmenu.props.lighting", "light"),
		("park", "#spawnmenu.props.nature", "nature"),
		("category", "#spawnmenu.props.props", "prop"),
		("toys", "#spawnmenu.props.toy", "toy"),
		("local_shipping", "#spawnmenu.props.vehicle", "vehicle"),
		("target", "#spawnmenu.props.weapon_props", "worldmodel"),
	];
}
