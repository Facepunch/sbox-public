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
		[.. new[] { ("core", "core"), ("citizen", "addons/citizen/Assets"), ("menu", "addons/menu/Assets") }
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
}
