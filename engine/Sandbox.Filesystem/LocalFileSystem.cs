namespace Sandbox;

/// <summary>
/// A directory on a disk
/// </summary>
internal class LocalFileSystem : BaseFileSystem
{
	Zio.FileSystems.PhysicalFileSystem Physical { get; }

	internal LocalFileSystem(string rootFolder, bool makereadonly = false)
	{
		Physical = new Zio.FileSystems.PhysicalFileSystem();

<<<<<<< HEAD
		var rootPath = Physical.ConvertPathFromInternal(rootFolder.ToLowerInvariant());
		if (!OperatingSystem.IsWindows()) rootPath = Physical.ConvertPathFromInternal(rootFolder);
		system = new Zio.FileSystems.SubFileSystem(Physical, rootPath);
=======
		var rootPath = Physical.ConvertPathFromInternal(
			OperatingSystem.IsWindows() ? rootFolder.ToLowerInvariant() : rootFolder );
		system = new Zio.FileSystems.SubFileSystem( Physical, rootPath );
>>>>>>> 73728b8cc77f9bcef4ce8d4d456ac637b7507b94

		if (makereadonly)
		{
			system = new Zio.FileSystems.ReadOnlyFileSystem(system);
		}
	}

	internal override void Dispose()
	{
		base.Dispose();

		Physical?.Dispose();
	}
}
