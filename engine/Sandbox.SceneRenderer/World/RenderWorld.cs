namespace Sandbox.SceneRenderer;

/// <summary>
/// Render objects and dense bounds/transform arrays, equivalent to native's <c>CSceneWorld</c>.
/// Objects update arrays on change so culling avoids chasing references.
/// </summary>
public sealed class RenderWorld
{
	const int InitialCapacity = 64;

	RenderObject[] _objects = new RenderObject[InitialCapacity];
	Vector3[] _boundsCenters = new Vector3[InitialCapacity];
	Vector3[] _boundsExtents = new Vector3[InitialCapacity];
	Matrix[] _localToWorld = new Matrix[InitialCapacity];
	int[] _treeLeaves = new int[InitialCapacity];

	/// <summary>
	/// Spatial index of size-culled objects.
	/// </summary>
	internal SpatialTree Tree { get; } = new();

	/// <summary>
	/// Dynamic-only spatial index for baked-light shadows and cached lights' dynamic passes.
	/// </summary>
	internal SpatialTree DynamicTree { get; } = new();

	// Each object's leaf in DynamicTree, or NotAdded
	int[] _dynamicTreeLeaves = new int[InitialCapacity];

	/// <summary>
	/// Objects that are never size culled - lights - by index. Few, so views just test them all.
	/// </summary>
	readonly List<int> _unculledObjectIndices = [];

	internal ReadOnlySpan<int> Unculled => System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _unculledObjectIndices );

	readonly List<Type> _objectTypes = [];

	/// <summary>
	/// All types ever added, used to warm feature caches before parallel culling.
	/// </summary>
	internal ReadOnlySpan<Type> ObjectTypes => System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _objectTypes );

	// A leaf in the tree, or one of these
	const int NotAdded = -1;
	const int NotInTree = -2;

	/// <summary>
	/// The sun and ambient light the world is drawn with.
	/// </summary>
	public SceneLighting Lighting { get; } = new();

	/// <summary>
	/// Optional 3D skybox; null uses this world's 2D sky.
	/// </summary>
	public Skybox3D Skybox3D { get; set; }

	/// <summary>
	/// Probe volumes for <see cref="MeshObject.NeedsLightProbe"/> objects. Insertion order breaks selection ties.
	/// </summary>
	public List<LightProbeVolume> LightProbeVolumes { get; } = [];

	/// <summary>
	/// Fog volumes in native order, which determines tint in overlaps. Requires enabled volumetric fog.
	/// </summary>
	public List<FogVolume> FogVolumes { get; } = [];

	/// <summary>
	/// Number of objects in the world.
	/// </summary>
	public int Count { get; private set; }

	internal ReadOnlySpan<RenderObject> Objects => _objects.AsSpan( 0, Count );
	internal ReadOnlySpan<Vector3> BoundsCenter => _boundsCenters.AsSpan( 0, Count );
	internal ReadOnlySpan<Vector3> BoundsExtents => _boundsExtents.AsSpan( 0, Count );
	internal ReadOnlySpan<Matrix> LocalToWorld => _localToWorld.AsSpan( 0, Count );

	/// <summary>
	/// Add an object. An object can be in one world at a time.
	/// </summary>
	public void Add( RenderObject obj )
	{
		ArgumentNullException.ThrowIfNull( obj );
		if ( obj.World is not null ) throw new InvalidOperationException( "Render object is already in a world" );

		var type = obj.GetType();
		if ( !_objectTypes.Contains( type ) ) _objectTypes.Add( type );

		if ( Count == _objects.Length )
		{
			var capacity = _objects.Length * 2;
			Array.Resize( ref _objects, capacity );
			Array.Resize( ref _boundsCenters, capacity );
			Array.Resize( ref _boundsExtents, capacity );
			Array.Resize( ref _localToWorld, capacity );
			Array.Resize( ref _treeLeaves, capacity );
			Array.Resize( ref _dynamicTreeLeaves, capacity );
		}

		obj.World = this;
		obj.Index = Count++;
		_objects[obj.Index] = obj;
		_treeLeaves[obj.Index] = NotAdded;
		_dynamicTreeLeaves[obj.Index] = NotAdded;
		Sync( obj );
	}

	/// <summary>
	/// Remove an object. The last object moves into its slot, so indices aren't stable across removes.
	/// </summary>
	public void Remove( RenderObject obj )
	{
		ArgumentNullException.ThrowIfNull( obj );
		if ( obj.World != this ) return;

		var index = obj.Index;
		var last = --Count;

		if ( _treeLeaves[index] >= 0 ) Tree.Remove( _treeLeaves[index] );
		else _unculledObjectIndices.Remove( index );
		if ( _dynamicTreeLeaves[index] >= 0 ) DynamicTree.Remove( _dynamicTreeLeaves[index] );

		if ( index != last )
		{
			var moved = _objects[last];
			_objects[index] = moved;
			_boundsCenters[index] = _boundsCenters[last];
			_boundsExtents[index] = _boundsExtents[last];
			_localToWorld[index] = _localToWorld[last];
			_treeLeaves[index] = _treeLeaves[last];
			_dynamicTreeLeaves[index] = _dynamicTreeLeaves[last];
			moved.Index = index;

			if ( _treeLeaves[index] >= 0 ) Tree.SetItem( _treeLeaves[index], index );
			else _unculledObjectIndices[_unculledObjectIndices.IndexOf( last )] = index;
			if ( _dynamicTreeLeaves[index] >= 0 ) DynamicTree.SetItem( _dynamicTreeLeaves[index], index );
		}

		_objects[last] = null;
		obj.World = null;
		obj.Index = -1;
	}

	/// <summary>
	/// Copy an object's transform and bounds into the dense arrays. Objects call this when they change.
	/// </summary>
	internal void Sync( RenderObject obj )
	{
		var index = obj.Index;
		var matrix = ToMatrix( obj.Transform );
		_localToWorld[index] = matrix;

		// Transform centre/extents to avoid eight corner transforms.
		var local = obj.LocalBounds;
		var center = matrix.Transform( local.Center );
		var halfSize = local.Size * 0.5f;
		var extents = new Vector3(
			MathF.Abs( matrix.M11 ) * halfSize.x + MathF.Abs( matrix.M21 ) * halfSize.y + MathF.Abs( matrix.M31 ) * halfSize.z,
			MathF.Abs( matrix.M12 ) * halfSize.x + MathF.Abs( matrix.M22 ) * halfSize.y + MathF.Abs( matrix.M32 ) * halfSize.z,
			MathF.Abs( matrix.M13 ) * halfSize.x + MathF.Abs( matrix.M23 ) * halfSize.y + MathF.Abs( matrix.M33 ) * halfSize.z );

		_boundsCenters[index] = center;
		_boundsExtents[index] = extents;

		var leaf = _treeLeaves[index];
		if ( leaf >= 0 ) Tree.Move( leaf, center - extents, center + extents );
		else if ( leaf == NotAdded && obj.SizeCulled ) _treeLeaves[index] = Tree.Add( center - extents, center + extents, index );
		else if ( leaf == NotAdded )
		{
			_treeLeaves[index] = NotInTree;
			_unculledObjectIndices.Add( index );
		}

		SyncDynamic( obj );
	}

	/// <summary>
	/// Keep an object in <see cref="DynamicTree"/> while it's in <see cref="Tree"/> and not static, at its current bounds.
	/// </summary>
	internal void SyncDynamic( RenderObject obj )
	{
		var index = obj.Index;
		var leaf = _dynamicTreeLeaves[index];
		var wanted = _treeLeaves[index] >= 0 && !obj.IsStatic;

		if ( !wanted )
		{
			if ( leaf >= 0 ) DynamicTree.Remove( leaf );
			_dynamicTreeLeaves[index] = NotAdded;
			return;
		}

		var center = _boundsCenters[index];
		var extents = _boundsExtents[index];
		if ( leaf >= 0 ) DynamicTree.Move( leaf, center - extents, center + extents );
		else _dynamicTreeLeaves[index] = DynamicTree.Add( center - extents, center + extents, index );
	}

	internal static Matrix ToMatrix( in Transform transform ) => Matrix.FromTransform( transform );
}
