using Sandbox.Mounting;

/// <summary>
/// Prefabs a mounted game builds from its own content and tags for spawning: <see cref="NpcTag"/>,
/// <see cref="WeaponTag"/> or <see cref="ItemTag"/>. They're built the first time they're loaded, which is also when
/// their title and icon metadata become readable.
/// </summary>
public static class MountedPrefabs
{
	public const string NpcTag = "npc";
	public const string WeaponTag = "weapon";
	public const string ItemTag = "item";

	/// <summary>
	/// The mounted games' prefabs with this tag.
	/// </summary>
	public static IEnumerable<Sandbox.Mounting.ResourceLoader> WithTag( string tag ) => Directory.GetAll()
		.Where( x => x.Mounted )
		.Select( x => Directory.Get( x.Ident ) )
		.Where( x => x?.IsMounted == true )
		.SelectMany( x => x.GetAll( Sandbox.Mounting.ResourceType.PrefabFile ) )
		.Where( x => x.Tags.Contains( tag ) );

	const string WithoutCookie = "spawnmenu.mounts.without";
	static HashSet<string> _without;

	/// <summary>
	/// "ident/tag" for each game seen mounted without prefabs of that tag. What an unmounted game offers can't be known
	/// until it's mounted, so this is what keeps the mount shortcut off tabs a game has nothing for.
	/// </summary>
	static HashSet<string> Without => _without ??= new( Game.Cookies.Get( WithoutCookie, new List<string>() ), StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Note which tags each mounted game offers.
	/// </summary>
	public static void Learn()
	{
		var changed = false;
		foreach ( var mount in Directory.GetAll().Where( x => x.Mounted ).Select( x => Directory.Get( x.Ident ) ).Where( x => x?.IsMounted == true ) )
		{
			var tags = mount.GetAll( Sandbox.Mounting.ResourceType.PrefabFile ).SelectMany( x => x.Tags ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			foreach ( var tag in new[] { NpcTag, WeaponTag, ItemTag } )
				changed |= tags.Contains( tag ) ? Without.Remove( $"{mount.Ident}/{tag}" ) : Without.Add( $"{mount.Ident}/{tag}" );
		}

		if ( changed ) Game.Cookies.Set( WithoutCookie, Without.ToList() );
	}

	/// <summary>
	/// Installed games that aren't mounted and may offer prefabs with this tag: all of them, less the ones seen without.
	/// </summary>
	public static IEnumerable<MountInfo> Unmounted( string tag ) => Directory.GetAll()
		.Where( x => x.Available && !x.Mounted && !Without.Contains( $"{x.Ident}/{tag}" ) );

	/// <summary>
	/// The game a mount:// path is from, such as "Half-Life 2".
	/// </summary>
	public static string GameTitle( string path ) => MountUtility.TryParse( path, out string ident ) ? Directory.Get( ident )?.Title : null;

	/// <summary>
	/// The prefab, built if it hasn't been yet. Null when its game isn't mounted.
	/// </summary>
	public static PrefabFile Load( string path ) => IsListed( path ) ? PrefabFile.Load( path ) : null;

	/// <summary>
	/// The prefab if it's been built already, without building it.
	/// </summary>
	public static PrefabFile Loaded( string path ) => ResourceLibrary.Get<PrefabFile>( path );

	/// <summary>
	/// Whether this path is a prefab a mounted game provides.
	/// </summary>
	public static bool IsListed( string path ) => MountUtility.TryParse( path, out string ident )
		&& Directory.Get( ident ) is { IsMounted: true } mount && mount.GetByPath( path ) is not null;

	public static bool HasTag( string path, string tag ) => MountUtility.TryParse( path, out string ident )
		&& Directory.Get( ident )?.GetByPath( path )?.Tags.Contains( tag ) == true;

	/// <summary>
	/// Its title once built, until then one made from its file name: combine_soldier is "Combine Soldier".
	/// </summary>
	public static string Title( string path )
	{
		if ( Loaded( path )?.GetMetadata( "Title" ) is { Length: > 0 } title )
			return title;

		var name = System.IO.Path.GetFileNameWithoutExtension( path ).Replace( '_', ' ' );
		return string.Join( ' ', name.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( w => char.ToUpperInvariant( w[0] ) + w[1..] ) );
	}
}
