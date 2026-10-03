using System.IO;

namespace Sandbox;

/// <summary>
/// A snapshot of the files under a folder, each with the filesystem it resolves to - the one an aggregate
/// would pick. Bulk work can check and open files without searching every mounted filesystem per file,
/// which is what an aggregate does for each FileExists or open. It doesn't update: a file created after
/// the snapshot isn't in it, so only use it for work that runs right after taking it.
/// </summary>
internal sealed class FileIndex
{
	readonly Zio.UPath _folder;

	/// <summary>
	/// Path -> the filesystem an aggregate resolves it to. Case-insensitive like the physical filesystems
	/// under it, and filled highest priority first: an aggregate asks each filesystem in that order whether
	/// it has the path, and those match any casing.
	/// </summary>
	readonly Dictionary<string, Zio.IFileSystem> _owners;

	/// <summary>
	/// Every indexed file, relative to the indexed folder like <see cref="BaseFileSystem.FindFile"/>
	/// returns them, sorted by path.
	/// </summary>
	public IReadOnlyList<string> Files { get; }

	internal FileIndex( Zio.UPath folder, SortedSet<Zio.UPath> paths, Dictionary<string, Zio.IFileSystem> owners )
	{
		_folder = folder;
		_owners = owners;

		var folderLength = folder.FullName.Length;
		var files = new List<string>( paths.Count );
		foreach ( var path in paths )
			files.Add( path.FullName.Substring( folderLength ).Trim( '/' ) );

		Files = files;
	}

	/// <summary>
	/// True if the file was there when the snapshot was taken.
	/// </summary>
	public bool Contains( string path ) => TryGet( path, out _, out _ );

	/// <summary>
	/// The path of a file relative to the indexed folder, and the filesystem it resolves to, if it's in the snapshot.
	/// </summary>
	internal bool TryGet( string path, out Zio.UPath fullPath, out Zio.IFileSystem owner )
	{
		fullPath = _folder / path.Replace( '\\', '/' ).Trim( '/' );
		return _owners.TryGetValue( fullPath.FullName, out owner );
	}

	/// <summary>
	/// Read a file from the filesystem it resolves to. False if it isn't in the snapshot, or has gone since.
	/// </summary>
	public bool TryReadAllBytes( string path, out byte[] bytes )
	{
		bytes = null;

		if ( !TryGet( path, out var fullPath, out var owner ) )
			return false;

		try
		{
			using var stream = owner.OpenFile( fullPath, FileMode.Open, FileAccess.Read, FileShare.Read );
			bytes = new byte[stream.Length];
			stream.ReadExactly( bytes );
			return true;
		}
		catch ( FileNotFoundException ) { return false; }
		catch ( DirectoryNotFoundException ) { return false; }
	}
}
