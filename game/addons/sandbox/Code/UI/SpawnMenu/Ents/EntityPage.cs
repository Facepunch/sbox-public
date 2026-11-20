
/// <summary>
/// This component has a kill icon that can be used in the killfeed, or somewhere else.
/// </summary>
[Title( "#spawnmenu.tab.entity" ), Order( 2000 ), Icon( "🧠" )]
public class EntityPage : BaseSpawnMenu
{
	static Dictionary<string, string> CategoryIcons = new()
	{
		{ "Chair", "🪑" },
		{ "Pickup", "🧰" },
		{ "Weapon", "🔫" },
		{ "Vehicle", "🚕" },
		{ "World", "🌍" },
	};

	/// <summary>
	/// Translate known category labels without changing their resource or search identifiers.
	/// </summary>
	static string CategoryTitle( string category ) => category?.ToLowerInvariant() switch
	{
		"chair" => "#ui.entity.chair",
		"pickup" => "#ui.entity.pickup",
		"world" => "#ui.entity.world",
		"vehicle" => "#spawnmenu.entity.vehicle",
		"other" => "#spawnmenu.entity.other",
		_ => category
	};

	protected override void Rebuild()
	{
		AddOption( "\U0001f9e0", "#spawnmenu.entity.all", () => new EntityListCloud { IncludeLocalEntities = true, ExcludedLocalCategoryRoots = new[] { "Weapon", "Npc" }, Query = "-cat:weapon -cat:npc" } );

		var categories = ResourceLibrary.GetAll<ScriptedEntity>()
			.Where( e => !e.Developer || ServerSettings.ShowDeveloperEntities )
			.Select( e => SpawnMenuCategory.Root( e.Category ) )
			.Where( category => !string.Equals( category, "Weapon", StringComparison.OrdinalIgnoreCase ) && !string.Equals( category, "Npc", StringComparison.OrdinalIgnoreCase ) )
			.Distinct()
			.OrderBy( c => string.Equals( c, "Other", StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( c => LocalizedText.Resolve( CategoryTitle( c ) ), StringComparer.CurrentCultureIgnoreCase );

		var addedCategories = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var category in categories )
		{
			var cat = category; // capture for lambda
			var icon = CategoryIcons.GetValueOrDefault( cat, "📦" );
			addedCategories.Add( cat );
			AddOption( icon, CategoryTitle( cat ), () => new EntityListCloud { LocalCategory = cat, Query = $"cat:{cat.ToLowerInvariant()}" } );
		}

		void AddCloudCategory( string icon, string name, string category )
		{
			if ( !addedCategories.Add( category ) ) return;
			AddOption( icon, name, () => new EntityListCloud { Query = $"cat:{category}" } );
		}

		AddCloudCategory( "🐵", "#spawnmenu.entity.animals", "animal" );
		AddCloudCategory( "🥁", "#spawnmenu.entity.audio", "audio" );
		AddCloudCategory( "✨", "#spawnmenu.entity.effect", "effect" );
		AddCloudCategory( "🎈", "#spawnmenu.entity.other", "other" );
		AddCloudCategory( "💪", "#spawnmenu.entity.showcase", "showcase" );
		AddCloudCategory( "🧸", "#spawnmenu.entity.toys_and_fun", "toyfun" );
		AddCloudCategory( "🚚", "#spawnmenu.entity.vehicle", "vehicle" );
		// AddOption( "⭐", "Favourites", () => new EntityListCloud() { Query = "sort:favourite" } );
	}
}
