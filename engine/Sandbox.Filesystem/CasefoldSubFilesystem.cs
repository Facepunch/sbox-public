using Zio;
using Zio.FileSystems;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace Sandbox;


internal class CasefoldSubFileSystem : SubFileSystem
{
    //Is this overkill?
	public CasefoldSubFileSystem( IFileSystem fileSystem, UPath subPath, bool owned = true ) : base( fileSystem, fileSystem.ConvertPathFromInternal( FileSystemStringCaseHelpers.Casepath( fileSystem.ConvertPathToInternal( subPath ) ) ), owned )
	{
        
	}


	/// <summary>
	/// An adaptation of POSIX implementation of casepath in C# world.
	/// WARNING: Handles only absolute paths. Also assumes that it's run under Linux or MacOS.
	/// On Windows, this function passes through inPath.
	/// </summary>
	/// <param name="inPath">path as fed through the parameter</param>
	/// <returns>the same path with proper casing. This is just partially adjusted if only part of the path exists.</returns>
	internal  UPath Casepath( UPath inPath )
	{
		return Casepath_recursive(FallbackSafe,inPath);
	}

	private static UPath Casepath_recursive(IFileSystem fs,UPath inPath)
	{
		//TODO: We should dive into FallbackSafe until we hit the proper filesystem.
		if (fs is SubFileSystem sfs)
		{
			if(sfs.FallbackSafe is SubFileSystem sub_sfs)
			{
				return Casepath_recursive(sub_sfs,UPath.Combine(sfs.SubPath,inPath));
			}
			
			if (sfs.FallbackSafe is CasefoldPhysicalFileSystem)
			{
				return sfs.FallbackSafe.ConvertPathFromInternal( FileSystemStringCaseHelpers.Casepath( sfs.FallbackSafe.ConvertPathToInternal( inPath ) ) );
			}
		} else if (fs is Zio.FileSystems.AggregateFileSystem zafs)
		{
			var entry = zafs.FindFirstFileSystemEntry( inPath );
			
			if ( entry == null ) return inPath;

			return Casepath_recursive(entry?.FileSystem,inPath);
			//return entry?.FileSystem.ConvertPathToInternal( entry.Path );
			//throw new NotImplementedException("not yet implemented.");
		}
		return inPath;
	}

	protected override UPath ConvertPathFromDelegate( UPath path )
	{
		return base.ConvertPathFromDelegate( Casepath(path) );
	}

	protected override UPath ConvertPathToDelegate( UPath path )
	{
		return base.ConvertPathToDelegate( Casepath(path) );
	}



}