using System.IO;

namespace Sandbox;

/// <summary>
/// Projects in <c>addons/local/</c>, whose assets are mounted into whichever game is being edited or hosted from its
/// project - in the editor, or a dedicated server launched with a .sbproj. Use this to work on content that's published
/// separately, like an addon, and use it from the game you test it in. Only assets are mounted; the projects' code isn't
/// compiled, and the editor never publishes them with the game.
/// </summary>
internal static class LocalAddons
{
	/// <summary>
	/// The folder, relative to the engine root, whose projects are mounted.
	/// </summary>
	public const string Folder = "addons/local";

	static readonly List<(Project Project, BaseFileSystem Assets, string Path)> _mounted = new();

	/// <summary>
	/// Every mounted project's assets. Mounted into the game filesystem each time the game instance resets, so
	/// projects mounted later show up in it too.
	/// </summary>
	public static AggregateFileSystem Content { get; } = new();

	/// <summary>
	/// Projects whose assets are mounted.
	/// </summary>
	public static IEnumerable<Project> Projects => _mounted.Select( x => x.Project );

	/// <summary>
	/// Each mounted project's assets filesystem.
	/// </summary>
	public static IEnumerable<BaseFileSystem> FileSystems => _mounted.Select( x => x.Assets );

	public static bool IsMounted( Project project ) => _mounted.Any( x => x.Project == project );

	/// <summary>
	/// Is this file inside a mounted project's assets? Those belong to their own project, so aren't published with the game.
	/// </summary>
	public static bool Contains( string absolutePath )
	{
		if ( string.IsNullOrEmpty( absolutePath ) || _mounted.Count == 0 ) return false;

		var path = Path.GetFullPath( absolutePath );
		return _mounted.Any( x => path.StartsWith( x.Path, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Mount the assets of every project in <see cref="Folder"/>, except <paramref name="current"/>. Projects already mounted are skipped.
	/// </summary>
	internal static void MountAll( Project current )
	{
		if ( Application.IsStandalone ) return;

		var root = EngineFileSystem.Root.GetFullPath( Folder );
		if ( !Directory.Exists( root ) ) return;

		foreach ( var folder in Directory.EnumerateDirectories( root ).Order( StringComparer.OrdinalIgnoreCase ) )
		{
			try
			{
				var configs = Directory.EnumerateFiles( folder, "*.sbproj" ).ToArray();
				if ( configs.Length != 1 ) continue;

				// Registered inactive, so its code isn't compiled
				var project = Project.AddFromFile( configs[0], active: false );
				if ( project is null || project == current ) continue;

				Mount( project );
			}
			catch ( Exception e )
			{
				Log.Warning( $"Couldn't mount addon in {folder}: {e.Message}" );
			}
		}
	}

	static void Mount( Project project )
	{
		if ( IsMounted( project ) || !project.HasAssetsPath() ) return;

		var assetsPath = Path.GetFullPath( project.GetAssetsPath() );

		// Our own filesystem, the project's is recreated (and the old one disposed) whenever it reloads
		var assets = new LocalFileSystem( assetsPath );
		Content.Mount( assets );

		// Found by the native filesystem and asset system
		NativeEngine.FullFileSystem.AddProjectPath( project.Config.FullIdent, assetsPath );

		_mounted.Add( (project, assets, assetsPath.TrimEnd( '\\', '/' ) + Path.DirectorySeparatorChar) );

		Log.Info( $"Mounted assets from {project.Config.Title} ({assetsPath})" );
	}
}
