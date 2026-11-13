using System.Numerics;

namespace Nav;

public enum TileJobStatus { Pending, Built, Unchanged, Failed, NeedsGeometry }

/// <summary>
/// Everything needed to build one tile. Taken from the mesh, built on any thread, committed back.
/// The job holds copies of what it needs from the mesh, so the owner may go on changing the mesh
/// while it is built: the result is committed as it is and the tile gets another job for the rest.
/// When NeedsGeometry is set the engine collects the triangles inside the job's box, from whatever it
/// keeps them in, and hands them over with SetGeometry before the job is built.
/// </summary>
public sealed partial class TileJob
{
	// Jobs come from NavMesh.TakeJobs
	internal TileJob()
	{
	}

	public int X { get; internal set; }
	public int Y { get; internal set; }

	/// <summary>
	/// Call SetGeometry before building. False for a job that rebuilds from what the mesh kept.
	/// </summary>
	public bool NeedsGeometry { get; internal set; }

	/// <summary>
	/// World box to collect triangles from, border included.
	/// </summary>
	public Vector3 BoundsMin { get; internal set; }
	public Vector3 BoundsMax { get; internal set; }

	/// <summary>
	/// Triangles entirely below MinZ or above MaxZ are left out.
	/// </summary>
	public float MinZ = float.NegativeInfinity, MaxZ = float.PositiveInfinity;

	public TileJobStatus Status { get => (TileJobStatus)Volatile.Read( ref status ); internal set => Volatile.Write( ref status, (int)value ); }
	int status;

	/// <summary>
	/// Set when the job failed.
	/// </summary>
	public string? Error { get; internal set; }

	internal NavMesh? Mesh;

	// The triangles changed since the tile was last built, so what the mesh kept cannot serve.
	internal bool GeometryChanged;

	// The built tile, null for an empty one. Read by Commit when Status is Built.
	internal Tile? Built;

	internal ReadOnlyMemory<Vector3> Vertices;
	internal ReadOnlyMemory<int> Indices;
	internal bool HasGeometry;

	/// <summary>
	/// The triangles of the tile, three indices each. A triangle is ground when the cross product of
	/// v1 - v0 and v2 - v0 points up, within the slope limit, and an obstacle either way. Triangles
	/// outside the job's box do no harm. The memory is read while the job is built and not kept.
	/// An empty set says there is nothing here: the job builds to an empty tile, and committing it
	/// removes the tile. A job that needs geometry and is built without this call fails.
	/// </summary>
	public void SetGeometry( ReadOnlyMemory<Vector3> vertices, ReadOnlyMemory<int> indices )
	{
		Vertices = vertices;
		Indices = indices;
		HasGeometry = true;
	}

	internal void Complete( TileJobStatus status, Tile? built = null, string? error = null )
	{
		Built = built;
		Error = error;
		Status = status;
	}
}
