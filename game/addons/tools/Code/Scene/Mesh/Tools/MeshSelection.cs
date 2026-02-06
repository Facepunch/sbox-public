
namespace Editor.MeshEditor;

/// <summary>
/// Select and edit mesh objects.
/// </summary>
[Title( "Mesh Selection" )]
[Icon( "layers" )]
[Alias( "tools.mesh-selection" )]
[Group( "5" )]
public sealed partial class MeshSelection( MeshTool tool ) : SelectionTool
{
	public MeshTool Tool { get; private init; } = tool;

	readonly Dictionary<MeshComponent, Transform> _startPoints = [];
	readonly Dictionary<MeshVertex, Vector3> _transformVertices = [];
	IDisposable _undoScope;

	MeshComponent[] _meshes = [];

	protected override void OnStartDrag()
	{
		if ( _startPoints.Count > 0 ) return;
		if ( _meshes.Length == 0 ) return;
		if ( _meshes.Any( x => !x.IsValid() ) ) return;

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
				.WithGameObjectChanges( _meshes.Select( x => x.GameObject ), GameObjectUndoFlags.Properties )
				.WithComponentChanges( _meshes )
				.Push();
		}

		foreach ( var mesh in _meshes )
		{
			_startPoints[mesh] = mesh.WorldTransform;

			foreach ( var vertex in mesh.Mesh.VertexHandles )
			{
				var v = new MeshVertex( mesh, vertex );
				_transformVertices[v] = mesh.WorldTransform.PointToWorld( mesh.Mesh.GetVertexPosition( vertex ) );
			}
		}
	}

	protected override void OnEndDrag()
	{
		_startPoints.Clear();

		_undoScope?.Dispose();
		_undoScope = null;
	}

	public override void Translate( Vector3 delta )
	{
		foreach ( var entry in _startPoints )
		{
			entry.Key.WorldPosition = entry.Value.Position + delta;
		}
	}

	public override void Rotate( Vector3 origin, Rotation basis, Rotation delta )
	{
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
		foreach ( var entry in _startPoints )
		{
			var position = entry.Value.Position - origin;
			position *= basis.Inverse;
			position *= deltaScale;
			position *= basis;
			position += origin;

			var scale = entry.Value.Scale * deltaScale;

			entry.Key.WorldTransform = new Transform(
				position,
				entry.Value.Rotation,
				scale
			);
		}
	}

	public override void Resize( Vector3 origin, Rotation basis, Vector3 scale )
	{
		foreach ( var startPoint in _startPoints )
		{
			var position = (startPoint.Value.Position - origin) * basis.Inverse;
			position *= scale;
			position *= basis;
			position += origin;

			var component = startPoint.Key;
			var transform = component.WorldTransform.WithPosition( position );
			component.Mesh.SetTransform( transform );
		}

		foreach ( var entry in _transformVertices )
		{
			var position = (entry.Value - origin) * basis.Inverse;
			position *= scale;
			position *= basis;
			position += origin;

			var transform = entry.Key.Component.Mesh.Transform;
			entry.Key.Component.Mesh.SetVertexPosition( entry.Key.Handle, transform.PointToLocal( position ) );
		}

		foreach ( var startPoint in _startPoints )
		{
			var component = startPoint.Key;
			component.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			component.WorldTransform = component.Mesh.Transform;
			component.RebuildMesh();
		}
	}

	public override void Nudge( Vector2 direction )
	{
		if ( _meshes.Length == 0 ) return;

		var viewport = SceneViewWidget.Current?.LastSelectedViewportWidget;
		if ( !viewport.IsValid() ) return;

		var gizmo = viewport.GizmoInstance;
		if ( gizmo is null ) return;

		using var gizmoScope = gizmo.Push();
		if ( Gizmo.Pressed.Any ) return;

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active.UndoScope( "Nudge Mesh(s)" )
			.WithGameObjectChanges( _meshes.Select( x => x.GameObject ), GameObjectUndoFlags.Properties )
			.Push();

		var rotation = CalculateSelectionBasis();
		var delta = Gizmo.Nudge( rotation, direction );

		Pivot -= delta;

		foreach ( var mesh in _meshes )
		{
			mesh.WorldPosition -= delta;
		}
	}

	public override BBox CalculateLocalBounds()
	{
		return CalculateSelectionBounds();
	}

	public override Rotation CalculateSelectionBasis()
	{
		if ( GlobalSpace ) return Rotation.Identity;

		var mesh = _meshes.FirstOrDefault();
		return mesh.IsValid() ? mesh.WorldRotation : Rotation.Identity;
	}

	public override void OnEnabled()
	{
		var objects = Selection.OfType<GameObject>()
			.Where( x => x.GetComponent<MeshComponent>().IsValid() )
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
		DrawBounds();
	}

	void UpdateMoveMode()
	{
		if ( Tool is null ) return;
		if ( Tool.MoveMode is null ) return;
		if ( _meshes.Length == 0 ) return;
		if ( _meshes.Any( x => !x.IsValid() ) ) return;

		Tool.MoveMode.Update( this );
	}

	public override Vector3 CalculateSelectionOrigin()
	{
		var mesh = _meshes.FirstOrDefault();
		return mesh.IsValid() ? mesh.WorldPosition : default;
	}

	public override BBox CalculateSelectionBounds()
	{
		return BBox.FromPoints( _transformVertices
			.Where( x => x.Key.IsValid() )
			.Select( x => x.Key.Transform.PointToWorld( x.Key.Component.Mesh.GetVertexPosition( x.Key.Handle ) ) ) );
	}

	public override void OnSelectionChanged()
	{
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

		Tool?.MoveMode?.OnBegin( this );
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

		if ( Gizmo.WasLeftMousePressed )
		{
			Select( tr.GameObject );
		}
	}

	void Select( GameObject element )
	{
		bool ctrl = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl );
		bool shift = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift );
		bool contains = Selection.Contains( element );

		if ( shift && contains ) return;

		using ( Scene.Editor?.UndoScope( "Select Mesh" ).Push() )
		{
			if ( ctrl )
			{
				if ( contains ) Selection.Remove( element );
				else Selection.Add( element );
			}
			else if ( shift )
			{
				Selection.Add( element );
			}
			else
			{
				Selection.Set( element );
			}
		}
	}

	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		var selection = new HashSet<GameObject>();
		var previous = new HashSet<GameObject>();

		bool fullyInside = true;
		bool removing = Gizmo.IsCtrlPressed;

		foreach ( var mr in Scene.GetAllComponents<MeshComponent>() )
		{
			var bounds = mr.GetWorldBounds();
			if ( !frustum.IsInside( bounds, !fullyInside ) )
			{
				previous.Add( mr.GameObject );
				continue;
			}

			selection.Add( mr.GameObject );
		}

		foreach ( var selectedObj in selection )
		{
			if ( !removing )
			{
				if ( Selection.Contains( selectedObj ) ) continue;

				Selection.Add( selectedObj );
			}
			else
			{
				if ( !Selection.Contains( selectedObj ) ) continue;

				Selection.Remove( selectedObj );
			}
		}

		foreach ( var removed in previous )
		{
			if ( removing )
			{
				Selection.Add( removed );
			}
			else
			{
				Selection.Remove( removed );
			}
		}
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
	}

	public void PreviousPivot() => StepPivot( -1 );
	public void NextPivot() => StepPivot( 1 );

	public void ClearPivot()
	{
		Pivot = CalculateSelectionOrigin();
		_pivotIndex = 0;
	}

	public void ZeroPivot()
	{
		Pivot = default;
		_pivotIndex = 0;
	}
}
