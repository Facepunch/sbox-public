using System.Text.Json;

namespace Sandbox.ContentBuild;

/// <summary>
/// Where each content root keeps its build state, shared by SboxBuild and the editor so neither rebuilds
/// what the other built.
/// </summary>
public static class ContentRoots
{
	/// <summary>
	/// Engine content roots under game/, in mount order.
	/// </summary>
	public static ContentRoot[] Engine( string gameDirectory ) =>
		[.. new[] { ("core", "core"), ("citizen", "addons/citizen/Assets"), ("content", "addons/content/Assets"), ("menu", "addons/menu/Assets") }
			.Select( x => new ContentRoot( x.Item1, Path.GetFullPath( Path.Combine( gameDirectory, x.Item2 ) ), Path.Combine( gameDirectory, ".source2", "contentbuild", x.Item1 ) ) )];

	/// <summary>
	/// The engine roots followed by projects (libraries, then the open project), each mounted after the ones before.
	/// A project whose assets are an engine root's folder (the menu project itself) is mounted again under its own
	/// ident, which is the mod its compiles write to: that root takes the ident and the project's place in the
	/// order, keeping the engine root's records and lock.
	/// </summary>
	public static List<ContentRoot> WithProjects( IEnumerable<ContentRoot> engine, IEnumerable<(string Ident, string AssetsPath, string ProjectDirectory)> projects )
	{
		var roots = engine.ToList();
		foreach ( var (ident, assets, directory) in projects )
		{
			var path = Path.GetFullPath( assets );
			var same = roots.FindIndex( x => string.Equals( x.Path, path, StringComparison.OrdinalIgnoreCase ) );
			if ( same < 0 )
			{
				roots.Add( new ContentRoot( ident, path, ProjectState( directory ) ) );
				continue;
			}

			var root = roots[same];
			roots.RemoveAt( same );
			roots.Add( root with { Mod = ident } );
		}

		return roots;
	}

	/// <summary>
	/// Short, because compiled paths under its staging have to fit the native MAX_PATH.
	/// </summary>
	public static string ProjectState( string projectDirectory ) => Path.Combine( projectDirectory, ".sbox", "build" );

	/// <summary>
	/// Where the engine roots' build publishes the generated files they share.
	/// </summary>
	public static string EngineTransients( string gameDirectory ) => Path.Combine( gameDirectory, "addons", "menu", "Transients" );

	/// <summary>
	/// Every compiled file a content build of the engine roots or a project under game/addons has a record of: that
	/// build checks it against its sources and compiles it again when they change, so nothing else should replace it.
	/// Read only, unlike opening the roots' stores, which recovers interrupted publications.
	/// </summary>
	public static HashSet<string> RecordedOutputs( string gameDirectory )
	{
		var roots = Engine( gameDirectory ).Select( x => (x.Path, x.StateDirectory, Transients: EngineTransients( gameDirectory )) ).ToList();
		var addons = new DirectoryInfo( Path.Combine( gameDirectory, "addons" ) );
		var anyCase = new EnumerationOptions { AttributesToSkip = 0, MatchCasing = MatchCasing.CaseInsensitive };
		foreach ( var project in addons.Exists ? addons.EnumerateDirectories( "*", anyCase ) : [] )
		{
			var assets = project.EnumerateDirectories( "Assets", anyCase ).FirstOrDefault()?.FullName;
			if ( assets is null || roots.Any( x => string.Equals( x.Path, assets, StringComparison.OrdinalIgnoreCase ) ) ) continue;

			// The editor publishes a project's generated files under it (see EditorContentBuild)
			roots.Add( (assets, ProjectState( project.FullName ), Path.Combine( project.FullName, ".sbox", "transient" )) );
		}

		var records = roots
			.Where( root => Directory.Exists( Path.Combine( root.StateDirectory, "records" ) ) )
			.SelectMany( root => Directory.GetFiles( Path.Combine( root.StateDirectory, "records" ), "*.json" ).Select( file => (Root: root, File: file) ) );

		return records.AsParallel()
			.SelectMany( x => JsonSerializer.Deserialize<AssetRecord>( File.ReadAllText( x.File ) ).Outputs.Keys.Select( key =>
			{
				var colon = key.IndexOf( ':' );
				var directory = colon < 0 ? x.Root.Path : string.Equals( key[..colon], "transient", StringComparison.OrdinalIgnoreCase ) ? x.Root.Transients : null;
				return directory is null ? null : Path.GetFullPath( Path.Combine( directory, key[(colon + 1)..] ) );
			} ) )
			.Where( x => x is not null )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );
	}
}
