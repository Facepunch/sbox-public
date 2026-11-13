using Nav.Build;

namespace Nav;

public sealed partial class TileJob
{
	// What the mesh hands a job when it is taken: copies, so the owner may go on changing volumes.
	internal NavVolume[] Volumes = [];
	internal ulong VolumeHash;

	// The field to rebuild from when no geometry is needed, and the areas the tile's polygons stand for.
	internal CachedField? Cached;
	internal ulong LastAreaHash;

	// A build from triangles hands its field back for the rebuild cache.
	internal bool KeepField;

	// What the builder leaves for the mesh.
	internal CachedField? Field;
	internal ulong AreaHash;
	internal int TrianglesUsed, TrianglesRejected;
}
