using System.Numerics;
using System.Runtime.InteropServices;
using Nav.Build;

namespace Nav;

public sealed partial class NavMesh
{
	// Tiles whose padded box a volume's world box reaches. Empty when Max is below Min.
	readonly record struct TileRect( int MinX, int MinY, int MaxX, int MaxY )
	{
		public static readonly TileRect Nothing = new( 0, 0, -1, -1 );

		public bool Contains( int x, int y ) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
		public long Tiles => MaxX < MinX || MaxY < MinY ? 0 : Math.Min( (long)MaxX - MinX + 1, 1L << 30 ) * Math.Min( (long)MaxY - MinY + 1, 1L << 30 );
	}

	NavVolume[] volumeDefs = [];
	TileRect[] volumeRects = [];
	ulong[] volumeHashes = [];
	bool[] volumeUsed = [];
	int volumeHigh;

	/// <summary>
	/// Adds a volume and marks the tiles it reaches dirty. They rebuild from their kept field, or ask
	/// for geometry once when they have none. Returns the id, the lowest one that is free.
	/// Throws for an area id above NavFilter.MaxArea. On a loaded mesh, add the volumes of the scene
	/// before the first TakeJobs: a tile baked with volumes that are not there by then is rebuilt.
	/// </summary>
	public int AddVolume( in NavVolume volume )
	{
		CheckArea( volume );
		int id = 0;
		while ( id < volumeHigh && volumeUsed[id] ) id++;
		if ( id == volumeDefs.Length )
		{
			int capacity = Math.Max( 16, id * 2 );
			Array.Resize( ref volumeDefs, capacity );
			Array.Resize( ref volumeRects, capacity );
			Array.Resize( ref volumeHashes, capacity );
			Array.Resize( ref volumeUsed, capacity );
		}
		volumeHigh = Math.Max( volumeHigh, id + 1 );
		volumeUsed[id] = true;
		Place( id, volume );
		return id;
	}

	/// <summary>
	/// Replaces a volume. The tiles of the old and the new place are marked dirty. Throws for an id that
	/// is not in use and for an area id above NavFilter.MaxArea.
	/// </summary>
	public void SetVolume( int id, in NavVolume volume )
	{
		if ( (uint)id >= (uint)volumeHigh || !volumeUsed[id] ) throw new ArgumentException( "No such volume", nameof( id ) );
		CheckArea( volume );
		MarkVolumeTiles( volumeRects[id] );
		Place( id, volume );
	}

	public void RemoveVolume( int id )
	{
		if ( (uint)id >= (uint)volumeHigh || !volumeUsed[id] ) return;
		volumeUsed[id] = false;
		MarkVolumeTiles( volumeRects[id] );
	}

	// A filter has one bit per area id, and a bake refuses a polygon with a higher one.
	static void CheckArea( in NavVolume volume )
	{
		if ( volume.Area > NavFilter.MaxArea ) throw new ArgumentOutOfRangeException( nameof( volume ), $"Volume area {volume.Area} is above {NavFilter.MaxArea}" );
	}

	void Place( int id, in NavVolume volume )
	{
		volumeDefs[id] = volume;
		volumeHashes[id] = Hash( volume );
		volumeRects[id] = RectOf( volume );
		MarkVolumeTiles( volumeRects[id] );
	}

	TileRect RectOf( in NavVolume volume )
	{
		if ( !VolumeMarker.WorldBounds( volume, out var min, out var max ) ) return TileRect.Nothing;
		float border = BorderCells * settings.CellSize;
		return new TileRect( TileOf( min.X - border ), TileOf( min.Y - border ), TileOf( max.X + border ), TileOf( max.Y + border ) );
	}

	// Dirty without the need for geometry: every tile of the rectangle that has polygons, a record
	// or a build under way. A volume over anything else has nothing to mark.
	void MarkVolumeTiles( TileRect rect )
	{
		long tiles = rect.Tiles;
		if ( tiles == 0 ) return;

		var s = State;
		if ( tiles <= s.TileCount + records.Count + marks.Count )
		{
			for ( int y = rect.MinY; y <= rect.MaxY; y++ )
			{
				for ( int x = rect.MinX; x <= rect.MaxX; x++ )
				{
					if ( s.Find( x, y ) != null || records.ContainsKey( (x, y) ) || marks.ContainsKey( (x, y) ) ) MarkVolumeDirty( x, y );
				}
			}
			return;
		}

		// A rectangle larger than the world: walk what exists
		volumeTouched.Clear();
		foreach ( var page in s.Pages )
		{
			if ( page == null ) continue;
			foreach ( var tile in page )
			{
				if ( tile != null && rect.Contains( tile.X, tile.Y ) ) volumeTouched.Add( (tile.X, tile.Y) );
			}
		}
		foreach ( var (at, _) in records )
		{
			if ( rect.Contains( at.X, at.Y ) ) volumeTouched.Add( at );
		}
		foreach ( var at in marks.Keys )
		{
			if ( rect.Contains( at.X, at.Y ) ) volumeTouched.Add( at );
		}
		foreach ( var (x, y) in volumeTouched ) MarkVolumeDirty( x, y );
	}

	readonly List<(int X, int Y)> volumeTouched = new();

	void MarkVolumeDirty( int tileX, int tileY ) => MarkDirty( tileX, tileY, false );

	// The volumes a build of this tile has to mark, and a hash of the set that does not depend on their order.
	NavVolume[] VolumesOver( int tileX, int tileY, out ulong hash )
	{
		hash = 0;
		int count = 0;
		for ( int id = 0; id < volumeHigh; id++ )
		{
			if ( !volumeUsed[id] || !volumeRects[id].Contains( tileX, tileY ) ) continue;
			count++;
			hash += volumeHashes[id];
		}
		if ( count == 0 ) return [];

		var volumes = new NavVolume[count];
		count = 0;
		for ( int id = 0; id < volumeHigh; id++ )
		{
			if ( volumeUsed[id] && volumeRects[id].Contains( tileX, tileY ) ) volumes[count++] = volumeDefs[id];
		}
		return volumes;
	}

	// Over the fields of the volume, the same in every process because the hash is kept in bakes.
	// Never 0, which stands for no volumes.
	static ulong Hash( in NavVolume volume )
	{
		Span<byte> bytes = stackalloc byte[2 + 24 * 4];
		bytes[0] = (byte)volume.Shape;
		bytes[1] = volume.Area;
		var m = volume.LocalToWorld;
		ReadOnlySpan<float> fields =
		[
			m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44,
			volume.A.X, volume.A.Y, volume.A.Z, volume.B.X, volume.B.Y, volume.B.Z, volume.Radius, volume.Margin
		];
		MemoryMarshal.AsBytes( fields ).CopyTo( bytes.Slice( 2 ) );
		return Fnv.Hash( bytes );
	}
}
