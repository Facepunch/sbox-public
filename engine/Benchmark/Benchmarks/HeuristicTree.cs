using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sandbox.SceneRenderer.Culling;
using V3 = System.Numerics.Vector3;

internal struct SurfaceAreaPolicy { }
internal struct VolumePolicy { }
internal struct ProximityPolicy { }
internal struct LeafWeightedPolicy { }

/// <summary>
/// Controlled insertion-heuristic experiments. All retain SpatialTree's padding and height rotations.
/// Policies are inspired by ReactPhysics3D (volume), Bullet DBVT (L1 center distance), and BEPU (leaf-weighted SAH).
/// These are not ports of those libraries' complete trees. SurfaceAreaPolicy is the control.
/// </summary>
internal sealed class HeuristicTree<TPolicy> where TPolicy : struct
{
	const int Null = -1;
	struct Node
	{
		public V3 Min, Max;
		public int Parent, Child1, Child2, Height, LeafCount, Item;
		public readonly bool IsLeaf => Child1 == Null;
	}

	Node[] nodes = new Node[64];
	int root = Null, freeList = Null, nodeCount;
	[ThreadStatic] static int[] stack;
	public int Height => root == Null ? 0 : nodes[root].Height;

	public HeuristicTree() => BuildFreeList( 0 );

	void BuildFreeList( int from )
	{
		for ( int i = from; i < nodes.Length - 1; i++ )
		{
			nodes[i].Parent = i + 1;
			nodes[i].Height = -1;
		}
		nodes[^1].Parent = Null;
		nodes[^1].Height = -1;
		freeList = from;
	}

	int AllocateNode()
	{
		if ( freeList == Null )
		{
			var old = nodes.Length;
			Array.Resize( ref nodes, old * 2 );
			BuildFreeList( old );
		}
		var id = freeList;
		ref var node = ref nodes[id];
		freeList = node.Parent;
		node.Parent = Null;
		node.Child1 = Null;
		node.Child2 = Null;
		node.Height = 0;
		node.LeafCount = 1;
		node.Item = Null;
		nodeCount++;
		return id;
	}

	void FreeNode( int id )
	{
		nodes[id].Parent = freeList;
		nodes[id].Height = -1;
		freeList = id;
		nodeCount--;
	}

	static V3 Margin( in V3 min, in V3 max ) => V3.Max( (max - min) * 0.1f, V3.One );

	public int Add( in V3 min, in V3 max, int item )
	{
		var leaf = AllocateNode();
		ref var node = ref nodes[leaf];
		var margin = Margin( min, max );
		node.Min = min - margin;
		node.Max = max + margin;
		node.Item = item;
		InsertLeaf( leaf );
		return leaf;
	}

	public void Remove( int leaf )
	{
		RemoveLeaf( leaf );
		FreeNode( leaf );
	}

	public bool Move( int leaf, in V3 min, in V3 max )
	{
		ref var node = ref nodes[leaf];
		var margin = Margin( min, max );
		var fatMin = min - margin;
		var fatMax = max + margin;
		if ( Contains( node.Min, node.Max, min, max )
			&& Contains( fatMin - 4.0f * margin, fatMax + 4.0f * margin, node.Min, node.Max ) ) return false;
		RemoveLeaf( leaf );
		node.Min = fatMin;
		node.Max = fatMax;
		InsertLeaf( leaf );
		return true;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static bool Contains( in V3 outerMin, in V3 outerMax, in V3 min, in V3 max ) =>
		min.X >= outerMin.X && min.Y >= outerMin.Y && min.Z >= outerMin.Z
		&& max.X <= outerMax.X && max.Y <= outerMax.Y && max.Z <= outerMax.Z;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static float Metric( in V3 min, in V3 max )
	{
		var d = max - min;
		if ( typeof( TPolicy ) == typeof( VolumePolicy ) ) return d.X * d.Y * d.Z;
		return 2.0f * (d.X * d.Y + d.Y * d.Z + d.Z * d.X);
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static float Proximity( in V3 centerSum, in Node node )
	{
		var d = V3.Abs( centerSum - (node.Min + node.Max) );
		return d.X + d.Y + d.Z;
	}

	float ChildCost( int child, in V3 min, in V3 max )
	{
		ref var node = ref nodes[child];
		var combined = Metric( V3.Min( node.Min, min ), V3.Max( node.Max, max ) );
		if ( typeof( TPolicy ) == typeof( LeafWeightedPolicy ) )
			return combined * (node.LeafCount + 1) - Metric( node.Min, node.Max ) * node.LeafCount;
		return node.IsLeaf ? combined : combined - Metric( node.Min, node.Max );
	}

	void InsertLeaf( int leaf )
	{
		if ( root == Null )
		{
			root = leaf;
			nodes[root].Parent = Null;
			return;
		}
		var min = nodes[leaf].Min;
		var max = nodes[leaf].Max;
		var index = root;
		while ( !nodes[index].IsLeaf )
		{
			ref var node = ref nodes[index];
			var child1 = node.Child1;
			var child2 = node.Child2;
			if ( typeof( TPolicy ) == typeof( ProximityPolicy ) )
			{
				var centerSum = min + max;
				index = Proximity( centerSum, nodes[child1] ) < Proximity( centerSum, nodes[child2] ) ? child1 : child2;
			}
			else if ( typeof( TPolicy ) == typeof( LeafWeightedPolicy ) )
			{
				var cost1 = ChildCost( child1, min, max );
				var cost2 = ChildCost( child2, min, max );
				var first = cost1 == cost2 ? nodes[child1].LeafCount < nodes[child2].LeafCount : cost1 < cost2;
				index = first ? child1 : child2;
			}
			else
			{
				var area = Metric( node.Min, node.Max );
				var combined = Metric( V3.Min( node.Min, min ), V3.Max( node.Max, max ) );
				var cost = 2.0f * combined;
				var inheritance = 2.0f * (combined - area);
				var cost1 = ChildCost( child1, min, max ) + inheritance;
				var cost2 = ChildCost( child2, min, max ) + inheritance;
				if ( cost < cost1 && cost < cost2 ) break;
				index = cost1 < cost2 ? child1 : child2;
			}
		}

		var sibling = index;
		var oldParent = nodes[sibling].Parent;
		var newParent = AllocateNode();
		ref var parent = ref nodes[newParent];
		parent.Parent = oldParent;
		parent.Min = V3.Min( min, nodes[sibling].Min );
		parent.Max = V3.Max( max, nodes[sibling].Max );
		parent.Height = nodes[sibling].Height + 1;
		parent.Child1 = sibling;
		parent.Child2 = leaf;
		if ( oldParent != Null )
		{
			if ( nodes[oldParent].Child1 == sibling ) nodes[oldParent].Child1 = newParent;
			else nodes[oldParent].Child2 = newParent;
		}
		else root = newParent;
		nodes[sibling].Parent = newParent;
		nodes[leaf].Parent = newParent;
		Refit( newParent );
	}

	void RemoveLeaf( int leaf )
	{
		if ( leaf == root ) { root = Null; return; }
		var parent = nodes[leaf].Parent;
		var grandParent = nodes[parent].Parent;
		var sibling = nodes[parent].Child1 == leaf ? nodes[parent].Child2 : nodes[parent].Child1;
		if ( grandParent != Null )
		{
			if ( nodes[grandParent].Child1 == parent ) nodes[grandParent].Child1 = sibling;
			else nodes[grandParent].Child2 = sibling;
			nodes[sibling].Parent = grandParent;
			FreeNode( parent );
			Refit( grandParent );
		}
		else
		{
			root = sibling;
			nodes[sibling].Parent = Null;
			FreeNode( parent );
		}
	}

	void Refit( int index )
	{
		while ( index != Null )
		{
			index = Balance( index );
			ref var node = ref nodes[index];
			ref var a = ref nodes[node.Child1];
			ref var b = ref nodes[node.Child2];
			node.Height = 1 + Math.Max( a.Height, b.Height );
			node.LeafCount = a.LeafCount + b.LeafCount;
			node.Min = V3.Min( a.Min, b.Min );
			node.Max = V3.Max( a.Max, b.Max );
			index = node.Parent;
		}
	}

	int Balance( int index )
	{
		ref var node = ref nodes[index];
		if ( node.IsLeaf || node.Height < 2 ) return index;
		var a = node.Child1;
		var b = node.Child2;
		var balance = nodes[b].Height - nodes[a].Height;
		if ( balance > 1 ) return Rotate( index, b, a, true );
		if ( balance < -1 ) return Rotate( index, a, b, false );
		return index;
	}

	int Rotate( int index, int rising, int kept, bool right )
	{
		ref var node = ref nodes[index];
		ref var up = ref nodes[rising];
		var f = up.Child1;
		var g = up.Child2;
		up.Child1 = index;
		up.Parent = node.Parent;
		node.Parent = rising;
		if ( up.Parent != Null )
		{
			if ( nodes[up.Parent].Child1 == index ) nodes[up.Parent].Child1 = rising;
			else nodes[up.Parent].Child2 = rising;
		}
		else root = rising;
		var tallF = nodes[f].Height > nodes[g].Height;
		var stay = tallF ? f : g;
		var moved = tallF ? g : f;
		up.Child2 = stay;
		if ( right ) node.Child2 = moved;
		else node.Child1 = moved;
		nodes[moved].Parent = index;
		ref var keep = ref nodes[kept];
		ref var move = ref nodes[moved];
		node.Min = V3.Min( keep.Min, move.Min );
		node.Max = V3.Max( keep.Max, move.Max );
		node.Height = 1 + Math.Max( keep.Height, move.Height );
		node.LeafCount = keep.LeafCount + move.LeafCount;
		return rising;
	}

	public void Query<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery
	{
		rejected = 0;
		if ( root == Null ) return;
		var capacity = Height + 1;
		var stack = HeuristicTree<TPolicy>.stack;
		if ( stack is null || stack.Length < capacity ) HeuristicTree<TPolicy>.stack = stack = new int[Math.Max( 64, capacity )];
		var count = 1;
		stack[0] = root << 1;
		while ( count > 0 )
		{
			var entry = stack[--count];
			ref var node = ref nodes[entry >> 1];
			var inside = (entry & 1) != 0;
			if ( !inside )
			{
				var containment = query.Test( node.Min, node.Max );
				if ( containment == Containment.Outside ) continue;
				inside = containment == Containment.Inside;
			}
			if ( query.Reject( node.Min, node.Max ) ) { rejected += node.LeafCount; continue; }
			if ( node.IsLeaf ) { results.Add( node.Item << 1 | (inside ? 1 : 0) ); continue; }
			var flag = inside ? 1 : 0;
			stack[count++] = node.Child1 << 1 | flag;
			stack[count++] = node.Child2 << 1 | flag;
		}
	}

	public void Validate()
	{
		if ( root == Null ) { if ( nodeCount != 0 ) throw new Exception( "Empty tree" ); return; }
		if ( nodes[root].Parent != Null || ValidateNode( root ) * 2 - 1 != nodeCount ) throw new Exception( "Root/count" );
	}

	int ValidateNode( int index )
	{
		ref var node = ref nodes[index];
		if ( node.IsLeaf )
		{
			if ( node.Height != 0 || node.LeafCount != 1 || node.Child2 != Null ) throw new Exception( "Leaf" );
			return 1;
		}
		ref var a = ref nodes[node.Child1];
		ref var b = ref nodes[node.Child2];
		if ( a.Parent != index || b.Parent != index || node.Height != 1 + Math.Max( a.Height, b.Height )
			|| node.LeafCount != a.LeafCount + b.LeafCount || node.Min != V3.Min( a.Min, b.Min ) || node.Max != V3.Max( a.Max, b.Max ) )
			throw new Exception( "Branch" );
		return ValidateNode( node.Child1 ) + ValidateNode( node.Child2 );
	}
}
