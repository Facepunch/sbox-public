
using HalfEdgeMesh;

namespace Editor.MeshEditor;

/// <summary>
/// Select and edit objects.
/// </summary>
[Title( "Object Selection" )]
[Icon( "meshtools/sub-tools/object_selection.png" )]
[Alias( "tools.object-selection" )]
[Group( "5" )]
public sealed partial class ObjectSelection( MeshTool tool ) : SelectionTool( tool )
{
	readonly Dictionary<GameObject, Transform> _startPoints = [];
	readonly Dictionary<MeshVertex, Vector3> _transformVertices = [];
	IDisposable _undoScope;

	MeshComponent[] _meshes = [];
	GameObject[] _objects = [];

	protected override bool ShowSelectionBoundsDefault => true;

	readonly Dictionary<MeshComponent, FaceTextureParameters[]> _startFaceParameters = [];

	readonly record struct FaceTextureParameters( FaceHandle Face, Vector4 AxisU, Vector4 AxisV, Vector2 Scale );
	readonly record struct MeshTopology( int VertexCount, int EdgeCount, int FaceCount );

	public override void BuildSceneContextMenu( Menu menu, Ray ray, SceneTraceResult? trace )
	{
		menu.AddSeparator();

		bool hasMeshes = _meshes.Length > 0;
		bool manyMeshes = _meshes.Length > 1;
		bool hasObjects = _objects.Length > 0;

		if ( hasObjects )
		{
			var selection = menu.AddMenu( "Selection", "select_all" );
			AddMenuOption( selection, "Select Similar", "filter_center_focus", SelectSimilar, "mesh.select-similar", true );
		}

		bool convertible = _objects
			.Select( x => x.GetComponent<ModelRenderer>() )
			.Any( x => x.IsValid() && x.Model.IsValid() && x.Model.HasRenderMeshes() );

		if ( manyMeshes || convertible || hasMeshes )
		{
			var ops = menu.AddMenu( "Object Operations", "build" );
			AddMenuOption( ops, "Merge Meshes", "meshtools/object_selection_buttons/merge_meshes.png", "mesh.merge-meshes", manyMeshes );
			AddMenuOption( ops, "Boolean Tool", "meshtools/object_selection_buttons/boolean_tool.png", "mesh.boolean-tool", manyMeshes );
			AddMenuOption( ops, "Convert To Mesh", "meshtools/object_selection_buttons/convert_to_mesh.png", "mesh.convert-model-to-mesh", convertible );
			AddMenuOption( ops, "Flip Faces", "meshtools/object_selection_buttons/flip_faces.png", "mesh.flip-all-mesh-faces", hasMeshes );
		}

		if ( hasMeshes )
		{
			var transform = menu.AddMenu( "Transform", "straighten" );
			AddMenuOption( transform, "Bake Scale", "meshtools/object_selection_buttons/bake_scale.png", "mesh.bake-scale", true );
			AddMenuOption( transform, "Set Origin To Pivot", "meshtools/object_selection_buttons/set_origin_to_pivot.png", "mesh.set-origin-to-pivot", hasObjects );
			AddMenuOption( transform, "Center Origin", "meshtools/object_selection_buttons/center_origin.png", "mesh.center-origin", true );
			AddMenuOption( transform, "Align To View", "visibility", "gameObject.align-to-view", true );
			transform.AddSeparator();
			AddMenuOption( transform, "Align Down Local", "vertical_align_bottom", "mesh.align-down-local", true );
			AddMenuOption( transform, "Align Down World", "vertical_align_bottom", "mesh.align-down-world", true );
			AddMenuOption( transform, "Align To Closest Normal", "swap_vert", "mesh.align-to-closest-normal", true );
		}

		if ( hasObjects )
		{
			var pivot = menu.AddMenu( "Pivot", "my_location" );
			AddMenuOption( pivot, "Previous", "meshtools/pivot_tools/previous.png", "mesh.previous-pivot", true );
			AddMenuOption( pivot, "Next", "meshtools/pivot_tools/next.png", "mesh.next-pivot", true );
			AddMenuOption( pivot, "Clear", "meshtools/pivot_tools/clear.png", "mesh.clear-pivot", true );
			AddMenuOption( pivot, "Center", "meshtools/pivot_tools/center.png", "mesh.center-pivot", true );
			AddMenuOption( pivot, "World Origin", "meshtools/pivot_tools/world_origin.png", "mesh.zero-pivot", true );
		}
	}

	protected override void OnStartDrag()
	{
		if ( _startPoints.Count > 0 ) return;

		if ( Gizmo.IsShiftPressed )
		{
			_undoScope ??= SceneEditorSession.Active.UndoScope( "Duplicate Object(s)" )
				.WithGameObjectCreations()
				.WithComponentChanges( _meshes )
				.Push();

			DuplicateSelection();
			OnSelectionChanged();
		}
		else
		{
			_undoScope ??= SceneEditorSession.Active.UndoScope( "Transform Object(s)" )
				.WithGameObjectChanges( _objects, GameObjectUndoFlags.Properties )
				.WithComponentChanges( _meshes )
				.Push();
		}

		foreach ( var go in _objects )
		{
			_startPoints[go] = go.WorldTransform;
		}

		_startFaceParameters.Clear();

		foreach ( var mesh in _meshes )
		{
			foreach ( var vertex in mesh.Mesh.VertexHandles )
			{
				var v = new MeshVertex( mesh, vertex );
				_transformVertices[v] = mesh.WorldTransform.PointToWorld( mesh.Mesh.GetVertexPosition( vertex ) );
			}

			var parameters = new List<FaceTextureParameters>();

			foreach ( var face in mesh.Mesh.FaceHandles )
			{
				mesh.Mesh.GetFaceTextureParameters( face, out var axisU, out var axisV, out var scale );
				parameters.Add( new FaceTextureParameters( face, axisU, axisV, scale ) );
			}

			_startFaceParameters[mesh] = parameters.ToArray();
		}
	}

	protected override void OnEndDrag()
	{
		_startPoints.Clear();
		_startFaceParameters.Clear();
		_transformKind = TextureLockTransform.Move;

		_undoScope?.Dispose();
		_undoScope = null;
	}

	public override void Translate( Vector3 delta )
	{
		_transformKind = TextureLockTransform.Move;

		foreach ( var entry in _startPoints )
		{
			entry.Key.WorldPosition = entry.Value.Position + delta;
		}
	}

	public override void Rotate( Vector3 origin, Rotation basis, Rotation delta )
	{
		_transformKind = TextureLockTransform.Rotate;

		foreach ( var entry in _startPoints )
		{
			var rot = basis * delta * basis.Inverse;
			var position = entry.Value.Position - origin;
			position *= rot;
			position += origin;
			rot *= entry.Value.Rotation;
			var scale = entry.Value.Scale;
			entry.Key.WorldTransform = new Transform( position, rot, scale );
		}
	}

	public override void Scale( Vector3 origin, Rotation basis, Vector3 deltaScale )
	{
		_transformKind = TextureLockTransform.Scale;

		var scaleFromIndividualOrigins = !GlobalSpace && _startPoints.Count > 1;

		foreach ( var entry in _startPoints )
		{
			var position = entry.Value.Position;

			if ( !scaleFromIndividualOrigins )
			{
				position -= origin;
				position *= basis.Inverse;
				position *= deltaScale;
				position *= basis;
				position += origin;
			}

			entry.Key.WorldTransform = new Transform(
				position,
				entry.Value.Rotation,
				entry.Value.Scale * deltaScale
			);
		}
	}

	public override void Resize( Vector3 origin, Rotation basis, Vector3 scale )
	{
		_transformKind = TextureLockTransform.Scale;

		var invBasis = basis.Inverse;

		foreach ( var entry in _startPoints )
		{
			var start = entry.Value;
			var local = invBasis * (start.Position - origin);
			local *= scale;
			var position = origin + (basis * local);

			if ( entry.Key.GetComponent<MeshComponent>() is { } mc && mc.IsValid() )
			{
				mc.Mesh.SetTransform( mc.WorldTransform.WithPosition( position ) );
			}
			else
			{
				entry.Key.WorldTransform = new Transform( position, start.Rotation, start.Scale * scale );
			}
		}

		foreach ( var entry in _transformVertices )
		{
			var local = invBasis * (entry.Value - origin);
			local *= scale;
			var worldPos = origin + (basis * local);
			var mesh = entry.Key.Component.Mesh;
			mesh.SetVertexPosition( entry.Key.Handle, mesh.Transform.PointToLocal( worldPos ) );
		}

		foreach ( var start in _startPoints )
		{
			if ( start.Key.GetComponent<MeshComponent>() is not { } mc || !mc.IsValid() ) continue;

			mc.WorldTransform = mc.Mesh.Transform;
			mc.RebuildMesh();
		}
	}

	protected override void OnUpdateDrag()
	{
		if ( ShouldLockTexture() )
			return;

		foreach ( var (mesh, parameters) in _startFaceParameters )
		{
			if ( !mesh.IsValid() )
				continue;

			foreach ( var p in parameters )
			{
				if ( !p.Face.IsValid )
					continue;

				mesh.Mesh.SetFaceTextureParameters( p.Face, p.AxisU, p.AxisV, p.Scale );
			}

			mesh.RebuildMesh();
		}
	}

	public override void Nudge( Vector2 direction )
	{
		if ( _objects.Length == 0 ) return;

		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
		if ( !viewport.IsValid() ) return;

		var gizmo = viewport.GizmoInstance;
		if ( gizmo is null ) return;

		using var gizmoScope = gizmo.Push();
		if ( Gizmo.Pressed.Any ) return;

		using var scope = SceneEditorSession.Scope();
		var duplicate = Gizmo.IsShiftPressed;
		using var undoScope = duplicate
			? SceneEditorSession.Active.UndoScope( "Duplicate Object(s)" )
				.WithGameObjectCreations()
				.WithComponentChanges( _meshes )
				.Push()
			: SceneEditorSession.Active.UndoScope( "Nudge Mesh(s)" )
				.WithGameObjectChanges( _objects, GameObjectUndoFlags.Properties )
				.Push();

		if ( duplicate )
		{
			DuplicateSelection();
			OnSelectionChanged();
		}

		var rotation = CalculateSelectionBasis();
		var delta = Gizmo.Nudge( rotation, direction );

		Pivot -= delta;

		foreach ( var go in _objects )
		{
			go.WorldPosition -= delta;
		}

		Tool?.MoveMode?.OnBegin( this );
	}

	public override void NudgeRotation( Vector2 direction )
	{
		if ( !Selection.Any() ) return;

		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
		if ( !viewport.IsValid() ) return;

		var gizmo = viewport.GizmoInstance;
		if ( gizmo is null ) return;

		using var gizmoScope = gizmo.Push();
		if ( Gizmo.Pressed.Any ) return;

		var basis = CalculateSelectionBasis();
		var screenLeft = -Gizmo.Nudge( basis, Vector2.Left ).Normal;
		var screenUp = -Gizmo.Nudge( basis, Vector2.Up ).Normal;
		var faceNormal = screenLeft.Cross( screenUp ).Normal;

		var axis = direction.x != 0.0f
			? faceNormal
			: screenLeft;

		var angle = direction.x != 0.0f
			? direction.x * Gizmo.Settings.AngleSpacing
			: -direction.y * Gizmo.Settings.AngleSpacing;

		var delta = Rotation.FromAxis( axis, angle );

		StartDrag();

		try
		{
			Rotate( Pivot, Rotation.Identity, delta );
			UpdateDrag();
		}
		finally
		{
			EndDrag();
		}

		Tool?.MoveMode?.OnBegin( this );
	}

	public override BBox CalculateLocalBounds()
	{
		var invBasis = CalculateSelectionBasis().Inverse;

		var points = _objects
			.Where( x => x.IsValid() )
			.SelectMany( go =>
			{
				if ( go.GetComponent<MeshComponent>() is { } mc && mc.IsValid() )
					return mc.Mesh.VertexHandles.Select( v => invBasis * mc.WorldTransform.PointToWorld( mc.Mesh.GetVertexPosition( v ) ) );

				return go.GetBounds().Corners.Select( c => invBasis * c );
			} );

		return BBox.FromPoints( points );
	}

	public override Rotation CalculateSelectionBasis()
	{
		if ( GlobalSpace ) return Rotation.Identity;

		var mesh = _objects.FirstOrDefault();
		return mesh.IsValid() ? mesh.WorldRotation : Rotation.Identity;
	}

	public override void OnEnabled()
	{
		var objects = Selection.OfType<GameObject>()
			.ToArray();

		var connectedObjects = Selection.OfType<IMeshElement>()
			.Select( x => x.Component.GameObject )
			.ToArray();

		Selection.Clear();

		foreach ( var go in objects ) Selection.Add( go );
		foreach ( var go in connectedObjects ) Selection.Add( go );

		// Only restore previous selection if we don't have any selected objects ready to go.
		if ( !Selection.OfType<GameObject>().Any() )
		{
			RestorePreviousSelection<GameObject>();
		}

		OnSelectionChanged();

		var undo = SceneEditorSession.Active.UndoSystem;
		undo.OnUndo += OnUndoRedo;
		undo.OnRedo += OnUndoRedo;
	}

	public override void OnDisabled()
	{
		var undo = SceneEditorSession.Active.UndoSystem;
		undo.OnUndo -= OnUndoRedo;
		undo.OnRedo -= OnUndoRedo;

		SaveCurrentSelection<GameObject>();
	}

	void OnUndoRedo( object _ )
	{
		OnSelectionChanged();
	}

	public override void OnUpdate()
	{
		GlobalSpace = Gizmo.Settings.GlobalSpace;

		UpdateMoveMode();
		UpdateHovered();
		UpdateSelectionMode();

		if ( ShowSelectionBounds )
			DrawBounds();
	}

	void UpdateMoveMode()
	{
		if ( Tool is null ) return;
		if ( Tool.MoveMode is null ) return;
		if ( _objects.Length == 0 ) return;

		Tool.MoveMode.Update( this );
	}

	public override Vector3 CalculateSelectionOrigin()
	{
		var mesh = _objects.FirstOrDefault();
		return mesh.IsValid() ? mesh.WorldPosition : default;
	}

	public override BBox CalculateSelectionBounds()
	{
		return BBox.FromBoxes( _objects
			.Where( x => x.IsValid() )
			.Select( x => x.GetBounds() ) );
	}

	public override void OnSelectionChanged()
	{
		_objects = Selection.OfType<GameObject>().ToArray();
		_meshes = Selection.OfType<GameObject>()
			.Select( x => x.GetComponent<MeshComponent>() )
			.Where( x => x.IsValid() )
			.ToArray();

		_transformVertices.Clear();

		foreach ( var mesh in _meshes )
		{
			foreach ( var vertex in mesh.Mesh.VertexHandles )
			{
				var v = new MeshVertex( mesh, vertex );
				_transformVertices[v] = mesh.WorldTransform.PointToWorld( mesh.Mesh.GetVertexPosition( vertex ) );
			}
		}

		ClearPivot();
	}

	public void SelectSimilar()
	{
		var meshTopologies = _meshes
			.Where( x => x.IsValid() && x.Mesh is not null )
			.Select( GetTopology )
			.ToHashSet();

		var models = _objects
			.Select( x => x.GetComponent<ModelRenderer>() )
			.Where( x => x.IsValid() && x.Model.IsValid() )
			.Select( x => x.Model )
			.ToHashSet();

		if ( meshTopologies.Count == 0 && models.Count == 0 )
			return;

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active
			.UndoScope( "Select Similar Objects" )
			.Push();

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( go == Scene || go.Tags.Has( "hidden" ) )
				continue;

			bool matchingMesh = go.GetComponent<MeshComponent>() is { } meshComponent
				&& meshComponent.IsValid()
				&& meshComponent.Mesh is not null
				&& meshTopologies.Contains( GetTopology( meshComponent ) );

			bool matchingModel = go.GetComponent<ModelRenderer>() is { } modelRenderer
				&& modelRenderer.IsValid()
				&& modelRenderer.Model.IsValid()
				&& models.Contains( modelRenderer.Model );

			if ( matchingMesh || matchingModel )
				Selection.Add( go );
		}
	}

	private static MeshTopology GetTopology( MeshComponent component )
	{
		var mesh = component.Mesh;

		return new MeshTopology(
			mesh.VertexHandles.Count(),
			mesh.HalfEdgeHandles.Count() / 2,
			mesh.FaceHandles.Count()
		);
	}

	void UpdateSelectionMode()
	{
		if ( !Gizmo.HasMouseFocus ) return;

		if ( Gizmo.WasLeftMouseReleased && !Gizmo.Pressed.Any && !IsBoxSelecting )
		{
			using ( Scene.Editor?.UndoScope( "Deselect all" ).Push() )
			{
				EditorScene.Selection.Clear();
			}
		}
	}

	void UpdateHovered()
	{
		if ( IsBoxSelecting ) return;

		var tr = MeshTrace.Run();

		if ( !tr.Hit ) return;
		if ( tr.Component is not MeshComponent component ) return;

		using ( Gizmo.ObjectScope( tr.GameObject, tr.GameObject.WorldTransform ) )
		{
			Gizmo.Hitbox.DepthBias = 1;
			Gizmo.Hitbox.TrySetHovered( tr.Distance );

			if ( !Gizmo.IsHovered ) return;

			if ( component.IsValid() && component.Model.IsValid() && !Selection.Contains( tr.GameObject ) )
			{
				Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( MathF.Sin( RealTime.Now * 20.0f ).Remap( -1, 1, 0.3f, 0.8f ) );
				Gizmo.Draw.LineBBox( component.Model.Bounds );
			}
		}
	}

	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		var selection = new HashSet<GameObject>();
		var previous = new HashSet<GameObject>();

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			// GetAllObjects starts with the scene itself, which encloses everything
			if ( go == Scene ) continue;
			if ( go.Tags.Has( "hidden" ) ) continue;

			// Partial, otherwise you'd have to fit a whole block in the box to select it
			if ( frustum.IsInside( GetDragBounds( go ), true ) )
				selection.Add( go );
			else
				previous.Add( go );
		}

		ApplyDragSelection( selection, previous );
	}

	private void DrawBounds()
	{
		using ( Gizmo.Scope( "Bounds" ) )
		{
			var box = CalculateSelectionBounds();
			DimensionDisplay.DrawBounds( box );
		}
	}

	public override bool HasBoxSelectionMode() => true;

	static IReadOnlyList<Vector3> GetPivots( BBox box )
	{
		var mins = box.Mins;
		var maxs = box.Maxs;
		var center = box.Center;

		return
		[
			center,

			new Vector3( mins.x, mins.y, mins.z ),
			new Vector3( maxs.x, mins.y, mins.z ),
			new Vector3( mins.x, maxs.y, mins.z ),
			new Vector3( maxs.x, maxs.y, mins.z ),

			new Vector3( mins.x, mins.y, maxs.z ),
			new Vector3( maxs.x, mins.y, maxs.z ),
			new Vector3( mins.x, maxs.y, maxs.z ),
			new Vector3( maxs.x, maxs.y, maxs.z ),

			new Vector3( center.x, center.y, mins.z ),
			new Vector3( center.x, center.y, maxs.z ),
		];
	}

	int _pivotIndex = 0;

	void StepPivot( int direction )
	{
		var box = CalculateSelectionBounds();
		if ( box.Size.Length <= 0 ) return;

		var pivots = GetPivots( box );

		_pivotIndex = (_pivotIndex + direction + pivots.Count) % pivots.Count;
		Pivot = pivots[_pivotIndex];

		Tool?.MoveMode?.OnBegin( this );
	}

	public void PreviousPivot() => StepPivot( -1 );
	public void NextPivot() => StepPivot( 1 );

	public void ClearPivot()
	{
		Pivot = CalculateSelectionOrigin();
		_pivotIndex = 0;

		Tool?.MoveMode?.OnBegin( this );
	}

	public void ZeroPivot()
	{
		Pivot = default;
		_pivotIndex = 0;

		Tool?.MoveMode?.OnBegin( this );
	}

	public void CenterPivot()
	{
		var box = CalculateSelectionBounds();
		if ( box.Size.Length <= 0 ) return;

		_pivotIndex = 0;
		Pivot = box.Center;

		Tool?.MoveMode?.OnBegin( this );
	}

	public override void AlignDown( bool useLocalDown )
	{
		if ( useLocalDown )
			SceneEditorMenus.AlignToGroundLocal();
		else
			SceneEditorMenus.AlignToGround();
	}

	public override void AlignToClosestNormal()
	{
		SceneEditorMenus.AlignToClosestNormal();
	}
}
