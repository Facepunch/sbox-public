using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// A filesystem that can be accessed by the game.
/// </summary>
public static class FileSystem
{
	/// <summary>
	/// Normalizes given file path so the game's filesystem can understand it. Fixes slashes and lowercases the file path.
	/// </summary>
	/// <param name="filepath">The file path to normalize</param>
	/// <returns>The normalized file path</returns>
	public static string NormalizeFilename( string filepath ) => BaseFileSystem.NormalizeFilename( filepath );

	/// <summary>
	/// All mounted content.
	/// </summary>
	public static BaseFileSystem Mounted => GlobalContext.Current.FileMount;

	/// <summary>
	/// A subset of <see cref="OrganizationData"/> for custom gamemode data.
	/// </summary>
	public static BaseFileSystem Data => GlobalContext.Current.FileData;

	/// <summary>
	/// A filesystem for custom data, per gamemode's organization.
	/// </summary>
	public static BaseFileSystem OrganizationData => GlobalContext.Current.FileOrg;

	/// <summary>
	/// A cached keystore that can be used for anything. This is stored in a global cache folder, and may be deleted at any time.
	/// </summary>
	public static KeyStore Cache { get => field ??= KeyStore.CreateGlobalCache(); }

	/// <summary>
	/// Create a filesystem that exists only in memory
	/// </summary>
	public static BaseFileSystem CreateMemoryFileSystem() => new MemoryFileSystem();
}
