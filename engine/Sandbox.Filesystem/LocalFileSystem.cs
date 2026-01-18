namespace Sandbox;

/// <summary>
/// A directory on a disk
/// </summary>
internal class LocalFileSystem : BaseFileSystem
{
	CasefoldPhysicalFileSystem Physical { get; }

	internal LocalFileSystem( string rootFolder, bool makereadonly = false )
	{
		Physical = new CasefoldPhysicalFileSystem();

		var rootPath = Physical.ConvertPathFromInternal( rootFolder.ToLowerInvariant() );
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
