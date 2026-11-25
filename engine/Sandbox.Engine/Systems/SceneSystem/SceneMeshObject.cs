namespace Sandbox;

/// <summary>
/// A scene object drawing its own vertex and index buffers with one material (the editor's tool render context: gizmo lines
/// and points). Managed only: its buffers are <see cref="GpuBuffer"/>s, and Borealis draws them as a custom object draws.
/// </summary>
internal sealed class SceneMeshObject : SceneCustomObject
{
	GpuBuffer _vertices;
	GpuBuffer _indices;
	Action<SceneMeshObject, RenderAttributes> _draw;
	Material _material;
	Graphics.PrimitiveType _primitiveType;
	BBox _localBounds;
	int _indexStart;
	int _indexCount;

	public int VertexCount => _vertices?.ElementCount ?? 0;
	public int IndexCount => _indices?.ElementCount ?? 0;

	public new BBox LocalBounds
	{
		get => _localBounds;
		set
		{
			_localBounds = value;
			Bounds = value.Transform( Transform );
		}
	}

	SceneMeshObject( SceneWorld world ) : base( world )
	{
	}

	public static SceneMeshObject Create<T>( SceneWorld world, Material material, Graphics.PrimitiveType primitiveType, Span<T> vertices, Span<int> indices, BBox bounds ) where T : unmanaged
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( material );
		if ( !world.IsValid() )
			throw new ArgumentException( "Scene world is not valid.", nameof( world ) );
		SceneDynamicObject.ThrowIfUndefined( primitiveType );
		ArgumentOutOfRangeException.ThrowIfZero( vertices.Length );
		ArgumentOutOfRangeException.ThrowIfZero( indices.Length );
		ValidateIndices( indices, vertices.Length );

		var vertexBuffer = new GpuBuffer<T>( vertices.Length, GpuBuffer.UsageFlags.Vertex, "SceneMeshObject vertices" );
		var indexBuffer = new GpuBuffer<int>( indices.Length, GpuBuffer.UsageFlags.Index, "SceneMeshObject indices" );
		vertexBuffer.SetData( vertices );
		indexBuffer.SetData( indices );

		var result = new SceneMeshObject( world )
		{
			_vertices = vertexBuffer,
			_indices = indexBuffer,
			_material = material,
			_primitiveType = primitiveType,
			_indexCount = indices.Length,
			_draw = static ( o, attributes ) => Graphics.Draw( (GpuBuffer<T>)o._vertices, o._indices, o._material, o._indexStart, o._indexCount, attributes, o._primitiveType ),
		};

		// As native's buffer object: placed at the origin, the bounds it was made with
		result.LocalBounds = bounds;
		return result;
	}

	public void SetVertexBufferData<T>( Span<T> vertices ) where T : unmanaged
	{
		ThreadSafe.AssertIsMainThread();
		if ( !this.IsValid() || _vertices is not GpuBuffer<T> buffer )
			throw new InvalidOperationException( "Scene object has been deleted." );
		if ( vertices.Length != VertexCount )
			throw new ArgumentException( "Vertex count must match the existing buffer.", nameof( vertices ) );

		buffer.SetData( vertices );
	}

	public void SetIndexBufferData( Span<int> indices )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !this.IsValid() || _indices is null )
			throw new InvalidOperationException( "Scene object has been deleted." );
		if ( indices.Length != IndexCount )
			throw new ArgumentException( "Index count must match the existing buffer.", nameof( indices ) );

		ValidateIndices( indices, VertexCount );
		_indices.SetData( (ReadOnlySpan<int>)indices );
	}

	public void SetIndexRange( int start, int count )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !this.IsValid() || _indices is null )
			throw new InvalidOperationException( "Scene object has no buffer geometry." );
		ArgumentOutOfRangeException.ThrowIfNegative( start );
		ArgumentOutOfRangeException.ThrowIfNegative( count );
		if ( (long)start + count > IndexCount )
			throw new ArgumentOutOfRangeException( nameof( count ), "Index range is outside the buffer." );

		_indexStart = start;
		_indexCount = count;
	}

	/// <summary>Draw the buffers with the object's own attributes (a tool overlay's depth test and order).</summary>
	public override void RenderSceneObject()
	{
		if ( _draw is null || _indexCount <= 0 || !_material.IsValid() ) return;
		_draw( this, CreatedAttributes?.ManagedSet ?? Graphics.Attributes );
	}

	internal override void OnTransformChanged( in Transform transform )
	{
		base.OnTransformChanged( transform );
		Bounds = _localBounds.Transform( transform );
	}

	internal override void Destroyed()
	{
		base.Destroyed();

		// The renderer has let go of the object by now; the buffers go with it
		_vertices?.Dispose();
		_indices?.Dispose();
		_vertices = null;
		_indices = null;
		_draw = null;
	}

	static void ValidateIndices( ReadOnlySpan<int> indices, int vertexCount )
	{
		var invalid = indices.IndexOfAnyExceptInRange( 0, vertexCount - 1 );
		if ( invalid >= 0 )
			throw new ArgumentOutOfRangeException( nameof( indices ), indices[invalid], "Index is outside the vertex buffer." );
	}
}
