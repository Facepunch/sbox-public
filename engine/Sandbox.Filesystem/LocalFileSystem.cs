namespace Sandbox;

/// <summary>
/// A directory on a disk
/// </summary>
internal class LocalFileSystem : BaseFileSystem
{
	Zio.FileSystems.PhysicalFileSystem Physical { get; }

	internal LocalFileSystem( string rootFolder, bool makereadonly = false )
	{
		Physical = new Zio.FileSystems.PhysicalFileSystem();

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
