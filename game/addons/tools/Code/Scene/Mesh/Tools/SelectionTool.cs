namespace Editor.MeshEditor;

public abstract class SelectionTool : EditorTool
{
	public virtual void SetMoveMode( MoveMode mode ) { }

	public Vector3 Pivot { get; set; }

	public bool DragStarted { get; private set; }

	public bool GlobalSpace { get; set; }

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

	public void StartDrag()
	{
		DragStarted = true;

		OnStartDrag();
	}

	public void UpdateDrag()
	{
		OnUpdateDrag();
	}

	public void EndDrag()
	{
		DragStarted = false;

		OnEndDrag();
	}

	protected virtual void OnStartDrag()
	{
	}

	protected virtual void OnUpdateDrag()
	{
	}

	protected virtual void OnEndDrag()
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

	public virtual void Nudge( Vector2 delta )
	{
	}

	public override Widget CreateShortcutsWidget() => new SelectionToolShortcutsWidget( this );

	/// <summary>
	/// Stores the previous selection for each tool type so that re-entering the tool restores it.
	/// </summary>
	[SkipHotload]
	protected static readonly Dictionary<Type, SelectionSystem> PreviousSelections = [];

	public static IEnumerable<T> GetAllSelected<T>()
	{
		return PreviousSelections.Values
			.SelectMany( s => s )
			.OfType<T>()
			.Distinct();
	}

	protected void SaveCurrentSelection<T>() where T : IValid
	{
		var stored = PreviousSelections.GetOrCreate( GetType() );
		stored.Clear();

		foreach ( var element in Selection.OfType<T>().Where( x => x.IsValid() ) )
		{
			stored.Add( element );
		}
	}

	protected void RestorePreviousSelection<T>() where T : IValid
	{
		if ( !PreviousSelections.TryGetValue( GetType(), out var previousSelection ) )
			return;

		foreach ( var element in previousSelection.OfType<T>().Where( x => x.IsValid() ) )
		{
			Selection.Add( element );
		}
	}
}

file class SelectionToolShortcutsWidget( SelectionTool tool ) : Widget
{
	[Shortcut( "mesh.selection-nudge-up", "UP", typeof( SceneViewWidget ) )]
	public void NudgeUp() => tool.Nudge( Vector2.Up );

	[Shortcut( "mesh.selection-nudge-down", "DOWN", typeof( SceneViewWidget ) )]
	public void NudgeDown() => tool.Nudge( Vector2.Down );

	[Shortcut( "mesh.selection-nudge-left", "LEFT", typeof( SceneViewWidget ) ),]
	public void NudgeLeft() => tool.Nudge( Vector2.Left );

	[Shortcut( "mesh.selection-nudge-right", "RIGHT", typeof( SceneViewWidget ) )]
	public void NudgeRight() => tool.Nudge( Vector2.Right );
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

		RestorePreviousSelection<T>();
		SelectElements();
		CalculateSelectionVertices();
		OnMeshSelectionChanged();
	}

	public override void OnDisabled()
	{
		SaveCurrentSelection<T>();
	}

	public bool IsAllowedToSelect => Tool?.MoveMode?.AllowSceneSelection ?? true;

	public override void OnUpdate()
	{
		GlobalSpace = Gizmo.Settings.GlobalSpace;

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

		bool isConverting = Application.KeyboardModifiers.Contains( KeyboardModifiers.Alt );
		var convertedElements = isConverting ?
			ConvertSelectionToCurrentType().ToArray() : [];

		var connectedElements = Application.KeyboardModifiers.Contains( KeyboardModifiers.Shift ) ?
			GetConnectedSelectionElements().ToArray() : [];

		Selection.Clear();

		if ( !isConverting )
		{
			foreach ( var element in elements ) Selection.Add( element );
		}

		foreach ( var element in convertedElements ) Selection.Add( element );
		foreach ( var element in connectedElements ) Selection.Add( element );
	}

	protected virtual IEnumerable<T> ConvertSelectionToCurrentType() => [];

	protected virtual IEnumerable<T> GetConnectedSelectionElements() => [];

	protected virtual IEnumerable<IMeshElement> GetAllSelectedElements() => [];

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

	public override void Nudge( Vector2 direction )
	{
		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
		if ( !viewport.IsValid() ) return;

		var gizmo = viewport.GizmoInstance;
		if ( gizmo is null ) return;

		using var gizmoScope = gizmo.Push();
		if ( Gizmo.Pressed.Any ) return;

		var components = Selection.OfType<IMeshElement>().Select( x => x.Component );
		if ( components.Any() == false ) return;

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active.UndoScope( "Nudge Vertices" ).WithComponentChanges( components ).Push();

		var rotation = CalculateSelectionBasis();
		var delta = Gizmo.Nudge( rotation, direction );

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
				position = transform.PointToWorld( position ) - delta;
				vertex.Component.Mesh.SetVertexPosition( vertex.Handle, transform.PointToLocal( position ) );
			}
		}

		Pivot -= delta;
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
		Tool?.MoveMode?.OnBegin( this );
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

	protected override void OnStartDrag()
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

	protected override void OnUpdateDrag()
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

	protected override void OnEndDrag()
	{
		_transformVertices.Clear();
		_transformFaces = null;

		_undoScope?.Dispose();
		_undoScope = null;
	}

	public MeshFace TraceFace()
	{
		if ( IsBoxSelecting || IsLassoSelecting )
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

	//All the meshtools want the lasso selection mode
	public override bool HasLassoSelectionMode() => true;

	protected override void OnLassoSelect( List<Vector2> lassoPoints, bool isFinal )
	{
		if ( !isFinal ) return;

		LassoSelection( lassoPoints );
	}

	/// <summary>
	/// Check if a vertex position is occluded by geometry when viewed from the camera.
	/// </summary>
	protected bool IsVertexOccluded( Vector3 worldPos, Vector3 cameraPos )
	{
		var trace = Scene.Trace
			.Ray( cameraPos, worldPos )
			.UseRenderMeshes()
			.Run();

		if ( trace.Hit )
		{
			var distanceToVertex = Vector3.DistanceBetween( cameraPos, worldPos );
			var distanceToHit = Vector3.DistanceBetween( cameraPos, trace.HitPosition );
			return distanceToHit < distanceToVertex - 0.1f;
		}

		return false;
	}

	protected void LassoSelection( List<Vector2> lassoPoints )
	{
		var minX = float.MaxValue;
		var maxX = float.MinValue;
		var minY = float.MaxValue;
		var maxY = float.MinValue;

		foreach ( var p in lassoPoints )
		{
			if ( p.x < minX ) minX = p.x;
			if ( p.x > maxX ) maxX = p.x;
			if ( p.y < minY ) minY = p.y;
			if ( p.y > maxY ) maxY = p.y;
		}

		var lassoBounds = new Rect( minX, minY, maxX - minX, maxY - minY );
		var cameraPos = Gizmo.Camera.Position;
		var cameraForward = Gizmo.Camera.Rotation.Forward;

		HashSet<T> selection = [];
		HashSet<T> previous = [];

		foreach ( var component in Scene.GetAllComponents<MeshComponent>() )
		{
			var mesh = component.Mesh;
			if ( mesh == null ) continue;

			var worldBounds = component.GetWorldBounds();
			var meshScreenBounds = GetScreenRectFromBounds( worldBounds );

			if ( !lassoBounds.IsInside( meshScreenBounds ) )
			{
				if ( typeof( T ) == typeof( MeshVertex ) )
				{
					foreach ( var h in mesh.VertexHandles )
						previous.Add( (T)(object)new MeshVertex( component, h ) );
				}
				else if ( typeof( T ) == typeof( MeshEdge ) )
				{
					foreach ( var h in mesh.HalfEdgeHandles )
					{
						if ( h.Index > mesh.GetOppositeHalfEdge( h ).Index )
							continue;
						previous.Add( (T)(object)new MeshEdge( component, h ) );
					}
				}
				else if ( typeof( T ) == typeof( MeshFace ) )
				{
					foreach ( var h in mesh.FaceHandles )
						previous.Add( (T)(object)new MeshFace( component, h ) );
				}
				continue;
			}

			var transform = component.Transform.World;

			if ( typeof( T ) == typeof( MeshVertex ) )
			{
				foreach ( var h in mesh.VertexHandles )
				{
					var worldPos = transform.PointToWorld( mesh.GetVertexPosition( h ) );
					var vertex = (T)(object)new MeshVertex( component, h );

					var toVertex = worldPos - cameraPos;
					if ( Vector3.Dot( toVertex, cameraForward ) < 0.1f )
					{
						previous.Add( vertex );
						continue;
					}

					if ( !Tool.SelectionThrough && IsVertexOccluded( worldPos, cameraPos ) )
					{
						previous.Add( vertex );
						continue;
					}

					var screenPos = Gizmo.Camera.ToScreen( worldPos );

					if ( lassoBounds.IsInside( screenPos ) && IsPointInLasso( screenPos, lassoPoints ) )
						selection.Add( vertex );
					else
						previous.Add( vertex );
				}
			}
			else if ( typeof( T ) == typeof( MeshEdge ) )
			{
				foreach ( var h in mesh.HalfEdgeHandles )
				{
					if ( h.Index > mesh.GetOppositeHalfEdge( h ).Index )
						continue;

					mesh.GetEdgeVertices( h, out var vA, out var vB );
					var worldPosA = transform.PointToWorld( mesh.GetVertexPosition( vA ) );
					var worldPosB = transform.PointToWorld( mesh.GetVertexPosition( vB ) );
					var edge = (T)(object)new MeshEdge( component, h );

					var toVertexA = worldPosA - cameraPos;
					var toVertexB = worldPosB - cameraPos;
					bool aInFront = Vector3.Dot( toVertexA, cameraForward ) > 0.1f;
					bool bInFront = Vector3.Dot( toVertexB, cameraForward ) > 0.1f;

					if ( !aInFront && !bInFront )
					{
						previous.Add( edge );
						continue;
					}

					if ( !Tool.SelectionThrough )
					{
						if ( aInFront && IsVertexOccluded( worldPosA, cameraPos ) )
							aInFront = false;

						if ( bInFront && IsVertexOccluded( worldPosB, cameraPos ) )
							bInFront = false;

						if ( !aInFront && !bInFront )
						{
							previous.Add( edge );
							continue;
						}
					}

					var screenPosA = Gizmo.Camera.ToScreen( worldPosA );
					var screenPosB = Gizmo.Camera.ToScreen( worldPosB );

					bool aInLasso = aInFront && lassoBounds.IsInside( screenPosA ) && IsPointInLasso( screenPosA, lassoPoints );
					bool bInLasso = bInFront && lassoBounds.IsInside( screenPosB ) && IsPointInLasso( screenPosB, lassoPoints );

					bool isSelected = Tool.LassoPartialSelection ? (aInLasso || bInLasso) : (aInLasso && bInLasso);

					if ( isSelected )
						selection.Add( edge );
					else
						previous.Add( edge );
				}
			}
			else if ( typeof( T ) == typeof( MeshFace ) )
			{
				foreach ( var h in mesh.FaceHandles )
				{
					mesh.GetVerticesConnectedToFace( h, out var vertices );
					var face = (T)(object)new MeshFace( component, h );

					int inFrontCount = 0;
					int inLassoCount = 0;
					int totalCount = 0;

					foreach ( var v in vertices )
					{
						var worldPos = transform.PointToWorld( mesh.GetVertexPosition( v ) );
						totalCount++;

						var toVertex = worldPos - cameraPos;
						if ( Vector3.Dot( toVertex, cameraForward ) <= 0 )
							continue;

						if ( !Tool.SelectionThrough && IsVertexOccluded( worldPos, cameraPos ) )
							continue;

						inFrontCount++;

						var screenPos = Gizmo.Camera.ToScreen( worldPos );
						if ( lassoBounds.IsInside( screenPos ) && IsPointInLasso( screenPos, lassoPoints ) )
							inLassoCount++;
					}

					if ( inFrontCount == 0 )
					{
						previous.Add( face );
						continue;
					}

					bool isSelected = Tool.LassoPartialSelection ? (inLassoCount > 0) : (inLassoCount == totalCount);

					if ( isSelected )
						selection.Add( face );
					else
						previous.Add( face );
				}
			}
		}

		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			foreach ( var element in selection )
			{
				if ( Selection.Contains( element ) )
					Selection.Remove( element );
			}
		}
		else
		{
			foreach ( var element in selection )
			{
				if ( !Selection.Contains( element ) )
					Selection.Add( element );
			}

			if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			{
				foreach ( var element in previous )
				{
					if ( Selection.Contains( element ) )
						Selection.Remove( element );
				}
			}
		}
	}

	private Rect GetScreenRectFromBounds( BBox bounds )
	{
		var corners = bounds.Corners.ToArray();
		var min = new Vector2( float.MaxValue, float.MaxValue );
		var max = new Vector2( float.MinValue, float.MinValue );

		foreach ( var corner in corners )
		{
			var screenPos = Gizmo.Camera.ToScreen( corner );
			min = Vector2.Min( min, screenPos );
			max = Vector2.Max( max, screenPos );
		}

		return new Rect( min.x, min.y, max.x - min.x, max.y - min.y );
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
