using System.Buffers;
using System.Runtime.InteropServices;

namespace Editor.MeshEditor;

sealed class MeshOverlayRenderer : IDisposable
{
	static Material LineMaterial => field ??= Material.Load( "materials/gizmo/line.vmat" );
	static Material PointMaterial => field ??= Material.Load( "materials/gizmo/sprite.vmat" );
	readonly Dictionary<(MeshComponent Component, SceneWorld World), MeshOverlay> _meshes = [];
	readonly Dictionary<MeshComponent, List<IMeshElement>> _selection = [];
	readonly List<(MeshComponent Component, SceneWorld World)> _remove = [];

	MeshOverlay GetOverlay( MeshComponent component, SceneWorld world )
	{
		var key = (component, world);
		if ( _meshes.TryGetValue( key, out var overlay ) )
			return overlay;

		overlay = new MeshOverlay( component, world );
		if ( _selection.TryGetValue( component, out var elements ) )
		{
			foreach ( var element in elements )
				overlay.AddSelection( element );
		}
		_meshes.Add( key, overlay );
		return overlay;
	}

	public void Draw( MeshComponent component, bool vertices, SceneWorld world )
	{
		if ( !component.IsValid() || component.Mesh is null )
			return;

		var overlay = GetOverlay( component, world );
		overlay.Update( component, vertices );
		overlay.Draw( vertices );
	}

	public void SetSelection( IEnumerable<IMeshElement> elements )
	{
		_selection.Clear();
		foreach ( var element in elements )
		{
			if ( !_selection.TryGetValue( element.Component, out var selected ) )
			{
				selected = [];
				_selection.Add( element.Component, selected );
			}
			selected.Add( element );
		}

		foreach ( var (key, overlay) in _meshes )
		{
			overlay.ClearSelection();
			if ( !_selection.TryGetValue( key.Component, out var selected ) )
				continue;
			foreach ( var element in selected )
				overlay.AddSelection( element );
		}
	}

	public void DrawSelected( MeshComponent component, Vector3 cameraPosition, SceneWorld world )
	{
		if ( !component.IsValid() || component.Mesh is null )
			return;

		var overlay = GetOverlay( component, world );
		overlay.Update( component, false, selectionOnly: true );
		overlay.DrawSelected( cameraPosition );
	}

	public void HideBase( SceneWorld world )
	{
		foreach ( var (key, overlay) in _meshes )
		{
			if ( key.World == world )
				overlay.HideBase();
		}
	}

	public void Trim( MeshComponent hover, IReadOnlySet<MeshComponent> selected, SceneWorld world )
	{
		_remove.Clear();
		foreach ( var key in _meshes.Keys )
		{
			if ( !key.Component.IsValid() || !key.World.IsValid()
				|| (key.World == world && key.Component != hover && !selected.Contains( key.Component )) )
				_remove.Add( key );
		}

		foreach ( var component in _remove )
		{
			_meshes[component].Dispose();
			_meshes.Remove( component );
		}
	}

	public void Dispose()
	{
		foreach ( var overlay in _meshes.Values )
			overlay.Dispose();

		_meshes.Clear();
		_selection.Clear();
		_remove.Clear();
	}

	sealed class MeshOverlay( MeshComponent component, SceneWorld world ) : IDisposable
	{
		SceneCustomObject _depthObject;
		SceneCustomObject _overlayObject;
		bool _depthVisible;
		bool _overlayVisible;
		readonly Batch _edges = new( false, new Color( 0.3137f, 0.7843f, 1, 1 ), 2, 0.1f );
		readonly Batch _vertices = new( true, new Color( 1, 1, 0.3f, 1 ), 8, 0.2f );
		readonly Batch _selectedVertices = new( true, Color.White, 8, 1, selected: true );
		readonly Batch _selectedEdges = new( false, Color.Yellow, 4, 1, selected: true );
		readonly Batch _selectedOpenEdges = new( false, Color.Yellow, 2, 1, selected: true );
		readonly Batch _hatches = new( false, Color.Yellow, 2, 1, selected: true, worldspace: true );
		readonly List<Vector3> _positions = [];
		readonly List<int> _indices = [];
		readonly List<HalfEdgeMesh.VertexHandle> _edgeVertices = [];
		readonly List<HalfEdgeMesh.VertexHandle> _visibleVertices = [];
		int[] _vertexIndices = [];
		readonly List<MeshVertex> _vertexSelection = [];
		readonly List<MeshEdge> _edgeSelection = [];
		public List<MeshEdge> OpenEdges { get; } = [];
		readonly List<Hatch> _hatchEdges = [];
		PolygonMesh _mesh;
		PolygonMesh _topologyMesh;
		int _topologyVersion;
		Model _model;
		bool _hasEdges;
		bool _hasVertices;
		bool _selectionDirty;
		bool _selectedVerticesDirty;
		bool _hatchesDirty;
		Transform _transform;
		Vector3 _cameraPosition;

		void EnsureObjects()
		{
			if ( !_depthObject.IsValid() )
			{
				_depthObject?.Delete();
				_overlayObject?.Delete();
				_depthObject = CreateObject( false );
				_overlayObject = CreateObject( true );
				_depthVisible = false;
				_overlayVisible = false;
			}
		}

		SceneCustomObject CreateObject( bool ignoreDepth )
		{
			return new SceneCustomObject( world )
			{
				RenderLayer = ignoreDepth ? SceneRenderLayer.OverlayWithoutDepth : SceneRenderLayer.OverlayWithDepth,
				RenderingEnabled = false,
				Flags = { CastShadows = false, IsOpaque = !ignoreDepth, IsTranslucent = ignoreDepth },
				RenderOverride = _ =>
				{
					_edges.Render( ignoreDepth );
					_vertices.Render( ignoreDepth );
					_selectedVertices.Render( ignoreDepth );
					_selectedEdges.Render( ignoreDepth );
					_selectedOpenEdges.Render( ignoreDepth );
					_hatches.Render( ignoreDepth );
				}
			};
		}

		void UpdateVisibility()
		{
			var depth = _edges.IsVisible( false ) || _vertices.IsVisible( false );
			var overlay = _edges.IsVisible( true ) || _vertices.IsVisible( true )
				|| _selectedVertices.IsVisible( true ) || _selectedEdges.IsVisible( true )
				|| _selectedOpenEdges.IsVisible( true ) || _hatches.IsVisible( true );
			if ( _depthObject.IsValid() && _depthVisible != depth )
			{
				_depthObject.RenderingEnabled = depth;
				_depthVisible = depth;
			}
			if ( _overlayObject.IsValid() && _overlayVisible != overlay )
			{
				_overlayObject.RenderingEnabled = overlay;
				_overlayVisible = overlay;
			}
		}

		public void ClearSelection()
		{
			_vertexSelection.Clear();
			_edgeSelection.Clear();
			OpenEdges.Clear();
			_selectionDirty = true;
			_selectedVerticesDirty = true;
			_selectedVertices.SetVisible( false );
			_selectedEdges.SetVisible( false );
			_selectedOpenEdges.SetVisible( false );
			_hatches.SetVisible( false );
			UpdateVisibility();
		}

		public void AddSelection( IMeshElement element )
		{
			if ( element is MeshVertex vertex )
				_vertexSelection.Add( vertex );
			else if ( element is MeshEdge edge )
				_edgeSelection.Add( edge );
		}

		public void Update( MeshComponent component, bool vertices, bool selectionOnly = false )
		{
			if ( _mesh != component.Mesh || _model != component.Model || component.Mesh.IsDirty )
			{
				_mesh = component.Mesh;
				_model = component.Model;
				_hasEdges = false;
				_hasVertices = false;
				_selectionDirty = true;
				_selectedVerticesDirty = true;
				_hatchesDirty = true;
			}

			if ( _transform != component.WorldTransform )
			{
				_transform = component.WorldTransform;
				_hasVertices = false;
				_selectedVerticesDirty = true;
				_hatchesDirty = true;
			}

			if ( selectionOnly )
				return;

			if ( _topologyMesh != _mesh || _topologyVersion != _mesh.TopologyVersion )
			{
				_hasEdges = false;
				_hasVertices = false;
				_edgeVertices.Clear();
				_edgeVertices.AddRange( _mesh.VertexHandles );
				_visibleVertices.Clear();
				for ( var i = 0; i < _edgeVertices.Count; ++i )
				{
					var vertex = _edgeVertices[i];
					if ( vertex.Index >= _vertexIndices.Length )
						Array.Resize( ref _vertexIndices, Math.Max( vertex.Index + 1, Math.Max( 16, _vertexIndices.Length * 2 ) ) );
					_vertexIndices[vertex.Index] = i;
					if ( !_mesh.IsVertexHidden( vertex ) )
						_visibleVertices.Add( vertex );
				}
				_indices.Clear();
				foreach ( var edge in _mesh.GetVisibleEdgeHandles() )
				{
					_mesh.GetEdgeVertices( edge, out var a, out var b );
					_indices.Add( _vertexIndices[a.Index] );
					_indices.Add( _vertexIndices[b.Index] );
				}
				_topologyMesh = _mesh;
				_topologyVersion = _mesh.TopologyVersion;
			}

			if ( !_hasEdges )
			{
				_positions.Clear();
				foreach ( var vertex in _edgeVertices )
					_positions.Add( _mesh.GetVertexPosition( vertex ) );
				_edges.Upload( _positions, _indices );
				_hasEdges = true;
			}

			if ( vertices && !_hasVertices )
			{
				_positions.Clear();
				foreach ( var vertex in _visibleVertices )
					_positions.Add( _transform.PointToWorld( _mesh.GetVertexPosition( vertex ) ) );
				_vertices.Upload( _positions );
				_hasVertices = true;
			}
		}

		public void Draw( bool vertices )
		{
			EnsureObjects();
			_edges.Draw( component );
			if ( vertices )
				_vertices.Draw( component );
			else
				_vertices.SetVisible( false );
			UpdateVisibility();
		}

		public void HideBase()
		{
			_edges.SetVisible( false );
			_vertices.SetVisible( false );
			UpdateVisibility();
		}

		public void DrawSelected( Vector3 cameraPosition )
		{
			EnsureObjects();
			if ( _selectedVerticesDirty )
			{
				_positions.Clear();
				foreach ( var vertex in _vertexSelection )
				{
					if ( vertex.IsValid() )
						_positions.Add( vertex.PositionWorld );
				}
				_selectedVertices.Upload( _positions );
				_selectedVerticesDirty = false;
			}

			if ( _selectionDirty )
			{
				_positions.Clear();
				OpenEdges.Clear();
				foreach ( var edge in _edgeSelection )
				{
					if ( !edge.IsValid() )
						continue;
					if ( edge.IsOpen )
					{
						OpenEdges.Add( edge );
						continue;
					}
					_positions.Add( edge.Line.Start );
					_positions.Add( edge.Line.End );
				}
				_selectedEdges.Upload( _positions );

				_positions.Clear();
				foreach ( var edge in OpenEdges )
				{
					_positions.Add( edge.Line.Start );
					_positions.Add( edge.Line.End );
				}
				_selectedOpenEdges.Upload( _positions );
				_selectionDirty = false;
				_hatchesDirty = true;
			}

			if ( _hatchesDirty || _cameraPosition != cameraPosition )
			{
				if ( _hatchesDirty )
				{
					_hatchEdges.Clear();
					foreach ( var edge in OpenEdges )
					{
						if ( TryCreateHatch( edge, out var hatch ) )
							_hatchEdges.Add( hatch );
					}
				}
				_positions.Clear();
				foreach ( var hatch in _hatchEdges )
					hatch.AddPositions( cameraPosition, _positions );
				_hatches.Upload( _positions );
				_hatchesDirty = false;
				_cameraPosition = cameraPosition;
			}

			_selectedVertices.Draw( component );
			_selectedEdges.Draw( component );
			_selectedOpenEdges.Draw( component );
			_hatches.Draw( component );
			UpdateVisibility();
		}

		public void Dispose()
		{
			_depthObject?.Delete();
			_overlayObject?.Delete();
			_edges.Dispose();
			_vertices.Dispose();
			_selectedVertices.Dispose();
			_selectedEdges.Dispose();
			_selectedOpenEdges.Dispose();
			_hatches.Dispose();
		}
	}

	internal static void AddHatches( MeshEdge edge, Vector3 cameraPosition, List<Vector3> positions )
	{
		if ( TryCreateHatch( edge, out var hatch ) )
			hatch.AddPositions( cameraPosition, positions );
	}

	static bool TryCreateHatch( MeshEdge edge, out Hatch hatch )
	{
		hatch = default;
		var mesh = edge.Component.Mesh;
		var face = mesh.GetHalfEdgeFace( edge.Handle );
		if ( !face.IsValid )
			face = mesh.GetHalfEdgeFace( mesh.GetOppositeHalfEdge( edge.Handle ) );
		if ( !face.IsValid )
			return false;

		var line = edge.Line;
		var transform = edge.Transform;
		var a = transform.PointToWorld( line.Start );
		var b = transform.PointToWorld( line.End );
		var length = a.Distance( b );
		if ( length <= 0 )
			return false;

		mesh.ComputeFaceNormal( face, out var normal );
		var tangent = transform.NormalToWorld( normal ).Cross( (b - a).Normal );
		if ( tangent.IsNearlyZero( 0.0001f ) )
			return false;
		tangent = tangent.Normal;
		var center = transform.PointToWorld( mesh.GetFaceCenter( face ) );
		if ( tangent.Dot( center - (a + b) * 0.5f ) < 0 )
			tangent = -tangent;

		hatch = new Hatch( a, b, tangent, length );
		return true;
	}

	readonly record struct Hatch( Vector3 Start, Vector3 End, Vector3 Tangent, float Length )
	{
		public void AddPositions( Vector3 cameraPosition, List<Vector3> positions )
		{
			var travelled = 0f;
			for ( var i = 0; i < 256; ++i )
			{
				var position = Vector3.Lerp( Start, End, travelled / Length );
				var scale = cameraPosition.Distance( position ) / 1000f;
				positions.Add( position );
				positions.Add( position + Tangent * (12f * scale) );
				if ( travelled >= Length )
					break;
				var step = MathF.Min( 20f * scale, Length * 0.5f );
				travelled = MathF.Min( travelled + MathF.Max( step, 0.01f ), Length );
			}
		}
	}

	sealed class Batch : IDisposable
	{
		readonly Pass[] _passes;

		public Batch( bool points, Color color, float size, float occludedAlpha, bool selected = false, bool worldspace = false )
		{
			_passes = selected
				? [new Pass( points, color, size, true, worldspace )]
				: [new Pass( points, color, size, false, worldspace ), new Pass( points, color.Darken( 0.3f ).WithAlpha( occludedAlpha ), points ? size : 1, true, worldspace )];
		}

		public void Upload( List<Vector3> positions, List<int> indices = null )
		{
			foreach ( var pass in _passes )
				pass.Upload( positions, indices );
		}

		public void Draw( MeshComponent component )
		{
			foreach ( var pass in _passes )
				pass.Draw( component );
		}

		public void Render( bool ignoreDepth )
		{
			foreach ( var pass in _passes )
				pass.Render( ignoreDepth );
		}

		public bool IsVisible( bool ignoreDepth )
		{
			foreach ( var pass in _passes )
			{
				if ( pass.IsVisible( ignoreDepth ) )
					return true;
			}
			return false;
		}

		public void SetVisible( bool visible )
		{
			foreach ( var pass in _passes )
				pass.SetVisible( visible );
		}

		public void Dispose()
		{
			foreach ( var pass in _passes )
				pass.Dispose();
		}
	}

	sealed class Pass( bool points, Color color, float size, bool ignoreDepth, bool worldspace ) : IDisposable
	{
		const int MaxSpareLineVertices = 1024;
		Mesh _mesh;
		Model _model;
		readonly RenderAttributes _attributes = new();
		Transform _transform;
		int _count;
		int[] _indices = [];
		bool _visible;
		bool _configure;

		public void Upload( List<Vector3> positions, List<int> indices )
		{
			var count = indices?.Count ?? positions.Count;
			if ( count == 0 )
			{
				Dispose();
				return;
			}

			var vertexCount = positions.Count;
			if ( !points && indices is null )
			{
				var capacity = _mesh?.VertexCount ?? 0;
				if ( vertexCount > capacity || capacity - vertexCount > MaxSpareLineVertices )
				{
					var spare = Math.Min( vertexCount / 4, MaxSpareLineVertices / 2 );
					vertexCount = checked(vertexCount + (spare & ~1));
				}
				else
				{
					vertexCount = capacity;
				}
			}

			var buffer = ArrayPool<OverlayVertex>.Shared.Rent( vertexCount );
			try
			{
				var vertices = buffer.AsSpan( 0, vertexCount );
				Color32 vertexColor = color;
				for ( var i = 0; i < positions.Count; ++i )
				{
					vertices[i] = new OverlayVertex
					{
						Position = positions[i],
						Color = vertexColor,
						Normal = Vector3.Up,
						TexCoord = new Vector4( size, size, 0, 0 )
					};
				}
				vertices[positions.Count..].Clear();

				var indexSpan = indices is null ? default : CollectionsMarshal.AsSpan( indices );
				if ( indices is null )
				{
					if ( _indices.Length != vertexCount )
					{
						_indices = new int[vertexCount];
						for ( var i = 0; i < _indices.Length; ++i )
							_indices[i] = i;
					}
					indexSpan = _indices.AsSpan();
				}
				if ( _mesh is null || _mesh.VertexCount != vertexCount || _indices.Length != indexSpan.Length )
				{
					_mesh = new Mesh( points ? PointMaterial : LineMaterial, points ? MeshPrimitiveType.Points : MeshPrimitiveType.Lines );
					_mesh.CreateVertexBuffer( vertices.Length, vertices );
					_mesh.CreateIndexBuffer( indexSpan.Length, indexSpan );
					_mesh.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
					_model = Model.Builder.AddMesh( _mesh ).Create();
					_configure = true;
				}
				else
				{
					_mesh.SetVertexBufferData( vertices );
					if ( !indexSpan.SequenceEqual( _indices ) )
						_mesh.SetIndexBufferData( indexSpan );
				}
				if ( !indexSpan.SequenceEqual( _indices ) )
					_indices = indexSpan.ToArray();
				_count = count;
			}
			finally
			{
				ArrayPool<OverlayVertex>.Shared.Return( buffer );
			}
		}

		public void Draw( MeshComponent component )
		{
			if ( _count == 0 )
				return;

			if ( _configure )
			{
				_attributes.SetCombo( "D_NO_ZTEST", ignoreDepth ? 1 : 0 );
				_attributes.Set( "LineThickness", size );
				if ( points )
					_attributes.Set( "TextureColor", Texture.White );
				_configure = false;
			}

			_transform = points || worldspace ? Transform.Zero : component.WorldTransform;
			SetVisible( true );
		}

		public void Render( bool depthIgnored )
		{
			if ( !_visible || _count == 0 || depthIgnored != ignoreDepth )
				return;

			Graphics.DrawModelInstanced( _model, stackalloc Transform[] { _transform }, _attributes );
		}

		public bool IsVisible( bool depthIgnored ) => _visible && _count > 0 && depthIgnored == ignoreDepth;

		public void SetVisible( bool visible )
		{
			_visible = visible;
		}

		public void Dispose()
		{
			_visible = false;
			_mesh = null;
			_model = null;
			_count = 0;
			_indices = [];
		}
	}

	struct OverlayVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Color] public Color32 Color;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.TexCoord] public Vector4 TexCoord;
	}
}
