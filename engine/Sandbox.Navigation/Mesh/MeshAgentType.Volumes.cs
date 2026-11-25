using System.Numerics;
using System.Runtime.InteropServices;
using Nav.Build;

namespace Nav;

internal sealed partial class MeshAgentType
{
	// Tiles whose padded box a volume's world box reaches. Empty when Max is below Min.
	readonly record struct TileRect( int MinX, int MinY, int MaxX, int MaxY )
	{
		public static readonly TileRect Nothing = new( 0, 0, -1, -1 );

		public bool Contains( int x, int y ) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
		public long Tiles => MaxX < MinX || MaxY < MinY ? 0 : Math.Min( (long)MaxX - MinX + 1, 1L << 30 ) * Math.Min( (long)MaxY - MinY + 1, 1L << 30 );
	}

	// Per volume id of the mesh: the tiles of this type the volume reaches.
	TileRect[] volumeRects = [];

	// The mesh placed volume id: the tiles of its old place, if any, and of its new place are marked dirty.
	internal void PlaceVolume( int id, in NavVolume volume, bool replaced )
	{
		if ( volumeRects.Length < Mesh.VolumeCapacity ) Array.Resize( ref volumeRects, Mesh.VolumeCapacity );
		if ( replaced ) MarkVolumeTiles( volumeRects[id] );
		volumeRects[id] = RectOf( volume );
		MarkVolumeTiles( volumeRects[id] );
	}

	internal void RemoveVolume( int id ) => MarkVolumeTiles( volumeRects[id] );

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
		var mesh = Mesh;
		hash = 0;
		int count = 0;
		for ( int id = 0; id < mesh.VolumeHigh; id++ )
		{
			if ( !mesh.VolumeUsed[id] || !volumeRects[id].Contains( tileX, tileY ) ) continue;
			count++;
			hash += mesh.VolumeHashes[id];
		}
		if ( count == 0 ) return [];

		var volumes = new NavVolume[count];
		count = 0;
		for ( int id = 0; id < mesh.VolumeHigh; id++ )
		{
			if ( mesh.VolumeUsed[id] && volumeRects[id].Contains( tileX, tileY ) ) volumes[count++] = mesh.VolumeDefs[id];
		}
		return volumes;
	}
}
