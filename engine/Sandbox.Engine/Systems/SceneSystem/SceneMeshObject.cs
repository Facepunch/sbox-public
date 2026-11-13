using NativeEngine;

namespace Sandbox;

internal sealed class SceneMeshObject : SceneObject
{
	VertexBufferHandle _vertices;
	IndexBufferHandle _indices;
	BBox _localBounds;
	int _indexStart;
	int _indexCount;

	public int VertexCount => _vertices.ElementCount;
	public int IndexCount => _indices.ElementCount;

	public new BBox LocalBounds
	{
		get => _localBounds;
		set
		{
			_localBounds = value;
			Bounds = value.Transform( Transform );
		}
	}

	SceneMeshObject()
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

		var result = new SceneMeshObject { _localBounds = bounds, _indexCount = indices.Length };
		try
		{
			result._vertices = VertexBufferHandle.Create( vertices.Length, vertices );
			result._indices = new IndexBufferHandle( indices.Length, indices );
			using ( IHandle.MakeNextHandle( result ) )
			{
				CSceneSystem.CreateSceneObjectFromBuffers( world, result._vertices, result._indices, material.native,
					vertices.Length, indices.Length, bounds, (RenderPrimitiveType)primitiveType );
			}

			if ( result.native.IsNull )
				throw new InvalidOperationException( "Failed to create a mesh scene object." );

			return result;
		}
		finally
		{
			if ( result.native.IsNull )
			{
				if ( result._vertices.IsValid )
					g_pRenderDevice.DestroyVertexBuffer( result._vertices );
				if ( result._indices.IsValid )
					g_pRenderDevice.DestroyIndexBuffer( result._indices );
			}
		}
	}

	public void SetVertexBufferData<T>( Span<T> vertices ) where T : unmanaged
	{
		ThreadSafe.AssertIsMainThread();
		if ( native.IsNull || !_vertices.IsValid )
			throw new InvalidOperationException( "Scene object has been deleted." );
		if ( vertices.Length != VertexCount )
			throw new ArgumentException( "Vertex count must match the existing buffer.", nameof( vertices ) );

		_vertices.SetData( vertices );
	}

	public void SetIndexBufferData( Span<int> indices )
	{
		ThreadSafe.AssertIsMainThread();
		if ( native.IsNull || !_indices.IsValid )
			throw new InvalidOperationException( "Scene object has been deleted." );
		if ( indices.Length != IndexCount )
			throw new ArgumentException( "Index count must match the existing buffer.", nameof( indices ) );

		ValidateIndices( indices, VertexCount );
		_indices.SetData( indices );
	}

	public void SetIndexRange( int start, int count )
	{
		ThreadSafe.AssertIsMainThread();
		if ( native.IsNull || !_indices.IsValid )
			throw new InvalidOperationException( "Scene object has no buffer geometry." );
		ArgumentOutOfRangeException.ThrowIfNegative( start );
		ArgumentOutOfRangeException.ThrowIfNegative( count );
		if ( (long)start + count > IndexCount )
			throw new ArgumentOutOfRangeException( nameof( count ), "Index range is outside the buffer." );
		if ( _indexStart == start && _indexCount == count )
			return;

		native.SetBufferIndexRange( start, count );
		_indexStart = start;
		_indexCount = count;
	}

	internal override void OnTransformChanged( in Transform transform )
	{
		base.OnTransformChanged( transform );
		Bounds = _localBounds.Transform( transform );
	}

	internal override void OnModelChanged()
	{
		base.OnModelChanged();
		_vertices = default;
		_indices = default;
	}

	static void ValidateIndices( ReadOnlySpan<int> indices, int vertexCount )
	{
		var invalid = indices.IndexOfAnyExceptInRange( 0, vertexCount - 1 );
		if ( invalid >= 0 )
			throw new ArgumentOutOfRangeException( nameof( indices ), indices[invalid], "Index is outside the vertex buffer." );
	}
}
