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

	// Links: whether the build looks for them, whether it rasterises an apron, because a tile around this one is not built with it,
	// the edge points of those tiles with the frames they are packed in, and whether the solid spans go
	// to the mesh, because a tile around is still to be built or holds its own.
	internal bool FindLinks, Apron, KeepSolid;
	internal (EdgePoint[] Points, FieldFrame Frame, int X, int Y)[] Around = [];

	// What the builder leaves for the mesh about links.
	internal LinkSet? Found;
	internal HeldField? Solid;

	// One job at a time also judges the links across tile borders of the tiles named here, on the mesh
	// as it was when the job was taken: the first job of a TakeJobs, or one that does nothing else when
	// no tile is dirty.
	internal SeamTask[]? Seams;
	internal MeshState? SeamState;
	internal bool SeamsOnly;
}
