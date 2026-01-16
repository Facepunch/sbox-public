namespace Sandbox;

internal sealed class VertexKDTree
{
	class Node
	{
		public int[] Children = { -1, -1 };
		public int Axis = -1;
		public float Split;
		public int LeafStart, LeafCount;

		public bool IsLeaf => Axis == -1;
		public void InitAsSplit( float split, int axis ) => (Axis, Split) = (axis, split);
		public void InitAsLeaf( int start, int count ) => (Axis, LeafStart, LeafCount) = (-1, start, count);
	}

	readonly List<Node> _tree = [];
	IReadOnlyList<Vector3> _positions;
	int[] _refs;

	public void BuildMidpoint( IReadOnlyList<Vector3> vertices )
	{
		_positions = vertices;
		_refs = new int[vertices.Count];
		for ( int i = 0; i < _refs.Length; i++ ) _refs[i] = i;

		_tree.Clear();
		BuildNode( 0, _refs.Length );
	}

	int BuildNode( int start, int count )
	{
		if ( count <= 8 )
		{
			var nodeIndex = _tree.Count;
			_tree.Add( new Node() );
			_tree[nodeIndex].InitAsLeaf( start, count );
			return nodeIndex;
		}

		ComputeBounds( out var min, out var max, start, count );
		var axis = GreatestAxis( max - min );
		var split = (max[axis] + min[axis]) * 0.5f;
		var splitIndex = FindMidpointIndex( start, count, axis, split );

		if ( splitIndex == start || splitIndex == start + count )
		{
			var nodeIndex = _tree.Count;
			_tree.Add( new Node() );
			_tree[nodeIndex].InitAsLeaf( start, count );
			return nodeIndex;
		}

		var idx = _tree.Count;
		_tree.Add( new Node { Axis = axis, Split = split } );
		_tree[idx].Children[0] = BuildNode( start, splitIndex - start );
		_tree[idx].Children[1] = BuildNode( splitIndex, count - (splitIndex - start) );
		return idx;
	}

	int FindMidpointIndex( int start, int count, int axis, float split )
	{
		var mid = start + count / 2;
		var end = start + count;

		for ( var i = mid; i < end; i++ )
		{
			if ( _positions[_refs[i]][axis] < split )
			{
				(_refs[mid], _refs[i]) = (_refs[i], _refs[mid]);
				mid++;
			}
		}

		for ( var i = mid - 1; i >= start; i-- )
		{
			if ( _positions[_refs[i]][axis] >= split )
			{
				(_refs[mid - 1], _refs[i]) = (_refs[i], _refs[mid - 1]);
				mid--;
			}
		}

		return mid;
	}

	void ComputeBounds( out Vector3 min, out Vector3 max, int start, int count )
	{
		min = max = _positions[_refs[start]];
		for ( var i = start + 1; i < start + count; i++ )
		{
			Vector3 p = _positions[_refs[i]];
			min = Vector3.Min( min, p );
			max = Vector3.Max( max, p );
		}
	}

	static int GreatestAxis( Vector3 v ) => v.x >= v.y ? (v.x > v.z ? 0 : 2) : (v.y > v.z ? 1 : 2);

	public List<int> FindVertsInBox( Vector3 minBounds, Vector3 maxBounds )
	{
		var result = new List<int>();
		FindVertsInBoxRecursive( 0, minBounds, maxBounds, result );
		return result;
	}

	void FindVertsInBoxRecursive( int nodeIndex, Vector3 minBounds, Vector3 maxBounds, List<int> result )
	{
		if ( nodeIndex < 0 || nodeIndex >= _tree.Count ) return;

		var node = _tree[nodeIndex];

		if ( node.IsLeaf )
		{
			for ( var i = node.LeafStart; i < node.LeafStart + node.LeafCount; i++ )
			{
				var idx = _refs[i];
				var p = _positions[idx];

				if ( p.x >= minBounds.x && p.x <= maxBounds.x &&
					 p.y >= minBounds.y && p.y <= maxBounds.y &&
					 p.z >= minBounds.z && p.z <= maxBounds.z )
				{
					result.Add( idx );
				}
			}
		}
		else
		{
			var axis = node.Axis;
			if ( minBounds[axis] <= node.Split ) FindVertsInBoxRecursive( node.Children[0], minBounds, maxBounds, result );
			if ( maxBounds[axis] >= node.Split ) FindVertsInBoxRecursive( node.Children[1], minBounds, maxBounds, result );
		}
	}
}
