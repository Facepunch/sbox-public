using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// A fixed-cell-size spatial hash grid that maps world positions to buckets.
/// Used as a broad-phase filter so we only run expensive LoS checks between
/// entities that are in nearby cells.
///
/// Complexity: Insert / Remove / Query are all O(1) amortised per entity.
/// Nearby-query returns entities in the same cell and the 26 surrounding cells (3×3×3 cube).
/// </summary>
public sealed class SpatialGrid<T> where T : class
{
	/// <summary>
	/// Stores the cell coordinate and the item together so we can remove without re-hashing.
	/// </summary>
	public readonly record struct Entry( T Item, int CellX, int CellY, int CellZ );

	private readonly Dictionary<long, List<T>> _cells = new( 256 );
	private readonly Dictionary<T, Entry> _entries = new( 64 );
	private float _cellSize;
	private float _inverseCellSize;

	public SpatialGrid( float cellSize )
	{
		_cellSize = cellSize;
		_inverseCellSize = 1f / cellSize;
	}

	/// <summary>
	/// Update the cell size at runtime (e.g. from a ConVar change).
	/// This clears and rebuilds nothing — callers must re-insert.
	/// </summary>
	public void SetCellSize( float cellSize )
	{
		if ( MathF.Abs( _cellSize - cellSize ) < 0.01f )
			return;

		_cellSize = cellSize;
		_inverseCellSize = 1f / cellSize;
		Clear();
	}

	/// <summary>
	/// Total number of tracked items.
	/// </summary>
	public int Count => _entries.Count;

	/// <summary>
	/// Insert or update an item at the given world position.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void InsertOrUpdate( T item, Vector3 position )
	{
		var cx = WorldToCell( position.x );
		var cy = WorldToCell( position.y );
		var cz = WorldToCell( position.z );

		if ( _entries.TryGetValue( item, out var existing ) )
		{
			// Same cell — nothing to do.
			if ( existing.CellX == cx && existing.CellY == cy && existing.CellZ == cz )
				return;

			// Remove from old cell.
			RemoveFromCell( existing );
		}

		var key = PackKey( cx, cy, cz );

		if ( !_cells.TryGetValue( key, out var list ) )
		{
			list = new List<T>( 4 );
			_cells[key] = list;
		}

		list.Add( item );
		_entries[item] = new Entry( item, cx, cy, cz );
	}

	/// <summary>
	/// Remove an item from the grid entirely.
	/// </summary>
	public bool Remove( T item )
	{
		if ( !_entries.Remove( item, out var entry ) )
			return false;

		RemoveFromCell( entry );
		return true;
	}

	/// <summary>
	/// Clear all data.
	/// </summary>
	public void Clear()
	{
		_cells.Clear();
		_entries.Clear();
	}

	/// <summary>
	/// Query all items within the 3×3×3 neighbourhood of the cell containing <paramref name="position"/>.
	/// Results are written into the provided list to avoid allocations.
	/// </summary>
	public void QueryNearby( Vector3 position, List<T> results )
	{
		results.Clear();

		var cx = WorldToCell( position.x );
		var cy = WorldToCell( position.y );
		var cz = WorldToCell( position.z );

		for ( var dx = -1; dx <= 1; dx++ )
		{
			for ( var dy = -1; dy <= 1; dy++ )
			{
				for ( var dz = -1; dz <= 1; dz++ )
				{
					var key = PackKey( cx + dx, cy + dy, cz + dz );

					if ( _cells.TryGetValue( key, out var list ) )
					{
						results.AddRange( list );
					}
				}
			}
		}
	}

	/// <summary>
	/// Query all items within a given world-space radius of <paramref name="position"/>.
	/// First does the 3×3×3 cell query, then distance-filters.
	/// </summary>
	public void QueryRadius( Vector3 position, float radius, List<T> results, System.Func<T, Vector3> positionGetter )
	{
		// Expand the cell search to cover the radius.
		var cellSpan = (int)MathF.Ceiling( radius * _inverseCellSize );
		var radiusSq = radius * radius;

		results.Clear();

		var cx = WorldToCell( position.x );
		var cy = WorldToCell( position.y );
		var cz = WorldToCell( position.z );

		for ( var dx = -cellSpan; dx <= cellSpan; dx++ )
		{
			for ( var dy = -cellSpan; dy <= cellSpan; dy++ )
			{
				for ( var dz = -cellSpan; dz <= cellSpan; dz++ )
				{
					var key = PackKey( cx + dx, cy + dy, cz + dz );

					if ( !_cells.TryGetValue( key, out var list ) )
						continue;

					foreach ( var item in list )
					{
						var itemPos = positionGetter( item );
						if ( position.DistanceSquared( itemPos ) <= radiusSq )
							results.Add( item );
					}
				}
			}
		}
	}

	// ─── Internals ───────────────────────────────────────────────

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private int WorldToCell( float v ) => (int)MathF.Floor( v * _inverseCellSize );

	/// <summary>
	/// Pack three 21-bit cell coordinates into a single 64-bit key.
	/// Supports cell indices roughly in the range ±1 048 575 which at 512 unit cells
	/// covers ±536 million units — far beyond any practical map.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static long PackKey( int x, int y, int z )
	{
		// Mask to 21 bits (0x1FFFFF) to handle negative coordinates.
		const long mask = 0x1FFFFF;
		return ((long)(x) & mask) | (((long)(y) & mask) << 21) | (((long)(z) & mask) << 42);
	}

	private void RemoveFromCell( Entry entry )
	{
		var key = PackKey( entry.CellX, entry.CellY, entry.CellZ );

		if ( !_cells.TryGetValue( key, out var list ) )
			return;

		list.Remove( entry.Item );

		if ( list.Count == 0 )
			_cells.Remove( key );
	}
}
