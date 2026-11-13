namespace Sandbox;

public sealed class ToolRenderContext
{
	static Material LineMaterial => field ??= Material.Load( "materials/gizmo/line.vmat" );
	static Material PointMaterial => field ??= Material.Load( "materials/gizmo/sprite.vmat" );
	static Material ScreenLineMaterial => field ??= Material.Load( "materials/dev/debug_wireframe_2d.vmat" );
	readonly SceneWorld _world;
	readonly List<Batch> _batches = [];
	Transform _transform;
	bool _enabled;
	int _cursor;

	internal int Version { get; set; }
	internal bool Dirty { get; set; } = true;
	public ToolRenderView View { get; private set; }
	public Color Color { get; set; }
	public float LineThickness { get; set; }
	internal bool IgnoreDepth { get; set; }
	public int RenderOrder { get; set; }
	internal Component Component { get; set; }
	public string Tag { get; set; }

	internal ToolRenderContext( SceneWorld world ) => _world = world;

	internal void Begin( Transform transform, bool enabled, ToolRenderView view )
	{
		_transform = transform;
		_enabled = enabled;
		View = view;
		_cursor = 0;
		Color = Color.White;
		LineThickness = 1;
		IgnoreDepth = true;
		RenderOrder = 0;
		Component = null;
		Tag = null;
	}

	internal void End()
	{
		for ( var i = _cursor; i < _batches.Count; ++i )
			_batches[i].Dispose();
		_batches.RemoveRange( _cursor, _batches.Count - _cursor );
	}

	internal void SetTransform( Transform transform )
	{
		_transform = transform;
		foreach ( var batch in _batches )
		{
			if ( batch.Kind == Primitive.Points )
				Dirty = true;
			else if ( batch.Kind == Primitive.Lines && batch.Object.IsValid() )
				batch.Object.Transform = transform;
		}
	}

	internal void SetEnabled( bool enabled )
	{
		_enabled = enabled;
		foreach ( var batch in _batches )
		{
			if ( batch.Object.IsValid() )
				batch.Object.RenderingEnabled = enabled;
		}
	}

	public void DrawLines( ReadOnlySpan<Vector3> positions ) => Draw( positions, default, Primitive.Lines, LineThickness );

	internal void DrawIndexedLines( ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices )
	{
		if ( !indices.IsEmpty )
			Draw( positions, indices, Primitive.Lines, LineThickness );
	}

	public void DrawPoints( ReadOnlySpan<Vector3> positions, float size = 8 ) => Draw( positions, default, Primitive.Points, size );

	public void DrawScreenLines( ReadOnlySpan<Vector2> positions )
	{
		if ( positions.IsEmpty )
			return;
		if ( positions.Length % 2 != 0 )
			throw new ArgumentException( "Lines require pairs of vertices.", nameof( positions ) );
		if ( View.Size.x <= 0 || View.Size.y <= 0 )
			throw new InvalidOperationException( "Screen drawing requires a view with a positive size." );

		if ( _cursor == _batches.Count )
			_batches.Add( new Batch( _world ) );
		var batch = _batches[_cursor++];
		var vertexCount = LineCapacity( batch, positions.Length );
		using var buffer = new PooledSpan<ScreenVertex>( vertexCount );
		var vertices = buffer.Span;
		var scale = new Vector2( 2 / View.Size.x, -2 / View.Size.y );
		var offset = new Vector2( -1, 1 );
		Color32 color = Color;
		for ( var i = 0; i < positions.Length; i += 2 )
		{
			var start = positions[i] * scale + offset;
			var end = positions[i + 1] * scale + offset;
			var first = new Vector4( start.x, start.y, 0.5f, 1 );
			vertices[i] = new ScreenVertex
			{
				Position = first,
				Color = color,
				FirstLinePosition = first
			};
			vertices[i + 1] = vertices[i] with { Position = new Vector4( end.x, end.y, 0.5f, 1 ) };
		}
		vertices[positions.Length..].Clear();
		Submit( batch, vertices, default, Primitive.ScreenLines, positions.Length, BBox.FromPositionAndSize( 0, float.MaxValue ) );
	}

	static int LineCapacity( Batch batch, int count )
	{
		const int maxSpareVertices = 1024;
		var capacity = batch.Object.IsValid() ? batch.Object.VertexCount : 0;
		return count <= capacity && capacity - count <= maxSpareVertices
			? capacity
			: checked(count + (Math.Min( count / 4, maxSpareVertices / 2 ) & ~1));
	}

	void Draw( ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, Primitive kind, float size )
	{
		if ( !indices.IsEmpty && positions.IsEmpty )
			throw new ArgumentException( "Indices require vertex positions.", nameof( positions ) );
		if ( positions.IsEmpty )
			return;
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( size );

		var points = kind == Primitive.Points;
		var count = indices.IsEmpty ? positions.Length : indices.Length;
		if ( !points && count % 2 != 0 )
			throw new ArgumentException( "Lines require pairs of vertices or indices." );

		if ( _cursor == _batches.Count )
			_batches.Add( new Batch( _world ) );
		var batch = _batches[_cursor++];
		var vertexCount = positions.Length;
		if ( !points && indices.IsEmpty )
			vertexCount = LineCapacity( batch, vertexCount );

		using var buffer = new PooledSpan<ToolVertex>( vertexCount );
		var vertices = buffer.Span;
		var vertex = new ToolVertex
		{
			Color = Color,
			Normal = Vector3.Up,
			TexCoord = new Vector4( size, size, 0, 0 )
		};
		var bounds = points ? BBox.FromPositionAndSize( 0, float.MaxValue ) : new BBox( positions[0], positions[0] );
		for ( var i = 0; i < positions.Length; ++i )
		{
			if ( !points )
			{
				if ( !positions[i].IsFinite )
					throw new ArgumentException( "Line positions must be finite.", nameof( positions ) );
				bounds = bounds.AddPoint( positions[i] );
			}
			vertices[i] = vertex with
			{
				Position = points ? _transform.PointToWorld( positions[i] ) : positions[i]
			};
		}
		vertices[positions.Length..].Clear();
		Submit( batch, vertices, indices, kind, count, bounds );
		batch.Object.Attributes.Set( "LineThickness", size );
		if ( points )
			batch.Object.Attributes.Set( "TextureColor", Texture.White );
	}

	void Submit<T>( Batch batch, Span<T> vertices, ReadOnlySpan<int> indices, Primitive kind, int count, BBox bounds ) where T : unmanaged
	{
		batch.Upload( vertices, indices, kind, bounds );
		var sceneObject = batch.Object;
		var screenSpace = kind == Primitive.ScreenLines;
		var opaque = kind == Primitive.Lines && !IgnoreDepth;
		sceneObject.SetIndexRange( 0, count );
		sceneObject.Transform = kind == Primitive.Lines ? _transform : Transform.Zero;
		sceneObject.RenderLayer = screenSpace ? SceneRenderLayer.ToolScreenOverlay : SceneRenderLayer.ToolOverlay;
		sceneObject.Component = Component;
		sceneObject.Tags.RemoveAll();
		if ( !string.IsNullOrEmpty( Tag ) )
			sceneObject.Tags.Add( Tag );
		sceneObject.Flags.CastShadows = false;
		sceneObject.Flags.IsOpaque = opaque;
		sceneObject.Flags.IsTranslucent = !opaque;
		sceneObject.Attributes.SetCombo( "D_NO_ZTEST", screenSpace || IgnoreDepth ? 1 : 0 );
		sceneObject.Attributes.Set( "RenderOrder", RenderOrder );
		sceneObject.RenderingEnabled = _enabled;
	}

	internal void Clear()
	{
		foreach ( var batch in _batches )
			batch.Dispose();
		_batches.Clear();
	}

	sealed class Batch( SceneWorld world ) : IDisposable
	{
		public SceneMeshObject Object { get; private set; }
		public Primitive Kind { get; private set; }
		int[] _indices = [];
		bool _indexed;

		public void Upload<T>( Span<T> vertices, ReadOnlySpan<int> indices, Primitive kind, BBox bounds ) where T : unmanaged
		{
			var indexed = !indices.IsEmpty;
			var indexCount = indexed ? indices.Length : vertices.Length;
			var recreate = !Object.IsValid() || Object.VertexCount != vertices.Length
				|| Object.IndexCount != indexCount || Kind != kind;
			var indicesChanged = _indexed != indexed || _indices.Length != indexCount
				|| (indexed && !indices.SequenceEqual( _indices ));
			if ( indicesChanged )
			{
				_indices = indexed ? indices.ToArray() : new int[indexCount];
				if ( !indexed )
				{
					for ( var i = 0; i < indexCount; ++i )
						_indices[i] = i;
				}
			}

			if ( recreate )
			{
				DeleteObject();
				var material = kind switch
				{
					Primitive.Lines => LineMaterial,
					Primitive.Points => PointMaterial,
					Primitive.ScreenLines => ScreenLineMaterial,
					_ => throw new ArgumentOutOfRangeException( nameof( kind ) )
				};
				Object = SceneMeshObject.Create( world, material,
					kind == Primitive.Points ? Graphics.PrimitiveType.Points : Graphics.PrimitiveType.Lines,
					vertices, _indices.AsSpan(), bounds );
			}
			else
			{
				Object.SetVertexBufferData( vertices );
				if ( indicesChanged )
					Object.SetIndexBufferData( _indices );
				Object.LocalBounds = bounds;
			}
			Kind = kind;
			_indexed = indexed;
		}

		void DeleteObject()
		{
			if ( world.IsValid() && Object.IsValid() )
			{
				Object.RenderingEnabled = false;
				Object.Delete();
			}
			Object = null;
		}

		public void Dispose()
		{
			DeleteObject();
			_indices = [];
		}
	}

	enum Primitive
	{
		Lines,
		Points,
		ScreenLines
	}

	struct ToolVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Color] public Color32 Color;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.TexCoord] public Vector4 TexCoord;
	}

	struct ScreenVertex
	{
		[VertexLayout.Position] public Vector4 Position;
		[VertexLayout.Color] public Color32 Color;
		[VertexLayout.TexCoord( 1 )] public Vector4 FirstLinePosition;
	}
}
