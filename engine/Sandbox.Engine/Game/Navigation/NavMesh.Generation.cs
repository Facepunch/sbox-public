using Nav;
using System.Buffers;
using System.Collections.Concurrent;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	private readonly record struct TileBuild( TileJob Job, Task Task, NVector3[] Vertices, int[] Indices );

	// A library mesh with the areas and links it was given, by component, and its builds under way
	private sealed class MeshWork( Nav.NavMesh mesh )
	{
		public readonly Nav.NavMesh Mesh = mesh;
		public readonly Dictionary<NavMeshArea, (int Id, NavVolume Volume)> Areas = new();
		public readonly Dictionary<NavMeshLink, (int Id, NavLink Link)> Links = new();
		public NavMeshLink[] LinkComponents = [];
		public readonly List<TileBuild> Builds = new();

		// The world an awaited call named for a tile, until that tile is given its geometry
		public readonly Dictionary<(int X, int Y), PhysicsWorld> Worlds = new();

		// For a mesh built aside: the tiles marked before its first update, less the ones built since
		public readonly HashSet<(int X, int Y)> FirstPass = new();
		public bool Started;

		// Its settings changed again: it takes no more tiles and never takes over
		public bool Abandoned;
	}

	// The mesh in use, and the one being built aside. Tile calls go to the second while there is one.
	private MeshWork live, next;

	private MeshWork Target => next ?? live;

	private readonly TileJob[] jobs = new TileJob[MaxBuilds];
	private int failedBuilds;

	private static readonly ConcurrentQueue<NavBuilder> builders = new();

	private static int MaxBuilds => Math.Max( 2, Environment.ProcessorCount - 1 );

	/// <summary>
	/// Generates or regenerates the navmesh tile at the given world position.
	/// This function is thread safe but can only be called from the main thread.
	/// </summary>
	///
	/// <remarks>
	/// While most of the generation happens in parallel, this function also requires some time on the main thread.
	/// If you need to update many tiles, consider spreading the updates accross multiple frames.
	/// </remarks>
	public async Task GenerateTile( PhysicsWorld world, Vector3 worldPosition )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled ) return;

		var waiting = new List<(int X, int Y)>();
		MarkDirty( new RectInt( WorldPositionToTilePosition( worldPosition ) ), waiting, world: world );
		await BuildTiles( world, waiting );
	}

	/// <summary>
	/// Generates or regenerates the navmesh tiles overlapping with the given bounds.
	/// This function is thread safe but can only be called from the main thread.
	/// </summary>
	///
	/// <remarks>
	/// While most of the generation happens in parallel, this function also requires some time on the main thread.
	/// If you need to update many tiles, consider spreading the updates accross multiple frames.
	/// </remarks>
	public async Task GenerateTiles( PhysicsWorld world, BBox bounds )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled ) return;

		var waiting = new List<(int X, int Y)>();
		MarkDirty( CalculateMinMaxTileCoords( bounds ), waiting, world: world );
		await BuildTiles( world, waiting );
	}

	/// <summary>
	/// Removes the navmesh tile at the given world position.
	/// </summary>
	public void UnloadTile( Vector3 worldPosition )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled ) return;

		var tiles = new RectInt( WorldPositionToTilePosition( worldPosition ) );
		Unload( live, tiles );
		if ( next is not null ) Unload( next, tiles );
	}

	/// <summary>
	/// Removes all navmesh tiles overlapping with the given bounds.
	/// </summary>
	public void UnloadTiles( BBox bounds )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled ) return;

		var tiles = CalculateMinMaxTileCoords( bounds );
		Unload( live, tiles );
		if ( next is not null ) Unload( next, tiles );
	}

	private static void Unload( MeshWork work, RectInt tiles )
	{
		for ( int x = tiles.Left; x <= tiles.Right; x++ )
		{
			for ( int y = tiles.Top; y <= tiles.Bottom; y++ )
			{
				work.Mesh.RemoveTile( x, y );
				work.FirstPass.Remove( (x, y) );
				work.Worlds.Remove( (x, y) );
			}
		}
		work.Mesh.Commit();
	}

	/// <summary>
	/// Queues the navmesh tile at the given world position for incremental generation
	/// over subsequent frames. Fire-and-forget alternative to <see cref="GenerateTile"/>.
	/// </summary>
	public void RequestTileGeneration( Vector3 worldPosition )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled ) return;

		MarkDirty( new RectInt( WorldPositionToTilePosition( worldPosition ) ) );
	}

	/// <summary>
	/// Queues all navmesh tiles overlapping with the given bounds for incremental generation
	/// over subsequent frames. Fire-and-forget alternative to <see cref="GenerateTiles"/>.
	/// </summary>
	public void RequestTilesGeneration( BBox bounds )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !IsEnabled ) return;

		MarkDirty( CalculateMinMaxTileCoords( bounds ) );
	}

	internal Vector2Int WorldPositionToTilePosition( Vector3 worldPosition )
	{
		return new Vector2Int( (int)MathF.Floor( worldPosition.x / TileSizeXYWorldSpace ), (int)MathF.Floor( worldPosition.y / TileSizeXYWorldSpace ) );
	}

	internal Vector3 TilePositionToWorldPosition( Vector2Int tilePosition )
	{
		return new Vector3( (tilePosition.x + 0.5f) * TileSizeXYWorldSpace, (tilePosition.y + 0.5f) * TileSizeXYWorldSpace, 0 );
	}

	// Limits a box while there are no bounds: 512 tiles each way
	private static readonly BBox worldLimit = BBox.FromPositionAndSize( 0, 512 * TileSizeXYWorldSpace - CellSize );

	internal RectInt CalculateMinMaxTileCoords( BBox bounds )
	{
		var limit = CustomBounds || boundsKnown ? Bounds : worldLimit;
		var coordMin = WorldPositionToTilePosition( Vector3.Max( bounds.Mins, limit.Mins ) );
		var coordMax = WorldPositionToTilePosition( Vector3.Min( bounds.Maxs, limit.Maxs ) );

		return new RectInt( coordMin, coordMax - coordMin );
	}

	// Rebuilds these tiles from geometry, apart from the ones a bake covers. An awaiting caller passes the list and its world.
	private void MarkDirty( RectInt tiles, List<(int X, int Y)> waiting = null, RectInt? baked = null, PhysicsWorld world = null )
	{
		var work = Target;
		for ( int x = tiles.Left; x <= tiles.Right; x++ )
		{
			for ( int y = tiles.Top; y <= tiles.Bottom; y++ )
			{
				if ( baked is RectInt done && x >= done.Left && x <= done.Right && y >= done.Top && y <= done.Bottom ) continue;
				work.Mesh.MarkDirty( x, y );
				waiting?.Add( (x, y) );
				if ( world is not null ) work.Worlds[(x, y)] = world;
				if ( !work.Started ) work.FirstPass.Add( (x, y) );
			}
		}
	}

	// One round of the tile flow. A mesh built aside takes over once every tile it started with has been built.
	internal void UpdateCache( PhysicsWorld world )
	{
		ThreadSafe.AssertIsMainThread();

		if ( live is null ) return;

		CurrentCosts();
		UpdateTiles( live, world );
		if ( next is not null )
		{
			UpdateTiles( next, world );
			if ( next.FirstPass.Count == 0 && !next.Abandoned )
			{
				live = next;
				next = null;
				bakedTiles = null;
				Publish();
			}
		}

		ReadLinkEnds();
	}

	private void UpdateTiles( MeshWork work, PhysicsWorld world )
	{
		work.Started = true;
		UpdateAreas( work );
		UpdateLinks( work );

		int done = 0;
		for ( int i = work.Builds.Count - 1; i >= 0; i-- )
		{
			var build = work.Builds[i];
			if ( !build.Task.IsCompleted ) continue;
			work.Builds.RemoveAt( i );
			if ( build.Vertices is not null ) ArrayPool<NVector3>.Shared.Return( build.Vertices );
			if ( build.Indices is not null ) ArrayPool<int>.Shared.Return( build.Indices );

			if ( build.Task.IsFaulted )
			{
				// The tile goes, or it would be built forever
				Log.Warning( build.Task.Exception, $"Navmesh: Exception while generating tile {build.Job.X},{build.Job.Y}" );
				work.Mesh.RemoveTile( build.Job.X, build.Job.Y );
				failedBuilds++;
			}
			jobs[done++] = build.Job;
		}

		// Also publishes link changes and removed tiles
		failedBuilds += work.Mesh.Commit( jobs.AsSpan( 0, done ) );

		for ( int i = 0; i < done; i++ )
		{
			var job = jobs[i];
			// A job that came back asking for geometry built nothing, and its tile gets another
			if ( job.Status != TileJobStatus.NeedsGeometry ) work.FirstPass.Remove( (job.X, job.Y) );
			if ( job.Status == TileJobStatus.Failed ) Log.Warning( $"Navmesh: tile {job.X},{job.Y} was not built: {job.Error}" );
		}

		int taken = work.Abandoned ? 0 : work.Mesh.TakeJobs( jobs.AsSpan( 0, Math.Max( 0, MaxBuilds - work.Builds.Count ) ) );
		for ( int i = 0; i < taken; i++ )
		{
			var job = jobs[i];
			if ( CustomBounds )
			{
				job.MinZ = Bounds.Mins.z;
				job.MaxZ = Bounds.Maxs.z;
			}

			NVector3[] vertices = null;
			int[] indices = null;
			if ( !work.Worlds.Remove( (job.X, job.Y), out var tileWorld ) ) tileWorld = world;
			if ( job.NeedsGeometry ) CollectGeometry( tileWorld, job, out vertices, out indices );

			var task = Task.Run( () =>
			{
				if ( !builders.TryDequeue( out var builder ) ) builder = new NavBuilder();
				try { builder.Build( job ); }
				finally { builders.Enqueue( builder ); }
			} );
			work.Builds.Add( new TileBuild( job, task, vertices, indices ) );
		}
		Array.Clear( jobs );
	}

	// Runs the tile flow until these tiles are built and in the mesh in use
	private async Task BuildTiles( PhysicsWorld world, List<(int X, int Y)> waiting )
	{
		var work = Target;
		int total = waiting.Count;
		while ( work == live || work == next )
		{
			UpdateCache( world );

			for ( int i = waiting.Count - 1; i >= 0; i-- )
			{
				if ( !work.Mesh.IsPending( waiting[i].X, waiting[i].Y ) ) waiting.RemoveAt( i );
			}
			if ( total > 0 && LoadingScreen.IsVisible ) LoadProgress.Report( (double)(total - waiting.Count) / total );
			if ( (waiting.Count == 0 && work == live) || work.Builds.Count == 0 ) return;

			await Task.WhenAny( work.Builds.Select( build => build.Task ) );
			await GameTask.MainThread();
		}
	}

	// For the debug draw and tests
	private readonly List<NVector3> polyVertices = new();
	private readonly List<int> polyVertexCounts = new();
	private readonly List<byte> polyAreas = new();

	internal int GetPolyCount( Vector2Int tilePosition )
	{
		ReadPolygons( tilePosition );
		return polyVertexCounts.Count;
	}

	internal IEnumerable<Vector3> GetPolyVerts( Vector2Int tilePosition, int polyIndex )
	{
		ReadPolygons( tilePosition );
		if ( polyIndex >= polyVertexCounts.Count ) return [];
		int first = polyVertexCounts.Take( polyIndex ).Sum();
		return polyVertices.Skip( first ).Take( polyVertexCounts[polyIndex] ).Select( vertex => (Vector3)vertex ).ToArray();
	}

	private void ReadPolygons( Vector2Int tilePosition )
	{
		polyVertices.Clear();
		polyVertexCounts.Clear();
		polyAreas.Clear();
		mesh?.GetPolygons( tilePosition.x, tilePosition.y, polyVertices, polyVertexCounts, polyAreas );
	}
}
