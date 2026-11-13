using System.Collections.Concurrent;
using System.Numerics;
using Nav.Build;

namespace Nav;

public sealed partial class NavMesh
{
	// What the owner keeps about a tile for rebuilds. A tile has a record while it has a kept field or
	// stands for a set of volumes. Field is set the first time the tile is built from triangles with a
	// volume over it, and stays until its geometry changes with no volume over it or the tile is removed.
	sealed class TileRecord
	{
		public CachedField? Field;

		// What the areas of the tile's spans hash to, and which volumes the tile, with or without
		// polygons, was built with. 0 for no volumes.
		public ulong AreaHash, VolumeHash;
	}

	// Written by the owner only. Save reads a tile's volume hash and belongs on the owner thread as
	// well; the type is there so that a save from another thread reads a stale hash at worst.
	readonly ConcurrentDictionary<(int X, int Y), TileRecord> records = new();

	// Set by a load that brought tiles built with volumes, until TakeJobs has compared them with the
	// volumes there are now.
	bool loadedUnchecked;

	/// <summary>
	/// The whole level in one call: sorts the triangles into tiles, builds every tile they reach on this
	/// thread and commits in batches. Nothing of the input is kept. A triangle is ground when the cross
	/// product of v1 - v0 and v2 - v0 points up, within the slope limit. A triangle with an index out of
	/// range or a coordinate that is not finite is left out and counted as rejected. Tiles that were
	/// already dirty are built too, and one of those that no triangle reaches any more is removed.
	/// Returns the number of failed tiles; failures, when given, gets each of them with its reason.
	/// </summary>
	public int Build( NavBuilder builder, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, List<(int X, int Y, string Error)>? failures = null )
	{
		var bins = new TriangleBins( settings, vertices, indices );
		Interlocked.Add( ref TrianglesRejected, bins.Invalid );
		foreach ( var (x, y) in bins.Tiles ) MarkDirty( x, y );

		var jobs = new TileJob[64];
		int failed = 0;
		while ( true )
		{
			int count = TakeJobs( jobs );
			if ( count == 0 ) break;
			for ( int i = 0; i < count; i++ ) builder.Build( jobs[i], vertices, bins.Indices( jobs[i].X, jobs[i].Y ).Span );
			failed += Commit( jobs.AsSpan( 0, count ) );
			for ( int i = 0; i < count && failures != null; i++ )
			{
				if ( jobs[i].Status == TileJobStatus.Failed ) failures.Add( (jobs[i].X, jobs[i].Y, jobs[i].Error ?? "") );
			}
		}
		return failed + Commit();
	}

	// The volume set a tile stands for, as kept in a bake.
	internal ulong VolumeHashOf( int tileX, int tileY ) => records.TryGetValue( (tileX, tileY), out var record ) ? record.VolumeHash : 0;

	// Called by a load: the volume set each tile was baked with. The fields are not in a bake.
	internal void SetVolumeHashes( ReadOnlySpan<Tile> tiles, ReadOnlySpan<ulong> hashes )
	{
		for ( int i = 0; i < tiles.Length; i++ )
		{
			if ( hashes[i] == 0 ) continue;
			records[(tiles[i].X, tiles[i].Y)] = new TileRecord { VolumeHash = hashes[i] };
			loadedUnchecked = true;
		}
	}

	// Called at the start of TakeJobs. A loaded tile that was baked with volumes is marked dirty once:
	// when the volumes over it now are the ones it was baked with, PrepareJob drops the mark, otherwise
	// the tile is rebuilt, so a volume that left the scene since the bake does not leave its mark behind.
	void CheckLoadedTiles()
	{
		if ( !loadedUnchecked ) return;
		loadedUnchecked = false;
		foreach ( var (at, record) in records )
		{
			if ( record.VolumeHash != 0 ) MarkVolumeDirty( at.X, at.Y );
		}
	}

	// Called for every job TakeJobs is about to hand out. Gives it the volumes over its tile and, when
	// only volumes changed, the kept field to rebuild from. A tile whose volumes are the ones it was
	// built with, or where there is nothing to mark, gets no job.
	partial void PrepareJob( TileJob job, bool geometryForced, ref bool issue )
	{
		job.Volumes = VolumesOver( job.X, job.Y, out job.VolumeHash );
		job.KeepField = KeepFields && job.Volumes.Length > 0;
		if ( geometryForced ) return;

		records.TryGetValue( (job.X, job.Y), out var record );
		if ( (record == null && State.Find( job.X, job.Y ) == null) || (record?.VolumeHash ?? 0) == job.VolumeHash )
		{
			issue = false;
			return;
		}

		// Without a field the first volume over a tile has to ask for the triangles once
		if ( record?.Field == null ) return;
		job.NeedsGeometry = false;
		job.Cached = record.Field;
		job.LastAreaHash = record.AreaHash;
	}

	// Called at commit for a job that built a tile or found it unchanged.
	partial void JobPublished( TileJob job )
	{
		var at = (job.X, job.Y);
		records.TryGetValue( at, out var record );
		if ( job.Cached != null )
		{
			// From the kept field: other areas, or other volumes that mark the same. A tile removed since has no record.
			if ( record == null ) return;
			if ( job.Status == TileJobStatus.Built ) record.AreaHash = job.AreaHash;
			record.VolumeHash = job.VolumeHash;
			return;
		}

		Interlocked.Add( ref TrianglesUsed, job.TrianglesUsed );
		Interlocked.Add( ref TrianglesRejected, job.TrianglesRejected );

		// New geometry: the old field is stale whether or not this build left a new one. Where nothing
		// was walkable before the volumes, no volume change can bring anything back, and nothing is kept.
		Interlocked.Add( ref CacheBytes, (job.Field?.Bytes ?? 0) - (record?.Field?.Bytes ?? 0) );
		if ( job.Field == null && (job.VolumeHash == 0 || job.AreaHash == 0) )
		{
			if ( record != null ) records.TryRemove( at, out _ );
			return;
		}
		if ( record == null ) records[at] = record = new TileRecord();
		record.Field = job.Field;
		record.AreaHash = job.AreaHash;
		record.VolumeHash = job.VolumeHash;
	}

	partial void TileRemoved( int tileX, int tileY )
	{
		if ( records.TryRemove( (tileX, tileY), out var record ) && record.Field != null ) Interlocked.Add( ref CacheBytes, -record.Field.Bytes );
	}
}
