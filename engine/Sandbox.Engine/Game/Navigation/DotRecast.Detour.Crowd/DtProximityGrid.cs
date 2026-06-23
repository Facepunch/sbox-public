/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com
Copyright (c) 2024 Facepunch Studios Ltd

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System.Runtime.CompilerServices;

namespace DotRecast.Detour.Crowd
{
	// Flat item pool + index-chained hash buckets, matching the original C++ dtProximityGrid.
	internal class DtProximityGrid
	{
		private struct Item
		{
			public long Key;
			public DtCrowdAgent Agent;
			public int Next;
		}

		private const int BucketCount = 1024; // power of two

		private readonly float _cellSize;
		private readonly float _invCellSize;

		private Item[] _pool = new Item[256];
		private int _poolHead;
		private readonly int[] _buckets = new int[BucketCount];

		public DtProximityGrid( float cellSize )
		{
			_cellSize = cellSize;
			_invCellSize = 1.0f / cellSize;
			Array.Fill( _buckets, -1 );
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public static long CombineKey( int x, int y )
		{
			uint ux = (uint)x;
			uint uy = (uint)y;
			return ((long)ux << 32) | uy;
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		public static void DecomposeKey( long key, out int x, out int y )
		{
			uint ux = (uint)(key >> 32);
			uint uy = (uint)key;
			x = (int)ux;
			y = (int)uy;
		}

		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		private static int HashKey( long key )
		{
			ulong h = (ulong)key * 0x9E3779B97F4A7C15UL;
			return (int)(h >> 32) & (BucketCount - 1);
		}

		public void Clear()
		{
			Array.Fill( _buckets, -1 );

			// Don't keep agents alive through the pool
			Array.Clear( _pool, 0, _poolHead );
			_poolHead = 0;
		}

		public void AddItem( DtCrowdAgent agent, float minx, float miny, float maxx, float maxy )
		{
			int iminx = (int)MathF.Floor( minx * _invCellSize );
			int iminy = (int)MathF.Floor( miny * _invCellSize );
			int imaxx = (int)MathF.Floor( maxx * _invCellSize );
			int imaxy = (int)MathF.Floor( maxy * _invCellSize );

			for ( int y = iminy; y <= imaxy; ++y )
			{
				for ( int x = iminx; x <= imaxx; ++x )
				{
					if ( _poolHead >= _pool.Length )
					{
						Array.Resize( ref _pool, _pool.Length * 2 );
					}

					long key = CombineKey( x, y );
					int bucket = HashKey( key );

					_pool[_poolHead] = new Item { Key = key, Agent = agent, Next = _buckets[bucket] };
					_buckets[bucket] = _poolHead++;
				}
			}
		}

		public int QueryItems( float minx, float miny, float maxx, float maxy, Span<int> ids, int maxIds )
		{
			int iminx = (int)MathF.Floor( minx * _invCellSize );
			int iminy = (int)MathF.Floor( miny * _invCellSize );
			int imaxx = (int)MathF.Floor( maxx * _invCellSize );
			int imaxy = (int)MathF.Floor( maxy * _invCellSize );

			int n = 0;

			for ( int y = iminy; y <= imaxy; ++y )
			{
				for ( int x = iminx; x <= imaxx; ++x )
				{
					long key = CombineKey( x, y );

					for ( int idx = _buckets[HashKey( key )]; idx != -1; idx = _pool[idx].Next )
					{
						if ( _pool[idx].Key != key )
							continue;

						var item = _pool[idx].Agent;

						// Check if the id exists already.
						int end = n;
						int i = 0;
						while ( i != end && ids[i] != item.idx )
						{
							++i;
						}

						// Item not found, add it.
						if ( i == n )
						{
							ids[n++] = item.idx;

							if ( n >= maxIds )
								return n;
						}
					}
				}
			}

			return n;
		}

		public IEnumerable<(long, int)> GetItemCounts()
		{
			var counts = new Dictionary<long, int>();

			for ( int i = 0; i < _poolHead; i++ )
			{
				counts.TryGetValue( _pool[i].Key, out var count );
				counts[_pool[i].Key] = count + 1;
			}

			return counts.Select( e => (e.Key, e.Value) );
		}

		public float GetCellSize()
		{
			return _cellSize;
		}
	}
}
