using System.Runtime.CompilerServices;

namespace Sandbox.SceneRenderer.Culling;

/// <summary>
/// Dynamic AABB tree based on Box2D 2.4's <c>b2DynamicTree</c>, using 3D surface area.
/// Queries return candidates with padded bounds; callers test exact bounds where needed.
/// Queries may run concurrently, but mutations must be exclusive.
/// </summary>
internal sealed partial class SpatialTree
{
	const int Null = -1;

	/// <summary>
	/// Array-backed leaf or branch. Free nodes link through <see cref="Parent"/>.
	/// </summary>
	struct Node
	{
		public Vector3 Min, Max;
		public int Parent;
		public int Child1, Child2;
		public int Height; // Leaves are 0; free nodes are -1.
		public int LeafCount;

		public int Item;

		public readonly bool IsLeaf => Child1 == Null;
	}

	Node[] nodes = new Node[64];
	int root = Null;
	int freeList = Null;
	int nodeCount;

	public SpatialTree()
	{
		BuildFreeList( 0 );
	}

	/// <summary>
	/// Nodes in use, leaves and branches.
	/// </summary>
	public int NodeCount => nodeCount;

	/// <summary>
	/// Longest path from the root to a leaf.
	/// </summary>
	public int Height => root == Null ? 0 : nodes[root].Height;

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

	static Vector3 Margin( in Vector3 min, in Vector3 max ) => Vector3.Max( (max - min) * 0.1f, Vector3.One );

	/// <summary>
	/// Add bounds and return the leaf handle for moving or removal.
	/// </summary>
	public int Add( in Vector3 min, in Vector3 max, int item )
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

	/// <summary>
	/// Reinsert when bounds escape their padding or the old bounds have become too large.
	/// </summary>
	public bool Move( int leaf, in Vector3 min, in Vector3 max )
	{
		ref var node = ref nodes[leaf];
		var margin = Margin( min, max );
		var fatMin = min - margin;
		var fatMax = max + margin;

		// Box2D shrink hysteresis avoids retaining oversized leaves.
		if ( Contains( node.Min, node.Max, min, max )
			&& Contains( fatMin - 4.0f * margin, fatMax + 4.0f * margin, node.Min, node.Max ) )
			return false;

		RemoveLeaf( leaf );
		node.Min = fatMin;
		node.Max = fatMax;
		InsertLeaf( leaf );
		return true;
	}

	/// <summary>
	/// Update a leaf's item after world-index renumbering.
	/// </summary>
	public void SetItem( int leaf, int item ) => nodes[leaf].Item = item;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static bool Contains( in Vector3 outerMin, in Vector3 outerMax, in Vector3 min, in Vector3 max ) =>
		min.x >= outerMin.x && min.y >= outerMin.y && min.z >= outerMin.z
		&& max.x <= outerMax.x && max.y <= outerMax.y && max.z <= outerMax.z;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static float Area( in Vector3 min, in Vector3 max )
	{
		var size = max - min;
		return 2.0f * (size.x * size.y + size.y * size.z + size.z * size.x);
	}

	void InsertLeaf( int leaf )
	{
		if ( root == Null )
		{
			root = leaf;
			nodes[root].Parent = Null;
			return;
		}

		var leafMin = nodes[leaf].Min;
		var leafMax = nodes[leaf].Max;
		var index = root;

		while ( !nodes[index].IsLeaf )
		{
			ref var node = ref nodes[index];
			var child1 = node.Child1;
			var child2 = node.Child2;

			var area = Area( node.Min, node.Max );
			var combinedArea = Area( Vector3.Min( node.Min, leafMin ), Vector3.Max( node.Max, leafMax ) );

			var cost = 2.0f * combinedArea;
			var inheritance = 2.0f * (combinedArea - area);

			var cost1 = ChildCost( child1, leafMin, leafMax ) + inheritance;
			var cost2 = ChildCost( child2, leafMin, leafMax ) + inheritance;

			if ( cost < cost1 && cost < cost2 ) break;

			index = cost1 < cost2 ? child1 : child2;
		}

		var sibling = index;

		var oldParent = nodes[sibling].Parent;
		var newParent = AllocateNode();

		ref var parent = ref nodes[newParent];
		parent.Parent = oldParent;
		parent.Min = Vector3.Min( leafMin, nodes[sibling].Min );
		parent.Max = Vector3.Max( leafMax, nodes[sibling].Max );
		parent.Height = nodes[sibling].Height + 1;
		parent.Child1 = sibling;
		parent.Child2 = leaf;

		if ( oldParent != Null )
		{
			if ( nodes[oldParent].Child1 == sibling ) nodes[oldParent].Child1 = newParent;
			else nodes[oldParent].Child2 = newParent;
		}
		else
		{
			root = newParent;
		}

		nodes[sibling].Parent = newParent;
		nodes[leaf].Parent = newParent;

		Refit( nodes[leaf].Parent );
	}

	float ChildCost( int child, in Vector3 leafMin, in Vector3 leafMax )
	{
		ref var node = ref nodes[child];
		var combined = Area( Vector3.Min( node.Min, leafMin ), Vector3.Max( node.Max, leafMax ) );
		return node.IsLeaf ? combined : combined - Area( node.Min, node.Max );
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
			ref var child1 = ref nodes[node.Child1];
			ref var child2 = ref nodes[node.Child2];

			node.Height = 1 + Math.Max( child1.Height, child2.Height );
			node.LeafCount = child1.LeafCount + child2.LeafCount;
			node.Min = Vector3.Min( child1.Min, child2.Min );
			node.Max = Vector3.Max( child1.Max, child2.Max );

			index = node.Parent;
		}
	}

	int Balance( int iA )
	{
		ref var a = ref nodes[iA];
		if ( a.IsLeaf || a.Height < 2 ) return iA;

		var iB = a.Child1;
		var iC = a.Child2;
		var balance = nodes[iC].Height - nodes[iB].Height;

		if ( balance > 1 ) return Rotate( iA, iC, iB, true );
		if ( balance < -1 ) return Rotate( iA, iB, iC, false );
		return iA;
	}

	int Rotate( int iA, int iUp, int iKeep, bool upWasChild2 )
	{
		ref var a = ref nodes[iA];
		ref var up = ref nodes[iUp];

		var iF = up.Child1;
		var iG = up.Child2;

		up.Child1 = iA;
		up.Parent = a.Parent;
		a.Parent = iUp;

		if ( up.Parent != Null )
		{
			if ( nodes[up.Parent].Child1 == iA ) nodes[up.Parent].Child1 = iUp;
			else nodes[up.Parent].Child2 = iUp;
		}
		else
		{
			root = iUp;
		}

		// The taller grandchild stays with the rising node, the shorter moves under A
		var tallF = nodes[iF].Height > nodes[iG].Height;
		var iStay = tallF ? iF : iG;
		var iMove = tallF ? iG : iF;

		up.Child2 = iStay;
		if ( upWasChild2 ) a.Child2 = iMove;
		else a.Child1 = iMove;
		nodes[iMove].Parent = iA;

		ref var keep = ref nodes[iKeep];
		ref var move = ref nodes[iMove];

		a.Min = Vector3.Min( keep.Min, move.Min );
		a.Max = Vector3.Max( keep.Max, move.Max );
		a.Height = 1 + Math.Max( keep.Height, move.Height );
		a.LeafCount = keep.LeafCount + move.LeafCount;

		// Refit updates the rising node next.
		return iUp;
	}

	/// <summary>
	/// Validate parent links, heights and enclosing bounds for tests.
	/// </summary>
	internal void Validate()
	{
		if ( root == Null )
		{
			if ( nodeCount != 0 ) throw new InvalidOperationException( "Empty tree has live nodes" );
			return;
		}
		if ( nodes[root].Parent != Null ) throw new InvalidOperationException( "Root has a parent" );
		if ( 2 * ValidateNode( root ) - 1 != nodeCount ) throw new InvalidOperationException( "Tree has unreachable nodes" );
	}

	int ValidateNode( int index )
	{
		ref var node = ref nodes[index];
		if ( node.IsLeaf )
		{
			if ( node.Height != 0 ) throw new InvalidOperationException( $"Leaf {index} has height {node.Height}" );
			if ( node.LeafCount != 1 || node.Child2 != Null ) throw new InvalidOperationException( $"Leaf {index} has invalid children" );
			return 1;
		}

		ref var child1 = ref nodes[node.Child1];
		ref var child2 = ref nodes[node.Child2];
		if ( child1.Parent != index || child2.Parent != index ) throw new InvalidOperationException( $"Node {index}'s children don't point back" );

		var height = 1 + Math.Max( child1.Height, child2.Height );
		if ( node.Height != height ) throw new InvalidOperationException( $"Node {index} has height {node.Height}, should be {height}" );
		if ( node.LeafCount != child1.LeafCount + child2.LeafCount ) throw new InvalidOperationException( $"Node {index} has the wrong leaf count" );

		if ( !Vector3.Min( child1.Min, child2.Min ).Equals( node.Min ) || !Vector3.Max( child1.Max, child2.Max ).Equals( node.Max ) )
			throw new InvalidOperationException( $"Node {index}'s bounds don't fit its children" );

		return ValidateNode( node.Child1 ) + ValidateNode( node.Child2 );
	}
}
