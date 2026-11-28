using Sandbox.Mounting;
using System.Runtime.CompilerServices;

/// <summary>
/// Spawnlists a mounted game builds from its Workshop and installed addons, one per addon. They're JSON text resources tagged
/// <see cref="Tag"/> in the shape of a <see cref="SpawnlistResource"/>.
/// </summary>
public static class MountedSpawnlists
{
	public const string Tag = "spawnlist";

	static readonly ConditionalWeakTable<ResourceLoader, SpawnlistResource> Lists = new();

	/// <summary>
	/// For each mounted game with addons, a "Workshop" list of all their models under the game's top level list,
	/// followed by one list of its own for each addon under that.
	/// </summary>
	public static IEnumerable<SpawnlistResource> Build()
	{
		foreach ( var game in Addons().GroupBy( x => x.Ident ) )
		{
			var addons = game.Select( x => x.List ).OrderBy( x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase ).ToList();
			var workshop = new SpawnlistResource
			{
				Title = "Workshop",
				Icon = "extension",
				RequiredMount = game.Key,
				Order = 1000,
				Parent = ResourceLibrary.GetAll<SpawnlistResource>().FirstOrDefault( x => x.RequiredMount == game.Key && x.Parent is null && !x.IsRemote )
			};

			foreach ( var addon in addons )
			{
				workshop.Items.Add( new SpawnlistResource.Entry { Kind = SpawnlistItemKind.Title, Title = addon.DisplayName } );
				workshop.Items.AddRange( addon.Items.Where( x => x.Kind == SpawnlistItemKind.Item ) );
				addon.Parent = workshop;
			}

			yield return workshop;
			foreach ( var addon in addons )
				yield return addon;
		}
	}

	static IEnumerable<(string Ident, SpawnlistResource List)> Addons() => Directory.GetAll()
		.Where( x => x.Mounted )
		.Select( x => Directory.Get( x.Ident ) )
		.Where( x => x?.IsMounted == true )
		.SelectMany( mount => mount.GetAll( ResourceType.Text )
			.Where( x => x.Tags.Contains( Tag ) )
			.Select( x => (Ident: mount.Ident, List: Load( mount.Ident, x )) ) )
		.Where( x => x.List is not null );

	static SpawnlistResource Load( string ident, ResourceLoader loader )
	{
		if ( Lists.TryGetValue( loader, out var list ) )
			return list;

		// The mount builds the list up front, so this finishes without waiting.
		if ( loader.GetOrCreate().GetAwaiter().GetResult() is not string json )
			return null;

		list = new SpawnlistResource { Icon = "extension", RequiredMount = ident };
		list.LoadFromJson( json );
		Lists.Add( loader, list );
		return list;
	}
}
