using Borealis.Source2;
using Borealis.Source2.Models;
using N3 = System.Numerics.Vector3;

namespace Sandbox;

public partial class Model
{
	// A model made in code's trace mesh (ModelBuilder.AddTraceMesh), kept until the first trace turns it into triangles
	Vector3[] traceVertices;
	int[] traceIndices;

	MapTrace.ModelTriangles traceTriangles;
	volatile bool traceTrianglesRead;
	readonly System.Threading.Lock traceLock = new();

	/// <summary>
	/// Compiled files for traces on Source 2's device: the game folder's content, then the process's mounted files.
	/// </summary>
	static readonly Lazy<ContentFiles> mountedContent = new( () =>
	{
		var files = ContentFiles.FindGame();
		files.Resolve = BorealisGpuRenderBackend.MountedFile;
		return files;
	} );

	/// <summary>
	/// Keep a model made in code's trace mesh for its first trace. Native isn't given it: it would build and keep a ray trace
	/// object of its own for every model made in code.
	/// </summary>
	internal void SetTraceMesh( List<Vector3> vertices, List<int> indices )
	{
		lock ( traceLock )
		{
			var any = vertices.Count >= 3 && indices.Count >= 3;
			traceVertices = any ? vertices.ToArray() : null;
			traceIndices = any ? indices.ToArray() : null;
			traceTriangles = null;
			traceTrianglesRead = false;
		}
	}

	/// <summary>
	/// A file model's triangles are read again on its next trace after a reload, as its geometry may have changed. A model made
	/// in code keeps its own.
	/// </summary>
	void ResetTraceTriangles()
	{
		if ( IsProcedural ) return;
		lock ( traceLock )
		{
			traceTriangles = null;
			traceTrianglesRead = false;
		}
	}

	/// <summary>
	/// The model's LOD 0 triangles in a hierarchy, for traces against its render mesh (what native's <c>IModel::MeshTrace</c>
	/// traced). Built the first time a trace reaches the model's bounds, once even when several threads trace at once (a DDGI
	/// bake traces in parallel), the others waiting for it. Null when the model has nothing to trace, or when they can't be
	/// read on this thread yet (see <see cref="ReadTraceTriangles"/>): the next trace tries again.
	/// </summary>
	internal MapTrace.ModelTriangles TraceTriangles()
	{
		if ( traceTrianglesRead ) return traceTriangles;
		lock ( traceLock )
		{
			if ( traceTrianglesRead ) return traceTriangles;
			if ( !ReadTraceTriangles( out var triangles ) ) return null;
			traceTriangles = triangles;
			traceVertices = null;
			traceIndices = null;
			traceTrianglesRead = true;
			return triangles;
		}
	}

	/// <summary>
	/// Read the triangles to trace: a model made in code's trace mesh, else a file model's compiled file (in the editor only,
	/// where native built its trace data), else the geometry read back from the render system. False when they can't be read on
	/// this thread yet; true with null when the model has nothing to trace.
	/// </summary>
	bool ReadTraceTriangles( out MapTrace.ModelTriangles triangles )
	{
		triangles = null;
		if ( traceVertices is not null )
		{
			triangles = MapTrace.ModelTriangles.FromIndexed( traceVertices, traceIndices );
			return true;
		}

		// Native gave neither run-time meshes nor, outside the editor, file models a trace mesh
		if ( IsProcedural || !Application.IsEditor ) return true;
		var path = ResourcePath;
		if ( string.IsNullOrEmpty( path ) ) return true;

		ModelFile file = null;
		try
		{
			file = ReadTraceFile( path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Can't trace {path}: {e.Message}" );
		}

		if ( file is not null )
		{
			triangles = MapTrace.ModelTriangles.Read( file );
			return true;
		}

		// Native's draw call read goes through a render context: the main thread's
		if ( !ThreadSafe.IsMainThread ) return false;
		triangles = ReadBackTraceTriangles();
		return true;
	}

	/// <summary>
	/// A file model's compiled file, as <see cref="SceneMap"/> reads a map's models: Borealis.Gpu's cached one, else from the
	/// game's and the mounted files. Pure managed, so safe on worker threads.
	/// </summary>
	static ModelFile ReadTraceFile( string path )
	{
		if ( RenderBackend.Default is BorealisGpuRenderBackend gpu ) return gpu.Services.ReadModel( path ).GetAwaiter().GetResult();
		var content = mountedContent.Value;
		return content.TryRead( path ) is { } data ? ModelFile.Read( data, content.TryRead ) : null;
	}

	/// <summary>
	/// LOD 0's triangles read back from the render system's buffers, for a file model with no compiled file to read.
	/// </summary>
	MapTrace.ModelTriangles ReadBackTraceTriangles()
	{
		var corners = new List<N3>();
		var materialOf = new List<int>();
		var materials = new List<string>();
		var meshes = MeshInfo.Meshes;
		for ( int m = 0; m < meshes.Length; m++ )
		{
			if ( (meshes[m].LodMask & 1) == 0 ) continue;
			for ( int d = 0; d < meshes[m].DrawCalls.Length; d++ )
			{
				if ( !TryReadDrawCall( m, d, out var vertices, out var indices, out _ ) ) continue;
				var material = materials.Count;
				materials.Add( meshes[m].DrawCalls[d].Material?.ResourcePath );
				for ( int i = 0; i + 2 < indices.Length; i += 3 )
				{
					for ( int k = 0; k < 3; k++ )
					{
						var p = vertices[indices[i + k]].Position;
						corners.Add( new N3( p.x, p.y, p.z ) );
					}

					materialOf.Add( material );
				}
			}
		}

		return MapTrace.ModelTriangles.FromCorners( corners.ToArray(), materialOf.ToArray(), materials.ToArray() );
	}
}
