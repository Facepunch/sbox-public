using System.Numerics;
using Nav.Build;

namespace Nav;

internal sealed partial class MeshAgentType
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

	// Owner thread.
	readonly Dictionary<(int X, int Y), TileRecord> records = new();

	// The volume hash of every record that has one, in the order of y, then x: what Save writes beside the
	// tiles. The owner publishes it after the state that holds the tiles it describes. hashesChanged: a
	// hash of the records is not in it yet.
	(int Y, int X, ulong Hash)[] tileHashes = [];
	bool hashesChanged;

	internal (int Y, int X, ulong Hash)[] TileHashes => Volatile.Read( ref tileHashes );

	// Set by a load that brought tiles built with volumes, until TakeJobs has compared them with the
	// volumes there are now.
	bool loadedUnchecked;

	// The volume set a tile stands for. Owner thread.
	internal ulong VolumeHashOf( int tileX, int tileY ) => records.TryGetValue( (tileX, tileY), out var record ) ? record.VolumeHash : 0;

	// Called by a load: the volume set each tile was baked with. The fields are not in a bake.
	internal void SetVolumeHashes( ReadOnlySpan<Tile> tiles, ReadOnlySpan<ulong> hashes )
	{
		for ( int i = 0; i < tiles.Length; i++ )
		{
			if ( hashes[i] == 0 ) continue;
			records[(tiles[i].X, tiles[i].Y)] = new TileRecord { VolumeHash = hashes[i] };
			loadedUnchecked = hashesChanged = true;
		}
		PublishVolumeHashes();
	}

	void SetVolumeHash( TileRecord record, ulong hash )
	{
		hashesChanged |= record.VolumeHash != hash;
		record.VolumeHash = hash;
	}

	// Called at the end of a commit, once the state with the tiles is out, and by a load.
	partial void PublishVolumeHashes()
	{
		if ( !hashesChanged ) return;
		hashesChanged = false;
		int count = 0;
		foreach ( var (_, record) in records )
		{
			if ( record.VolumeHash != 0 ) count++;
		}
		var published = new (int Y, int X, ulong Hash)[count];
		count = 0;
		foreach ( var (at, record) in records )
		{
			if ( record.VolumeHash != 0 ) published[count++] = (at.Y, at.X, record.VolumeHash);
		}
		Array.Sort( published );
		Volatile.Write( ref tileHashes, published );
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
		if ( geometryForced )
		{
			FindLinks( job );
			return;
		}

		records.TryGetValue( (job.X, job.Y), out var record );
		if ( (record == null && State.Find( job.X, job.Y ) == null) || (record?.VolumeHash ?? 0) == job.VolumeHash )
		{
			issue = false;
			return;
		}

		// Without a field the first volume over a tile has to ask for the triangles once. They are the
		// ones the tile was built from: what was found about its links then still stands.
		if ( record?.Field == null )
		{
			if ( !tileLinks.ContainsKey( (job.X, job.Y) ) ) FindLinks( job );
			return;
		}
		job.NeedsGeometry = false;
		job.Cached = record.Field;
		job.LastAreaHash = record.AreaHash;
	}

	void FindLinks( TileJob job )
	{
		if ( !links.Enabled ) return;
		job.FindLinks = true;
		PrepareLinks( job );
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
			SetVolumeHash( record, job.VolumeHash );
			return;
		}

		Interlocked.Add( ref TrianglesUsed, job.TrianglesUsed );
		Interlocked.Add( ref TrianglesRejected, job.TrianglesRejected );

		// New geometry: the old field is stale whether or not this build left a new one. Where nothing
		// was walkable before the volumes, no volume change can bring anything back, and nothing is kept.
		Interlocked.Add( ref CacheBytes, (job.Field?.Bytes ?? 0) - (record?.Field?.Bytes ?? 0) );
		if ( job.Field == null && (job.VolumeHash == 0 || job.AreaHash == 0) )
		{
			if ( record == null ) return;
			hashesChanged |= record.VolumeHash != 0;
			records.Remove( at );
			return;
		}
		if ( record == null ) records[at] = record = new TileRecord();
		record.Field = job.Field;
		record.AreaHash = job.AreaHash;
		SetVolumeHash( record, job.VolumeHash );
	}

	partial void TileRemoved( int tileX, int tileY )
	{
		if ( !records.Remove( (tileX, tileY), out var record ) ) return;
		hashesChanged |= record.VolumeHash != 0;
		if ( record.Field != null ) Interlocked.Add( ref CacheBytes, -record.Field.Bytes );
	}
}
