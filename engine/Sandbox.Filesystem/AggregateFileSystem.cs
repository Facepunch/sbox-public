namespace Sandbox;

/// <summary>
/// A filesystem that merges a bunch of other filesystems. This is read only.
/// </summary>
internal class AggregateFileSystem : BaseFileSystem
{
	internal AggregateFileSystem() : base( new Zio.FileSystems.AggregateFileSystem( false ) )
	{

	}

	public void UnMountAll()
	{
		lock ( MountLock )
		{
			(system as Zio.FileSystems.AggregateFileSystem)?.ClearFileSystems();
		}
	}
}
