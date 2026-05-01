namespace Sandbox;

/// <summary>
/// A directory on a disk
/// </summary>
internal class LocalFileSystem : BaseFileSystem
{
	Zio.FileSystems.PhysicalFileSystem Physical { get; }

	internal LocalFileSystem( string rootFolder, bool makereadonly = false )
	{
		// on Linux we're going to have a case sensitive filesystem
		// instead of fucking everything up everywhere and relying on people to nail case
		// we wrap our highest filesystem and resolve it there
		if ( OperatingSystem.IsLinux() )
		{
			Physical = new CaseInsensitivePhysicalFileSystem();
		}
		// on sane operating systems with case insensitive filesystems
		// windows + macos do the normal path
		else
		{
			Physical = new Zio.FileSystems.PhysicalFileSystem();
		}

		var rootPath = Physical.ConvertPathFromInternal( rootFolder );
		system = new Zio.FileSystems.SubFileSystem( Physical, rootPath );

		if ( makereadonly )
		{
			system = new Zio.FileSystems.ReadOnlyFileSystem( system );
		}
	}

	internal override void Dispose()
	{
		base.Dispose();

		Physical?.Dispose();
	}
}
