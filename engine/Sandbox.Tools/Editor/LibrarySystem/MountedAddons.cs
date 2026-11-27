namespace Editor;

/// <summary>
/// Local projects in <c>addons/local/</c> whose assets are mounted into the open project while it's being edited,
/// without becoming part of it. Use this to work on content that's published separately - an addon, say - and use
/// it from the game you test it in. Only assets are mounted; the projects' code isn't compiled, and they're never
/// published with the open project.
/// </summary>
public static class MountedAddons
{
	/// <summary>
	/// The folder, relative to the engine root, whose projects are mounted.
	/// </summary>
	public const string Folder = LocalAddons.Folder;

	/// <summary>
	/// Projects whose assets are mounted into the open project.
	/// </summary>
	public static IEnumerable<Project> All => LocalAddons.Projects;

	/// <summary>
	/// The asset filesystems of the mounted projects, for the editor's content path.
	/// </summary>
	internal static IEnumerable<BaseFileSystem> FileSystems => LocalAddons.FileSystems;

	/// <summary>
	/// Whether this project's assets are mounted into the open project.
	/// </summary>
	public static bool IsMounted( Project project ) => LocalAddons.IsMounted( project );

	/// <summary>
	/// Whether this file is inside a mounted project's assets, which are published with their own project, not this one.
	/// </summary>
	public static bool Contains( string absolutePath ) => LocalAddons.Contains( absolutePath );

	/// <summary>
	/// Mount every project in <see cref="Folder"/>. Called on startup before the content path and asset system are
	/// built, so they pick these up.
	/// </summary>
	internal static void InitializeFromProject( Project current ) => LocalAddons.MountAll( current );
}
