using Zio;
using Zio.FileSystems;
using System.IO;
using System.Drawing;
using System.Reflection;
namespace Sandbox;


internal class CasefoldPhysicalFileSystem : PhysicalFileSystem
{

	/// <summary>
	/// An adaptation of POSIX implementation of casepath in C# world.
	/// WARNING: Handles only absolute paths. Also assumes that it's run under Linux or MacOS.
	/// On Windows, this function passes through inPath.
	/// </summary>
	/// <param name="inPath">path as fed through the parameter</param>
	/// <returns>the same path with proper casing. This is just partially adjusted if only part of the path exists.</returns>
	internal UPath Casepath( UPath inPath )
	{
        return ConvertPathFromInternalImpl(FileSystemStringCaseHelpers.Casepath(ConvertPathToInternalImpl(inPath)));
	}

	//directory API

	protected override void CreateDirectoryImpl( UPath path )
	{
		base.CreateDirectoryImpl( Casepath( path ) );
	}

	protected override bool DirectoryExistsImpl( UPath path )
	{
		return base.DirectoryExistsImpl( Casepath( path ) );
	}

	protected override void MoveDirectoryImpl( UPath srcPath, UPath destPath )
	{
		base.MoveDirectoryImpl( Casepath( srcPath ), Casepath( destPath ) );
	}

	protected override void DeleteDirectoryImpl( UPath path, bool isRecursive )
	{
		base.DeleteDirectoryImpl( Casepath( path ), isRecursive );
	}

	//File API

	protected override void CopyFileImpl( UPath srcPath, UPath destPath, bool overwrite )
	{
		base.CopyFileImpl( Casepath( srcPath ), Casepath( destPath ), overwrite );
	}

	protected override void ReplaceFileImpl( UPath srcPath, UPath destPath, UPath destBackupPath, bool ignoreMetadataErrors )
	{
		base.ReplaceFileImpl( Casepath( srcPath ), Casepath( destPath ), Casepath( destBackupPath ), ignoreMetadataErrors );
	}

	protected override long GetFileLengthImpl( UPath path )
	{
		return base.GetFileLengthImpl( Casepath( path ) );
	}

	protected override void MoveFileImpl( UPath srcPath, UPath destPath )
	{
		base.MoveFileImpl( Casepath( srcPath ), Casepath( destPath ) );
	}

	protected override void DeleteFileImpl( UPath path )
	{
		base.DeleteFileImpl( Casepath( path ) );
	}

	protected override Stream OpenFileImpl( UPath path, FileMode mode, FileAccess access, FileShare share = FileShare.None )
	{
		return base.OpenFileImpl( Casepath( path ), mode, access, share );
	}

	protected override FileAttributes GetAttributesImpl( UPath path )
	{
		return base.GetAttributesImpl( Casepath( path ) );
	}

	//Metadata API
	protected override void SetAttributesImpl( UPath path, FileAttributes attributes )
	{
		base.SetAttributesImpl( Casepath( path ), attributes );
	}

	protected override DateTime GetCreationTimeImpl( UPath path )
	{
		return base.GetCreationTimeImpl( Casepath( path ) );
	}

	protected override void SetCreationTimeImpl( UPath path, DateTime time )
	{
		base.SetCreationTimeImpl( Casepath( path ), time );
	}

	protected override DateTime GetLastAccessTimeImpl( UPath path )
	{
		return base.GetLastAccessTimeImpl( Casepath( path ) );
	}

	protected override void SetLastAccessTimeImpl( UPath path, DateTime time )
	{
		base.SetLastAccessTimeImpl( Casepath( path ), time );
	}

	protected override DateTime GetLastWriteTimeImpl( UPath path )
	{
		return base.GetLastWriteTimeImpl( Casepath( path ) );
	}
	protected override void SetLastWriteTimeImpl( UPath path, DateTime time )
	{
		base.SetLastWriteTimeImpl( Casepath( path ), time );
	}

	protected override void CreateSymbolicLinkImpl( UPath path, UPath pathToTarget )
	{
		base.CreateSymbolicLinkImpl( Casepath( path ), Casepath( pathToTarget ) );
	}

	protected override bool TryResolveLinkTargetImpl( UPath linkPath, out UPath resolvedPath )
	{
		return base.TryResolveLinkTargetImpl( Casepath( linkPath ), out resolvedPath );
	}

	//Search API

	protected override IEnumerable<FileSystemItem> EnumerateItemsImpl( UPath path, SearchOption searchOption, SearchPredicate searchPredicate )
	{
		return base.EnumerateItemsImpl( Casepath( path ), searchOption, searchPredicate );
	}

	protected override IEnumerable<UPath> EnumeratePathsImpl( UPath path, string searchPattern, SearchOption searchOption, SearchTarget searchTarget )
	{
		return base.EnumeratePathsImpl( Casepath( path ), searchPattern, searchOption, searchTarget );
	}


	//watch API

	protected override bool CanWatchImpl( UPath path )
	{
		return base.CanWatchImpl( Casepath( path ) );
	}

	protected override IFileSystemWatcher WatchImpl( UPath path )
	{
		return base.WatchImpl( Casepath( path ) );
	}

	protected override UPath ValidatePathImpl( UPath path, string name = "path" )
	{
		return base.ValidatePathImpl( Casepath( path ), name );
	}




}
