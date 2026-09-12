using System.Diagnostics.CodeAnalysis;
using System.IO;
using Zio;

namespace Sandbox.Internal;

/// <summary>
/// A readonly filesystem that has a list of files, and a list of redirects. When accessing a file, it'll access the redirect file invisibly.
/// </summary>
class RedirectFileSystem : FSEventsPhysicalFileSystem
{
	public Dictionary<UPath, UPath> Files { get; } = new Dictionary<UPath, UPath>( UPathComparer.OrdinalIgnoreCase );

	public static RedirectFileSystem Create( string rootPath )
	{
		return new RedirectFileSystem();
	}

	private RedirectFileSystem() : base()
	{

	}

	protected override void Dispose( bool disposing )
	{
		if ( disposing )
		{
			// Free our reference to any symlinks
			foreach ( var (local, target) in Files )
			{
				NativeEngine.FullFileSystem.RemoveSymLink( local.FullName[1..], "GAME" );
			}
		}

		base.Dispose( disposing );
	}

	/// <summary>
	/// Add a redirect path
	/// </summary>
	public void AddAbsFile( string localPath, string absoluteTargetFile )
	{
		// Don't add files with no extension. UPath is dodgy, it allows paths like "blah/blah.dll/" and will resolve it to "blah/blah.dll"
		// which would allow people to bypass extension checks - unless we check for no extension!
		var extension = System.IO.Path.GetExtension( localPath );
		if ( string.IsNullOrWhiteSpace( extension ) ) return;

		localPath = localPath.NormalizeFilename( true );

		//	Log.Info( $"[{localPath}] => [{absoluteTargetFile}]" );

		Files[localPath] = ConvertPathFromInternal( absoluteTargetFile );

		// The engine wants a local path with no initial slash (which we normalize to), and an absolute path to map it to
		NativeEngine.FullFileSystem.AddSymLink( localPath[1..], "GAME", absoluteTargetFile );
	}

	protected override string ConvertPathToInternalImpl( UPath path )
	{
		if ( Files.TryGetValue( path, out var target ) )
		{
			return base.ConvertPathToInternalImpl( target );
		}

		return base.ConvertPathToInternalImpl( path );
	}

	protected override void CopyFileImpl( UPath srcPath, UPath destPath, bool overwrite )
	{
		throw new NotImplementedException();
	}

	protected override void CreateDirectoryImpl( UPath path )
	{
		throw new NotImplementedException();
	}

	protected override void DeleteDirectoryImpl( UPath path, bool isRecursive )
	{
		throw new NotImplementedException();
	}

	protected override void DeleteFileImpl( UPath path )
	{
		throw new NotImplementedException();
	}

	protected override bool DirectoryExistsImpl( UPath path )
	{
		var str = path.ToString();
		return Files.Any( x => x.Key.ToString().StartsWith( str, StringComparison.OrdinalIgnoreCase ) );
	}

	protected override IEnumerable<UPath> EnumeratePathsImpl( UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget )
	{
		var sp = SearchPattern.Parse( ref path, ref searchPattern );

		var files = searchTarget == SearchTarget.Directory ? Files.Keys
				.Select( x => x.GetDirectory() )
				.Distinct()
				.Where( x => x != path ) : Files.Keys;

		return files.Where( x => sp.Match( x ) && x.IsInDirectory( path, searchOption == SearchOption.AllDirectories ) );
	}

	protected override bool FileExistsImpl( UPath path )
	{
		//Log.Info( $"File Exists: {path}" );

		if ( Files.TryGetValue( path, out var realPath ) )
		{
			//Log.Info( $"    => {realPath}" );
			return base.FileExistsImpl( realPath );
		}

		return false;
	}

	protected override FileAttributes GetAttributesImpl( UPath path )
	{
		//Log.Info( $"GetAttributesImpl: {path}" );
		if ( Files.TryGetValue( path, out var realPath ) )
		{
			//Log.Info( $"    => {realPath}" );
			return base.GetAttributesImpl( realPath );
		}

		return default;
	}

	protected override DateTime GetCreationTimeImpl( UPath path )
	{
		if ( Files.TryGetValue( path, out var realPath ) )
			return base.GetCreationTimeImpl( realPath );

		return default;
	}

	protected override long GetFileLengthImpl( UPath path )
	{
		if ( Files.TryGetValue( path, out var realPath ) )
			return base.GetFileLengthImpl( realPath );

		return 0;
	}

	protected override DateTime GetLastAccessTimeImpl( UPath path )
	{
		throw new NotImplementedException();
	}

	protected override DateTime GetLastWriteTimeImpl( UPath path )
	{
		throw new NotImplementedException();
	}

	protected override void MoveDirectoryImpl( UPath srcPath, UPath destPath )
	{
		throw new NotImplementedException();
	}

	protected override void MoveFileImpl( UPath srcPath, UPath destPath )
	{
		throw new NotImplementedException();
	}

	protected override Stream OpenFileImpl( UPath path, FileMode mode, FileAccess access, FileShare share )
	{
		//Log.Info( $"OpenFileImpl: {path}" );
		if ( Files.TryGetValue( path, out var realPath ) )
		{
			//Log.Info( $"    => {realPath}" );
			return base.OpenFileImpl( realPath, mode, access, share );
		}

		return null;
	}

	protected override void ReplaceFileImpl( UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors )
	{
		throw new NotImplementedException();
	}

	protected override void SetAttributesImpl( UPath path, FileAttributes attributes )
	{
		throw new NotImplementedException();
	}

	protected override void SetCreationTimeImpl( UPath path, DateTime time )
	{
		throw new NotImplementedException();
	}

	protected override void SetLastAccessTimeImpl( UPath path, DateTime time )
	{
		throw new NotImplementedException();
	}

	protected override void SetLastWriteTimeImpl( UPath path, DateTime time )
	{
		throw new NotImplementedException();
	}

	protected override IFileSystemWatcher WatchImpl( UPath path )
	{
		return null;
	}

	protected override bool CanWatchImpl( UPath path )
	{
		return false;
	}
}


file class UPathComparer : IEqualityComparer<UPath>
{
	internal static UPathComparer OrdinalIgnoreCase = new UPathComparer();

	public bool Equals( UPath x, UPath y )
	{
		return string.Equals( x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase );
	}

	public int GetHashCode( [DisallowNull] UPath obj )
	{
		return obj.FullName?.ToLowerInvariant().GetHashCode() ?? 0;
	}
}
