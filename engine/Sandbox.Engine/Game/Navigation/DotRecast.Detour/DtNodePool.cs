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

namespace DotRecast.Detour
{
	// Flat node array + index-chained hash buckets, matching the original C++ dtNodePool.
	internal class DtNodePool
	{
		private const int HashSize = 1024; // power of two

		private DtNode[] m_nodes;
		private int[] m_next;
		private readonly int[] m_first;
		private int m_nodeCount;

		private readonly List<DtNode> m_findScratch = new();

		public DtNodePool()
		{
			m_nodes = new DtNode[128];
			m_next = new int[128];
			m_first = new int[HashSize];
			Array.Fill( m_first, -1 );
		}

		private static int HashRef( long id )
		{
			ulong h = (ulong)id * 0x9E3779B97F4A7C15UL;
			return (int)(h >> 32) & (HashSize - 1);
		}

		public void Clear()
		{
			// Release shortcut lists so they don't linger on a pooled pool
			for ( int i = 0; i < m_nodeCount; i++ )
			{
				var node = m_nodes[i];
				if ( node != null )
					node.shortcut = null;
			}

			Array.Fill( m_first, -1 );
			m_nodeCount = 0;
		}

		public int GetNodeCount()
		{
			return m_nodeCount;
		}

		/// <summary>
		/// Find every node for the given poly ref. The returned list is reused by the next call.
		/// </summary>
		public int FindNodes( long id, out List<DtNode> nodes )
		{
			m_findScratch.Clear();

			for ( int i = m_first[HashRef( id )]; i != -1; i = m_next[i] )
			{
				if ( m_nodes[i].id == id )
					m_findScratch.Add( m_nodes[i] );
			}

			nodes = m_findScratch;
			return m_findScratch.Count;
		}

		public DtNode FindNode( long id )
		{
			// Chains prepend, so the last match is the first node created for this id
			DtNode found = null;

			for ( int i = m_first[HashRef( id )]; i != -1; i = m_next[i] )
			{
				if ( m_nodes[i].id == id )
					found = m_nodes[i];
			}

			return found;
		}

		public DtNode GetNode( long id, int state )
		{
			int bucket = HashRef( id );

			for ( int i = m_first[bucket]; i != -1; i = m_next[i] )
			{
				var node = m_nodes[i];
				if ( node.id == id && node.state == state )
					return node;
			}

			return Create( id, state, bucket );
		}

		private DtNode Create( long id, int state, int bucket )
		{
			if ( m_nodeCount >= m_nodes.Length )
			{
				Array.Resize( ref m_nodes, m_nodes.Length * 2 );
				Array.Resize( ref m_next, m_next.Length * 2 );
			}

			int i = m_nodeCount++;
			var node = m_nodes[i] ??= new DtNode( i );
			node.pos = Vector3.Zero; // reset: nodes are reused across Clear()
			node.pidx = 0;
			node.cost = 0;
			node.total = 0;
			node.id = id;
			node.state = state;
			node.flags = 0;
			node.shortcut = null;

			m_next[i] = m_first[bucket];
			m_first[bucket] = i;

			return node;
		}

		public int GetNodeIdx( DtNode node )
		{
			return node != null
				? node.ptr + 1
				: 0;
		}

		public DtNode GetNodeAtIdx( int idx )
		{
			return idx != 0
				? m_nodes[idx - 1]
				: null;
		}

		public DtNode GetNode( long refs )
		{
			return GetNode( refs, 0 );
		}

		public IEnumerable<DtNode> AsEnumerable()
		{
			return m_nodes.Take( m_nodeCount );
		}
	}
}
