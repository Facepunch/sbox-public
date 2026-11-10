using System;
using System.Collections.Generic;
using Sandbox.SceneRenderer.Culling;
using V3 = System.Numerics.Vector3;

/// <summary>
/// Benchmark candidate: SpatialTree's storage, padding and traversal with Box3D-style insertion and rotations.
/// See src/box3d/src/dynamic_tree.cpp (MIT, Copyright 2025 Erin Catto).
/// Tests both insert-only rotations and rotations on every change, without a periodic rebuild pass.
/// </summary>
internal sealed class Box3DTree
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

	readonly bool rotateChanges;
	public Box3DTree( bool rotateChanges = true )
	{
		this.rotateChanges = rotateChanges;
		BuildFreeList( 0 );
	}

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
		InsertLeaf( leaf, true );
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
		InsertLeaf( leaf, rotateChanges );
		return true;
	}

	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
	static bool Contains( in V3 outerMin, in V3 outerMax, in V3 min, in V3 max ) =>
		min.X >= outerMin.X && min.Y >= outerMin.Y && min.Z >= outerMin.Z
		&& max.X <= outerMax.X && max.Y <= outerMax.Y && max.Z <= outerMax.Z;

	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
	static float Area( in V3 min, in V3 max )
	{
		var d = max - min;
		return 2.0f * (d.X * d.Y + d.Y * d.Z + d.Z * d.X);
	}

	int FindSibling( in V3 min, in V3 max )
	{
		var leafArea = Area( min, max );
		var center = (min + max) * 0.5f;
		var index = root;
		var best = index;
		var direct = Area( V3.Min( nodes[index].Min, min ), V3.Max( nodes[index].Max, max ) );
		var bestCost = direct;
		var inherited = 0.0f;
		var area = Area( nodes[index].Min, nodes[index].Max );

		while ( !nodes[index].IsLeaf )
		{
			if ( direct + inherited < bestCost )
			{
				best = index;
				bestCost = direct + inherited;
			}
			inherited += direct - area;
			var child1 = nodes[index].Child1;
			var child2 = nodes[index].Child2;
			var lower1 = SiblingCost( child1, min, max, leafArea, inherited, ref best, ref bestCost, out var area1, out var direct1 );
			var lower2 = SiblingCost( child2, min, max, leafArea, inherited, ref best, ref bestCost, out var area2, out var direct2 );
			if ( bestCost <= lower1 && bestCost <= lower2 ) break;

			if ( lower1 == lower2 )
			{
				lower1 = V3.DistanceSquared( (nodes[child1].Min + nodes[child1].Max) * 0.5f, center );
				lower2 = V3.DistanceSquared( (nodes[child2].Min + nodes[child2].Max) * 0.5f, center );
			}
			var first = lower1 < lower2;
			index = first ? child1 : child2;
			area = first ? area1 : area2;
			direct = first ? direct1 : direct2;
		}
		return best;
	}

	float SiblingCost( int child, in V3 min, in V3 max, float leafArea, float inherited,
		ref int best, ref float bestCost, out float area, out float direct )
	{
		ref var node = ref nodes[child];
		direct = Area( V3.Min( node.Min, min ), V3.Max( node.Max, max ) );
		area = 0;
		if ( node.IsLeaf )
		{
			if ( direct + inherited < bestCost )
			{
				best = child;
				bestCost = direct + inherited;
			}
			return float.MaxValue;
		}
		area = Area( node.Min, node.Max );
		return inherited + direct + MathF.Min( leafArea - area, 0 );
	}

	void InsertLeaf( int leaf, bool rotate )
	{
		if ( root == Null )
		{
			root = leaf;
			nodes[root].Parent = Null;
			return;
		}
		var leafMin = nodes[leaf].Min;
		var leafMax = nodes[leaf].Max;
		var sibling = FindSibling( leafMin, leafMax );
		var oldParent = nodes[sibling].Parent;
		var newParent = AllocateNode();
		ref var parent = ref nodes[newParent];
		parent.Parent = oldParent;
		parent.Min = V3.Min( leafMin, nodes[sibling].Min );
		parent.Max = V3.Max( leafMax, nodes[sibling].Max );
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
		Refit( newParent, rotate );
	}

	void RemoveLeaf( int leaf )
	{
		if ( leaf == root )
		{
			root = Null;
			return;
		}
		var parent = nodes[leaf].Parent;
		var grandParent = nodes[parent].Parent;
		var sibling = nodes[parent].Child1 == leaf ? nodes[parent].Child2 : nodes[parent].Child1;
		if ( grandParent != Null )
		{
			if ( nodes[grandParent].Child1 == parent ) nodes[grandParent].Child1 = sibling;
			else nodes[grandParent].Child2 = sibling;
			nodes[sibling].Parent = grandParent;
			FreeNode( parent );
			Refit( grandParent, rotateChanges );
		}
		else
		{
			root = sibling;
			nodes[sibling].Parent = Null;
			FreeNode( parent );
		}
	}

	void Update( int index )
	{
		ref var node = ref nodes[index];
		ref var a = ref nodes[node.Child1];
		ref var b = ref nodes[node.Child2];
		node.Min = V3.Min( a.Min, b.Min );
		node.Max = V3.Max( a.Max, b.Max );
		node.Height = 1 + Math.Max( a.Height, b.Height );
		node.LeafCount = a.LeafCount + b.LeafCount;
	}

	void Refit( int index, bool rotate )
	{
		while ( index != Null )
		{
			Update( index );
			if ( rotate ) Rotate( index );
			index = nodes[index].Parent;
		}
	}

	void Rotate( int index )
	{
		var a = nodes[index].Child1;
		var b = nodes[index].Child2;
		var bestDelta = 0.0f;
		var down = Null;
		var up = Null;
		if ( !nodes[b].IsLeaf ) FindRotation( a, b, ref bestDelta, ref down, ref up );
		if ( !nodes[a].IsLeaf ) FindRotation( b, a, ref bestDelta, ref down, ref up );
		if ( up == Null ) return;

		var parent = nodes[up].Parent;
		if ( nodes[index].Child1 == down ) nodes[index].Child1 = up;
		else nodes[index].Child2 = up;
		if ( nodes[parent].Child1 == up ) nodes[parent].Child1 = down;
		else nodes[parent].Child2 = down;
		nodes[up].Parent = index;
		nodes[down].Parent = parent;
		Update( parent );
		Update( index );
	}

	void FindRotation( int sibling, int branch, ref float bestDelta, ref int down, ref int up )
	{
		ref var node = ref nodes[branch];
		var area = Area( node.Min, node.Max );
		var child1 = node.Child1;
		var child2 = node.Child2;
		var delta1 = Area( V3.Min( nodes[sibling].Min, nodes[child2].Min ), V3.Max( nodes[sibling].Max, nodes[child2].Max ) ) - area;
		var delta2 = Area( V3.Min( nodes[sibling].Min, nodes[child1].Min ), V3.Max( nodes[sibling].Max, nodes[child1].Max ) ) - area;
		if ( delta1 < bestDelta ) { bestDelta = delta1; down = sibling; up = child1; }
		if ( delta2 < bestDelta ) { bestDelta = delta2; down = sibling; up = child2; }
	}

	public void QueryClassified<T>( ref T query, List<int> results, out int rejected ) where T : struct, ISpatialQuery
	{
		rejected = 0;
		if ( root == Null ) return;
		var capacity = Height + 1;
		var stack = Box3DTree.stack;
		if ( stack is null || stack.Length < capacity ) Box3DTree.stack = stack = new int[Math.Max( 64, capacity )];
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
