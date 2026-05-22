using Zio;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// A SubFileSystem that resolves the sub-path to its real on-disk casing before
/// mounting. Without this, SubFileSystem stores the input casing as SubPath and
/// its Ordinal ConvertPathFromDelegate check fails when the inner
/// CaseInsensitivePhysicalFileSystem returns paths with actual on-disk casing.
/// </summary>
internal sealed class CaseInsensitiveSubFileSystem : SubFileSystem
{
	internal CaseInsensitiveSubFileSystem( IFileSystem fileSystem, UPath subPath, bool owned = false )
		: base( fileSystem, ResolveCasedSubPath( fileSystem, subPath ), owned )
	{
	}

	private static UPath ResolveCasedSubPath( IFileSystem fileSystem, UPath path )
	{
		if ( fileSystem is SubFileSystem fs )
		{
			var internalPath = fs.ConvertPathToInternal( path );
			return fs.ConvertPathFromInternal( internalPath );
		}

		return path;
	}
}
