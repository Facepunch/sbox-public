using Borealis.Source2.Compression;
using Borealis.Source2.Models;
using Borealis.Source2.Resources;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Borealis.Source2.World;

/// <summary>
/// A compiled map's visibility (<c>maps/name/world_visibility.vvis_c</c>, native's <c>CVoxelVisibility</c>): an octree
/// over the map whose leaves hold regions, each a 4x4x4 voxel mask and the cluster it belongs to, and one bit row per
/// cluster saying which clusters it sees. The queries answer as native's <c>CPVS</c> did (<c>IsInPVS</c> and
/// <c>IsAbsBoxInPVS</c> with a list of sources), step for step, and allocate nothing. Network visibility reads it; nothing
/// renders with it.
/// </summary>
public sealed class WorldVisibility
{
	/// <summary>Native's <c>VOXVIS_CLUSTER_ALL_VISIBILE</c>: everything is visible from it.</summary>
	const int AllVisibleCluster = 0;

	/// <summary>Native's <c>VOXVIS_CLUSTER_OUTSIDE_WORLD</c>: bounds that touch no cluster.</summary>
	const int OutsideWorldCluster = 1;

	/// <summary>Native's <c>VOXVIS_RESERVED_CLUSTER_COUNT</c>.</summary>
	const int ReservedClusters = 2;

	/// <summary>Native's <c>RECENT_CLUSTER_MAILBOX_COUNT</c>, minus one.</summary>
	const int MailboxMask = 511;

	// CVoxelVisNode: the low word is the leaf flag (bit 0) and the first child or region, the high word the region
	// count (8 bits) and the enclosed cluster list (24 bits)
	readonly ulong[] nodes;

	// CVoxelVisRegion: cluster (15 bits), intersects geometry (1), leaf (24), mask (24)
	readonly ulong[] regions;

	readonly ulong[] masks;

	// VisClusterListEntry_t: start and count, in pairs
	readonly uint[] enclosedLists;

	readonly ushort[] enclosedClusters;

	// One row of rowWords per cluster, then the sky and sun rows
	readonly uint[] bits;

	readonly int rowWords;

	/// <summary>Clusters, counting the two reserved ones (<c>m_nBaseClusterCount</c>).</summary>
	public int ClusterCount { get; }

	/// <summary>The octree's bounds, in the map's space.</summary>
	public Vector3 Mins { get; }

	/// <summary>The octree's bounds, in the map's space.</summary>
	public Vector3 Maxs { get; }

	/// <summary>The size of a vis voxel (<c>m_flGridSize</c>).</summary>
	public float GridSize { get; }

	/// <summary>
	/// Only the two reserved clusters: no visibility was built. Native's callers skip such a map (<c>IsEmptyPVS</c>), so the
	/// queries here see everything.
	/// </summary>
	public bool IsEmpty => ClusterCount <= ReservedClusters;

	/// <summary>Bytes held, roughly.</summary>
	public long Size => nodes.Length * 8L + regions.Length * 8L + masks.Length * 8L + enclosedLists.Length * 4L + enclosedClusters.Length * 2L + bits.Length * 4L;

	WorldVisibility( Dictionary<string, object> root, ReadOnlySpan<byte> payload )
	{
		ClusterCount = (int)Kv.Number( root.Get( "m_nBaseClusterCount" ) );
		var rowBytes = (int)Kv.Number( root.Get( "m_nPVSBytesPerCluster" ) );
		if ( rowBytes % 4 != 0 || ClusterCount < 0 ) throw new InvalidDataException( $"Bad visibility rows: {ClusterCount} clusters of {rowBytes} bytes" );
		rowWords = rowBytes / 4;
		Mins = Kv.Vector3( root.Get( "m_vMinBounds" ) );
		Maxs = Kv.Vector3( root.Get( "m_vMaxBounds" ) );
		GridSize = (float)Kv.Number( root.Get( "m_flGridSize" ) );

		nodes = Block<ulong>( root, "m_NodeBlock", payload, 8 );
		regions = Block<ulong>( root, "m_RegionBlock", payload, 8 );
		enclosedLists = Block<uint>( root, "m_EnclosedClusterListBlock", payload, 8 );
		enclosedClusters = Block<ushort>( root, "m_EnclosedClustersBlock", payload, 2 );
		masks = Block<ulong>( root, "m_MasksBlock", payload, 8 );
		bits = Block<uint>( root, "m_nVisBlocks", payload, 1 );
		if ( !IsEmpty && (nodes.Length == 0 || bits.Length < ClusterCount * rowWords || rowWords * 32 < ClusterCount) )
			throw new InvalidDataException( $"Visibility has {nodes.Length} nodes and {bits.Length} words of rows for {ClusterCount} clusters" );
	}

	/// <summary>A payload block (<c>VoxelVisBlockOffset_t</c>) as an array; its count is in elements of <paramref name="size"/> bytes.</summary>
	static T[] Block<T>( Dictionary<string, object> root, string name, ReadOnlySpan<byte> payload, int size ) where T : unmanaged
	{
		var block = root.Get( name ) as Dictionary<string, object> ?? throw new InvalidDataException( $"Visibility has no {name}" );
		var offset = (long)Kv.Number( block.Get( "m_nOffset" ) );
		var bytes = (long)Kv.Number( block.Get( "m_nElementCount" ) ) * size;
		if ( offset < 0 || bytes < 0 || offset + bytes > payload.Length ) throw new InvalidDataException( $"Visibility {name} runs off the payload" );
		return MemoryMarshal.Cast<byte, T>( payload.Slice( (int)offset, (int)bytes ) ).ToArray();
	}

	/// <summary>
	/// Read a map's visibility, or null when the file isn't one native would load (it then sees everything, as here).
	/// Version 8 stores the payload in a <c>VXVZ</c> block (its size, then zstd of it with each 8-byte lane's bytes
	/// transposed), version 7 plainly in <c>VXVS</c>.
	/// </summary>
	public static WorldVisibility Read( byte[] data )
	{
		var resource = ResourceFile.Read( data );
		if ( resource.ResourceVersion is not (7 or 8) ) return null;

		var root = Kv3.Read( resource.Block( "DATA" ) ) as Dictionary<string, object> ?? throw new InvalidDataException( "Visibility DATA isn't an object" );
		var vis = root.Get( "m_nVisBlocks" ) as Dictionary<string, object> ?? throw new InvalidDataException( "Visibility has no m_nVisBlocks" );
		var size = (long)Kv.Number( vis.Get( "m_nOffset" ) ) + (long)Kv.Number( vis.Get( "m_nElementCount" ) );
		if ( size <= 0 || size > int.MaxValue ) throw new InvalidDataException( $"Visibility payload of {size} bytes" );

		if ( resource.ResourceVersion == 7 )
		{
			var plain = resource.Block( "VXVS" );
			if ( plain.Length < size ) throw new InvalidDataException( "Visibility VXVS is short" );
			return new WorldVisibility( root, plain[..(int)size] );
		}

		var block = resource.Block( "VXVZ" );
		if ( block.Length <= 4 || BinaryPrimitives.ReadUInt32LittleEndian( block ) != size ) throw new InvalidDataException( "Visibility VXVZ is corrupt" );
		var transposed = Zstd.Decompress( block[4..], (int)size );
		var payload = new byte[size];
		var lanes = (int)(size / 8);
		for ( var lane = 0; lane < lanes; lane++ )
		{
			for ( var b = 0; b < 8; b++ )
				payload[lane * 8 + b] = transposed[b * lanes + lane];
		}

		transposed.AsSpan( lanes * 8 ).CopyTo( payload.AsSpan( lanes * 8 ) );
		return new WorldVisibility( root, payload );
	}

	/// <summary>Read <c>world_visibility.vvis</c> of the map whose folder is <paramref name="mapFolder"/>, or null when there's none.</summary>
	public static WorldVisibility Load( ContentFiles content, string mapFolder )
		=> content.TryRead( mapFolder + "/world_visibility.vvis" ) is { } data ? Read( data ) : null;

	/// <summary>
	/// Whether <paramref name="point"/> is visible from any of <paramref name="sources"/> (native's <c>IsInPVS</c>), with
	/// the map placed at <paramref name="origin"/>. No sources see nothing.
	/// </summary>
	public bool IsPointVisible( ReadOnlySpan<Vector3> sources, Vector3 point, Vector3 origin )
	{
		if ( IsEmpty ) return true;

		Span<uint> seen = stackalloc uint[rowWords];
		if ( Gather( sources, origin, seen ) ) return true;
		return Sees( seen, ClusterAt( point - origin ) );
	}

	/// <summary>
	/// Whether any of the box from <paramref name="mins"/> to <paramref name="maxs"/> is visible from any of
	/// <paramref name="sources"/> (native's <c>IsAbsBoxInPVS</c>), with the map placed at <paramref name="origin"/>.
	/// </summary>
	public bool IsBoxVisible( ReadOnlySpan<Vector3> sources, Vector3 mins, Vector3 maxs, Vector3 origin )
	{
		if ( IsEmpty ) return true;

		Span<uint> seen = stackalloc uint[rowWords];
		if ( Gather( sources, origin, seen ) ) return true;

		// GetClustersForBounds: grown a little so it's never empty; one that holds the whole tree is in cluster 0
		var lo = mins - origin;
		var hi = maxs - origin;
		var center = 0.5f * (hi + lo);
		var extent = Vector3.Max( hi - center, Vector3.Zero ) + new Vector3( 0.03125f );
		lo = center - extent;
		hi = center + extent;
		if ( Contains( lo, hi, Mins, Maxs ) ) return Sees( seen, AllVisibleCluster );

		var walk = new Walk { Seen = seen, Test = true, Smallest = int.MaxValue };
		ClustersInBox( lo, hi, ref walk );
		if ( walk.Visible ) return true;

		// Touching no cluster, it's outside the world
		return !walk.Any && Sees( seen, OutsideWorldCluster );
	}

	/// <summary>
	/// The cluster at a point in the map's space (native's <c>GetClusterForPosition</c>): its region's, or in solid
	/// space the smallest cluster within half a voxel, or cluster 0.
	/// </summary>
	public int ClusterAt( Vector3 point )
	{
		if ( IsEmpty || nodes.Length < 1 ) return AllVisibleCluster;

		var min = Mins;
		var max = Maxs;
		var leaf = LeafAt( point, ref min, ref max );
		if ( leaf >= 0 )
		{
			var node = nodes[leaf];
			var count = RegionCount( node );
			if ( count > 0 )
			{
				var first = FirstChild( node );
				var pointMask = PointMask( min, max, point );
				for ( var i = 0; i < count; i++ )
				{
					var region = regions[first + i];
					if ( (masks[MaskIndex( region )] & pointMask) != 0 ) return Cluster( region );
				}
			}
		}

		var half = new Vector3( GridSize * 0.5f );
		var walk = new Walk { Smallest = int.MaxValue };
		ClustersInBox( point - half, point + half, ref walk );
		return walk.Any ? walk.Smallest : AllVisibleCluster;
	}

	/// <summary>
	/// What the sources see, OR'd into <paramref name="seen"/> (native's <c>AddOriginToPVS</c> per source). True when they
	/// see everything: a source outside the tree, or in a leaf with no cluster at its point.
	/// </summary>
	bool Gather( ReadOnlySpan<Vector3> sources, Vector3 origin, Span<uint> seen )
	{
		seen.Clear();
		foreach ( var source in sources )
		{
			var point = source - origin;
			var min = Mins;
			var max = Maxs;
			var leaf = LeafAt( point, ref min, ref max );
			if ( leaf < 0 ) return true;

			var found = false;
			var node = nodes[leaf];
			var count = RegionCount( node );
			if ( count > 0 )
			{
				var first = FirstChild( node );
				var pointMask = PointMask( min, max, point );
				for ( var i = 0; i < count; i++ )
				{
					var region = regions[first + i];
					if ( (masks[MaskIndex( region )] & pointMask) == 0 ) continue;

					var cluster = Cluster( region );
					if ( cluster >= ClusterCount ) continue;

					var row = bits.AsSpan( cluster * rowWords, rowWords );
					for ( var w = 0; w < row.Length; w++ ) seen[w] |= row[w];
					found = true;
				}
			}

			if ( !found ) return true;
		}

		return false;
	}

	bool Sees( ReadOnlySpan<uint> seen, int cluster )
		=> (uint)cluster < (uint)ClusterCount && (seen[cluster >> 5] & (1u << (cluster & 31))) != 0;

	/// <summary>The leaf holding a point, its bounds left in <paramref name="min"/> and <paramref name="max"/>; -1 outside the tree.</summary>
	int LeafAt( Vector3 point, ref Vector3 min, ref Vector3 max )
	{
		if ( point.X < min.X || point.Y < min.Y || point.Z < min.Z || point.X > max.X || point.Y > max.Y || point.Z > max.Z ) return -1;

		var index = 0u;
		while ( true )
		{
			var node = nodes[index];
			if ( IsLeaf( node ) ) return (int)index;

			var center = (min + max) * 0.5f;
			var child = (point.X > center.X ? 1 : 0) | (point.Y > center.Y ? 2 : 0) | (point.Z > center.Z ? 4 : 0);
			ChildBounds( ref min, ref max, center, child );
			var next = FirstChild( node ) + (uint)child;
			if ( next >= (uint)nodes.Length ) return -1;
			index = next;
		}
	}

	/// <summary>The clusters a box touches, in native's order, each handed to <see cref="Add"/> until it says stop.</summary>
	void ClustersInBox( Vector3 lo, Vector3 hi, ref Walk walk )
	{
		var size = hi - lo;
		if ( MathF.Min( size.X, MathF.Min( size.Y, size.Z ) ) >= 512.0f ) Enclosed( 0, Mins, Maxs, lo, hi, ref walk );
		else Touching( 0, Mins, Maxs, lo, hi, ref walk );
	}

	/// <summary>Native's <c>ClustersInBoxEnclosed_r</c>: nodes the box holds whole give their enclosed cluster list.</summary>
	bool Enclosed( uint index, Vector3 min, Vector3 max, Vector3 lo, Vector3 hi, ref Walk walk )
	{
		var node = nodes[index];
		var list = EnclosedList( node );
		if ( list < 0xFFFFFF && Contains( lo, hi, min, max ) )
		{
			var start = enclosedLists[list * 2];
			var count = enclosedLists[list * 2 + 1];
			for ( var i = 0u; i < count; i++ )
			{
				if ( Add( enclosedClusters[start + i], ref walk ) ) return true;
			}

			return false;
		}

		if ( IsLeaf( node ) ) return Leaf( node, min, max, lo, hi, ref walk );

		var center = (min + max) * 0.5f;
		for ( var i = 0; i < 8; i++ )
		{
			var childMin = min;
			var childMax = max;
			ChildBounds( ref childMin, ref childMax, center, i );
			if ( !Overlaps( lo, hi, childMin, childMax ) ) continue;

			var child = FirstChild( node ) + (uint)i;
			if ( child < (uint)nodes.Length && Enclosed( child, childMin, childMax, lo, hi, ref walk ) ) return true;
		}

		return false;
	}

	/// <summary>Native's <c>ClustersInBox_r</c>.</summary>
	bool Touching( uint index, Vector3 min, Vector3 max, Vector3 lo, Vector3 hi, ref Walk walk )
	{
		var node = nodes[index];
		if ( IsLeaf( node ) ) return Leaf( node, min, max, lo, hi, ref walk );

		var center = (min + max) * 0.5f;
		for ( var i = 0; i < 8; i++ )
		{
			var childMin = min;
			var childMax = max;
			ChildBounds( ref childMin, ref childMax, center, i );
			if ( !Overlaps( lo, hi, childMin, childMax ) ) continue;

			var child = FirstChild( node ) + (uint)i;
			if ( child < (uint)nodes.Length && Touching( child, childMin, childMax, lo, hi, ref walk ) ) return true;
		}

		return false;
	}

	bool Leaf( ulong node, Vector3 min, Vector3 max, Vector3 lo, Vector3 hi, ref Walk walk )
	{
		var boxMask = BoxMask( lo, hi, min, max );
		var first = FirstChild( node );
		var count = RegionCount( node );
		for ( var i = 0; i < count; i++ )
		{
			var region = regions[first + i];
			if ( (masks[MaskIndex( region )] & boxMask) != 0 && Add( Cluster( region ), ref walk ) ) return true;
		}

		return false;
	}

	/// <summary>
	/// A cluster the box touches. Native collects them through a mailbox whose slot 0 starts out holding cluster 0, so
	/// cluster 0 only counts after a cluster sharing its slot (512, 1024...) came first; the rest only repeat.
	/// </summary>
	bool Add( int cluster, ref Walk walk )
	{
		if ( (cluster & MailboxMask) == 0 )
		{
			if ( walk.Slot0 == cluster ) return false;
			walk.Slot0 = cluster;
		}

		walk.Any = true;
		if ( cluster < walk.Smallest ) walk.Smallest = cluster;
		if ( !walk.Test || !Sees( walk.Seen, cluster ) ) return false;

		walk.Visible = true;
		return true;
	}

	ref struct Walk
	{
		/// <summary>What the sources see; tested when <see cref="Test"/>.</summary>
		public ReadOnlySpan<uint> Seen;
		public bool Test;
		public int Slot0;
		public bool Any;
		public bool Visible;
		public int Smallest;
	}

	/// <summary>Native's <c>Vox64::IntersectPointWithVoxelHierarchy</c>: the one of a node's 4x4x4 voxels (x fastest) holding a point.</summary>
	static ulong PointMask( Vector3 min, Vector3 max, Vector3 point )
	{
		var center = (min + max) * 0.5f;
		var outer = (point.X > center.X ? 1 : 0) | (point.Y > center.Y ? 2 : 0) | (point.Z > center.Z ? 4 : 0);
		ChildBounds( ref min, ref max, center, outer );
		center = (min + max) * 0.5f;
		var x = (outer & 1) * 2 + (point.X > center.X ? 1 : 0);
		var y = ((outer >> 1) & 1) * 2 + (point.Y > center.Y ? 1 : 0);
		var z = ((outer >> 2) & 1) * 2 + (point.Z > center.Z ? 1 : 0);
		return 1ul << (x + 4 * y + 16 * z);
	}

	/// <summary>Native's <c>Vox64::IntersectBoxWithVoxelHierarchy</c>: the voxels of a node's 4x4x4 (sized by its x extent) a box touches.</summary>
	static ulong BoxMask( Vector3 lo, Vector3 hi, Vector3 min, Vector3 max )
	{
		var size = 0.25f * (max.X - min.X);
		var xs = AxisMask( lo.X, hi.X, min.X, size );
		var ys = AxisMask( lo.Y, hi.Y, min.Y, size );
		var zs = AxisMask( lo.Z, hi.Z, min.Z, size );
		var mask = 0ul;
		for ( var z = 0; z < 4; z++ )
		{
			if ( (zs & (1u << z)) == 0 ) continue;
			for ( var y = 0; y < 4; y++ )
			{
				if ( (ys & (1u << y)) != 0 ) mask |= (ulong)xs << (4 * y + 16 * z);
			}
		}

		return mask;
	}

	static uint AxisMask( float lo, float hi, float min, float size )
	{
		var mask = 0u;
		for ( var i = 0; i < 4; i++ )
		{
			var start = min + size * i;
			var end = start + size;
			if ( !(hi < start || end < lo) ) mask |= 1u << i;
		}

		return mask;
	}

	/// <summary>Native's <c>Vox8::BoundsForChildVoxel</c>: child bit 1 takes the upper half in x, 2 in y, 4 in z.</summary>
	static void ChildBounds( ref Vector3 min, ref Vector3 max, Vector3 center, int child )
	{
		if ( (child & 1) != 0 ) min.X = center.X; else max.X = center.X;
		if ( (child & 2) != 0 ) min.Y = center.Y; else max.Y = center.Y;
		if ( (child & 4) != 0 ) min.Z = center.Z; else max.Z = center.Z;
	}

	/// <summary>Whether the box from <paramref name="lo"/> to <paramref name="hi"/> holds the other whole.</summary>
	static bool Contains( Vector3 lo, Vector3 hi, Vector3 min, Vector3 max )
		=> min.X >= lo.X && min.Y >= lo.Y && min.Z >= lo.Z && max.X <= hi.X && max.Y <= hi.Y && max.Z <= hi.Z;

	static bool Overlaps( Vector3 lo, Vector3 hi, Vector3 min, Vector3 max )
		=> !(max.X < lo.X || max.Y < lo.Y || max.Z < lo.Z || hi.X < min.X || hi.Y < min.Y || hi.Z < min.Z);

	static bool IsLeaf( ulong node ) => (node & 1) != 0;
	static uint FirstChild( ulong node ) => (uint)node >> 1;
	static int RegionCount( ulong node ) => (int)((node >> 32) & 0xFF);
	static uint EnclosedList( ulong node ) => (uint)(node >> 40);
	static int Cluster( ulong region ) => (int)(region & 0x7FFF);
	static int MaskIndex( ulong region ) => (int)(region >> 40);
}
