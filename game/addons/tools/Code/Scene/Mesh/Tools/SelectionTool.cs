namespace Editor.MeshEditor;

public abstract class SelectionTool : EditorTool
{
	public virtual void SetMoveMode( MoveMode mode ) { }

	public Vector3 Pivot { get; set; }

	public virtual Vector3 CalculateSelectionOrigin()
	{
		return default;
	}

	public virtual Rotation CalculateSelectionBasis()
	{
		return Rotation.Identity;
	}

	public virtual BBox CalculateSelectionBounds()
	{
		return default;
	}

	public virtual BBox CalculateLocalBounds()
	{
		return default;
	}

	public virtual void StartDrag()
	{
	}

	public virtual void UpdateDrag()
	{
	}

	public virtual void EndDrag()
	{
	}

	public virtual void Translate( Vector3 delta )
	{
	}

	public virtual void Rotate( Vector3 origin, Rotation basis, Rotation delta )
	{
	}

	public virtual void Scale( Vector3 origin, Rotation basis, Vector3 scale )
	{
	}

	public virtual void Resize( Vector3 origin, Rotation basis, Vector3 scale )
	{
		Scale( origin, basis, scale );
	}
}

public abstract class SelectionTool<T>( MeshTool tool ) : SelectionTool where T : IMeshElement
{
	protected MeshTool Tool { get; private init; } = tool;

	readonly HashSet<MeshVertex> _vertexSelection = [];
	readonly Dictionary<MeshVertex, Vector3> _transformVertices = [];
	List<MeshFace> _transformFaces;
	IDisposable _undoScope;

	protected virtual bool HasMoveMode => true;

	public static bool IsMultiSelecting => Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) ||
				Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift );

	private bool _meshSelectionDirty;
	private bool _nudge;
	private bool _invertSelection;

	private MeshComponent _hoverMesh;
	public virtual bool DrawVertices => false;

	public override void SetMoveMode( MoveMode mode )
	{
		if ( Tool != null )
		{
			Tool.MoveMode = mode;
		}
	}

	public override void Translate( Vector3 delta )
	{
		foreach ( var entry in _transformVertices )
		{
			var position = entry.Value + delta;
			var transform = entry.Key.Transform;
			entry.Key.Component.Mesh.SetVertexPosition( entry.Key.Handle, transform.PointToLocal( position ) );
		}
	}

	public override void Rotate( Vector3 origin, Rotation basis, Rotation delta )
	{
		foreach ( var entry in _transformVertices )
		{
			var rotation = basis * delta * basis.Inverse;
			var position = entry.Value - origin;
			position *= rotation;
			position += origin;

			var transform = entry.Key.Transform;
			entry.Key.Component.Mesh.SetVertexPosition( entry.Key.Handle, transform.PointToLocal( position ) );
		}
	}

	public override void Scale( Vector3 origin, Rotation basis, Vector3 scale )
	{
		foreach ( var entry in _transformVertices )
		{
			var position = (entry.Value - origin) * basis.Inverse;
			position *= scale;
			position *= basis;
			position += origin;

			var transform = entry.Key.Transform;
			entry.Key.Component.Mesh.SetVertexPosition( entry.Key.Handle, transform.PointToLocal( position ) );
		}
	}

	public override BBox CalculateLocalBounds()
	{
		return BBox.FromPoints( _vertexSelection
			.Select( x => CalculateSelectionBasis().Inverse * x.PositionWorld ) );
	}

	public override void OnEnabled()
	{
		Selection.OnItemAdded += OnMeshSelectionChanged;
		Selection.OnItemRemoved += OnMeshSelectionChanged;

		SceneEditorSession.Active.UndoSystem.OnUndo += ( _ ) => OnMeshSelectionChanged();
		SceneEditorSession.Active.UndoSystem.OnRedo += ( _ ) => OnMeshSelectionChanged();

		SelectElements();
		CalculateSelectionVertices();
		OnMeshSelectionChanged();
	}

	public bool IsAllowedToSelect => Tool?.MoveMode?.AllowSceneSelection ?? true;

	public override void OnUpdate()
	{
		UpdateMoveMode();

		if ( IsAllowedToSelect && Gizmo.WasLeftMouseReleased && !Gizmo.Pressed.Any && Gizmo.Pressed.CursorDelta.Length < 1 )
		{
			Gizmo.Select();
		}

		var removeList = GetInvalidSelection().ToList();
		foreach ( var s in removeList )
		{
			Selection.Remove( s );
		}

		if ( Application.IsKeyDown( KeyCode.I ) )
		{
			if ( !_invertSelection && Gizmo.IsCtrlPressed )
			{
				InvertSelection();
			}

			_invertSelection = true;
		}
		else
		{
			_invertSelection = false;
		}

		UpdateNudge();

		if ( _meshSelectionDirty )
		{
			CalculateSelectionVertices();
			OnMeshSelectionChanged();
		}

		if ( IsAllowedToSelect )
			DrawSelection();
	}

	void UpdateMoveMode()
	{
		if ( !HasMoveMode ) return;
		if ( Tool is null ) return;
		if ( Tool.MoveMode is null ) return;
		if ( !Selection.OfType<IMeshElement>().Any() ) return;

		Tool.MoveMode.Update( this );
	}

	void SelectElements()
	{
		var elements = Selection.OfType<T>().ToArray();

		Selection.Clear();

		foreach ( var element in elements )
		{
			Selection.Add( element );
		}
	}

	protected virtual IEnumerable<IMeshElement> GetAllSelectedElements()
	{
		return [];
	}

	void DrawSelection()
	{
		var face = TraceFace();
		if ( face.IsValid() )
			_hoverMesh = face.Component;

		if ( _hoverMesh.IsValid() )
			DrawMesh( _hoverMesh );

		foreach ( var group in Selection.OfType<IMeshElement>().GroupBy( x => x.Component ) )
		{
			var component = group.Key;
			if ( !component.IsValid() )
				continue;

			if ( component == _hoverMesh )
				continue;

			DrawMesh( component );
		}
	}

	void DrawMesh( MeshComponent mesh )
	{
		using ( Gizmo.ObjectScope( mesh.GameObject, mesh.WorldTransform ) )
		{
			using ( Gizmo.Scope( "Edges" ) )
			{
				var edgeColor = new Color( 0.3137f, 0.7843f, 1.0f, 1f );

				Gizmo.Draw.LineThickness = 1;
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = edgeColor.Darken( 0.3f ).WithAlpha( 0.1f );

				foreach ( var v in mesh.Mesh.GetEdges() )
				{
					Gizmo.Draw.Line( v );
				}

				Gizmo.Draw.Color = edgeColor;
				Gizmo.Draw.IgnoreDepth = false;
				Gizmo.Draw.LineThickness = 2;

				foreach ( var v in mesh.Mesh.GetEdges() )
				{
					Gizmo.Draw.Line( v );
				}
			}

			if ( DrawVertices )
			{
				var vertexColor = new Color( 1.0f, 1.0f, 0.3f, 1f );

				using ( Gizmo.Scope( "Vertices" ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = vertexColor.Darken( 0.3f ).WithAlpha( 0.2f );

					foreach ( var v in mesh.Mesh.GetVertexPositions() )
					{
						Gizmo.Draw.Sprite( v, 8, null, false );
					}

					Gizmo.Draw.Color = vertexColor;
					Gizmo.Draw.IgnoreDepth = false;

					foreach ( var v in mesh.Mesh.GetVertexPositions() )
					{
						Gizmo.Draw.Sprite( v, 8, null, false );
					}
				}
			}
		}
	}

	private void InvertSelection()
	{
		if ( !Selection.Any() )
			return;

		var newSelection = GetAllSelectedElements()
			.Except( Selection )
			.ToArray();

		Selection.Clear();

		foreach ( var element in newSelection )
		{
			Selection.Add( element );
		}
	}

	public virtual List<MeshFace> ExtrudeSelection( Vector3 delta = default )
	{
		return [];
	}

	private void UpdateNudge()
	{
		if ( Gizmo.Pressed.Any || !Application.FocusWidget.IsValid() || !Gizmo.HasMouseFocus )
			return;

		var keyUp = Application.IsKeyDown( KeyCode.Up );
		var keyDown = Application.IsKeyDown( KeyCode.Down );
		var keyLeft = Application.IsKeyDown( KeyCode.Left );
		var keyRight = Application.IsKeyDown( KeyCode.Right );

		if ( !keyUp && !keyDown && !keyLeft && !keyRight )
		{
			_nudge = false;

			_undoScope?.Dispose();
			_undoScope = null;

			return;
		}

		if ( _nudge )
			return;

		var basis = CalculateSelectionBasis();
		var direction = new Vector2( keyLeft ? 1 : keyRight ? -1 : 0, keyUp ? 1 : keyDown ? -1 : 0 );
		var delta = Gizmo.Nudge( basis, direction );

		var components = Selection.OfType<IMeshElement>().Select( x => x.Component );

		_undoScope ??= SceneEditorSession.Active.UndoScope( "Nudge Vertices" ).WithComponentChanges( components ).Push();

		if ( Gizmo.IsShiftPressed )
		{
			ExtrudeSelection( delta );
		}
		else
		{
			foreach ( var vertex in _vertexSelection )
			{
				var transform = vertex.Transform;
				var position = vertex.Component.Mesh.GetVertexPosition( vertex.Handle );
				position = transform.PointToWorld( position ) + delta;
				vertex.Component.Mesh.SetVertexPosition( vertex.Handle, transform.PointToLocal( position ) );
			}
		}

		_nudge = true;
	}

	public override BBox CalculateSelectionBounds()
	{
		return BBox.FromPoints( _vertexSelection
			.Where( x => x.IsValid() )
			.Select( x => x.Transform.PointToWorld( x.Component.Mesh.GetVertexPosition( x.Handle ) ) ) );
	}

	public override Vector3 CalculateSelectionOrigin()
	{
		var bounds = CalculateSelectionBounds();
		return bounds.Center;
	}

	public void CalculateSelectionVertices()
	{
		_vertexSelection.Clear();

		foreach ( var face in Selection.OfType<MeshFace>() )
		{
			foreach ( var vertex in face.Component.Mesh.GetFaceVertices( face.Handle )
				.Select( i => new MeshVertex( face.Component, i ) ) )
			{
				_vertexSelection.Add( vertex );
			}
		}

		foreach ( var vertex in Selection.OfType<MeshVertex>() )
		{
			_vertexSelection.Add( vertex );
		}

		foreach ( var edge in Selection.OfType<MeshEdge>() )
		{
			edge.Component.Mesh.GetEdgeVertices( edge.Handle, out var hVertexA, out var hVertexB );
			_vertexSelection.Add( new MeshVertex( edge.Component, hVertexA ) );
			_vertexSelection.Add( new MeshVertex( edge.Component, hVertexB ) );
		}

		_meshSelectionDirty = false;
	}

	private IEnumerable<IMeshElement> GetInvalidSelection()
	{
		foreach ( var selection in Selection.OfType<IMeshElement>()
			.Where( x => !x.IsValid() || x.Scene != Scene ) )
		{
			yield return selection;
		}
	}

	private void OnMeshSelectionChanged( object o )
	{
		_hoverMesh = null;
		_meshSelectionDirty = true;
	}

	private void OnMeshSelectionChanged()
	{
		Pivot = CalculateSelectionOrigin();
	}

	protected void Select( IMeshElement element )
	{
		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			if ( Selection.Contains( element ) )
			{
				Selection.Remove( element );
			}
			else
			{
				Selection.Add( element );
			}

			return;
		}
		else if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
		{
			if ( !Selection.Contains( element ) )
			{
				Selection.Add( element );
			}

			return;
		}

		Selection.Set( element );
	}

	public void UpdateSelection( IMeshElement element )
	{
		if ( Tool?.MoveMode?.AllowSceneSelection == false )
			return;

		if ( Gizmo.WasLeftMousePressed )
		{
			if ( element.IsValid() )
			{
				Select( element );
			}
			else if ( !IsMultiSelecting )
			{
				Selection.Clear();
			}
		}
		else if ( Gizmo.IsLeftMouseDown && element.IsValid() )
		{
			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
			{
				if ( Selection.Contains( element ) )
					Selection.Remove( element );
			}
			else
			{
				if ( !Selection.Contains( element ) )
					Selection.Add( element );
			}
		}
	}

	public override void StartDrag()
	{
		if ( _transformVertices.Count != 0 )
			return;

		var components = Selection.OfType<IMeshElement>()
			.Select( x => x.Component )
			.Distinct();

		_undoScope ??= SceneEditorSession.Active.UndoScope( $"{(Gizmo.IsShiftPressed ? "Extrude" : "Move")} Selection" )
			.WithComponentChanges( components )
			.Push();

		if ( Gizmo.IsShiftPressed )
		{
			_transformFaces = ExtrudeSelection();
		}

		foreach ( var vertex in _vertexSelection )
		{
			_transformVertices[vertex] = vertex.PositionWorld;
		}
	}

	public override void UpdateDrag()
	{
		if ( _transformFaces is not null )
		{
			foreach ( var group in _transformFaces.GroupBy( x => x.Component ) )
			{
				var mesh = group.Key.Mesh;
				var faces = group.Select( x => x.Handle ).ToArray();

				foreach ( var face in faces )
				{
					mesh.TextureAlignToGrid( mesh.Transform, face );
				}
			}
		}

		var meshes = _transformVertices
			.Select( x => x.Key.Component.Mesh )
			.Distinct();

		foreach ( var mesh in meshes )
		{
			mesh.ComputeFaceTextureCoordinatesFromParameters();
		}
	}

	public override void EndDrag()
	{
		_transformVertices.Clear();
		_transformFaces = null;

		_undoScope?.Dispose();
		_undoScope = null;
	}

	public MeshFace TraceFace()
	{
		if ( IsBoxSelecting )
			return default;

		var result = MeshTrace.Run();
		if ( !result.Hit || result.Component is not MeshComponent component )
			return default;

		var face = component.Mesh.TriangleToFace( result.Triangle );
		return new MeshFace( component, face );
	}

	public static Vector3 ComputeTextureVAxis( Vector3 normal ) => FaceDownVectors[GetOrientationForPlane( normal )];

	private static int GetOrientationForPlane( Vector3 plane )
	{
		plane = plane.Normal;
		var maxDot = 0.0f;
		int orientation = 0;

		for ( int i = 0; i < 6; i++ )
		{
			var dot = Vector3.Dot( plane, FaceNormals[i] );
			if ( dot >= maxDot )
			{
				maxDot = dot;
				orientation = i;
			}
		}

		return orientation;
	}

	[SkipHotload]
	private static readonly Vector3[] FaceNormals =
	{
		new( 0, 0, 1 ),
		new( 0, 0, -1 ),
		new( 0, -1, 0 ),
		new( 0, 1, 0 ),
		new( -1, 0, 0 ),
		new( 1, 0, 0 ),
	};

	[SkipHotload]
	private static readonly Vector3[] FaceDownVectors =
	{
		new( 0, -1, 0 ),
		new( 0, -1, 0 ),
		new( 0, 0, -1 ),
		new( 0, 0, -1 ),
		new( 0, 0, -1 ),
		new( 0, 0, -1 ),
	};
}
