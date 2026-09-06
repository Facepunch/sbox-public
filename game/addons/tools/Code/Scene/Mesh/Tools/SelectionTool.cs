using HalfEdgeMesh;
using Sandbox.Helpers;

namespace Editor.MeshEditor;

public enum PivotSpace
{
	/// <summary>Position relative to the pivot.</summary>
	Local,

	/// <summary>Position in world coordinates.</summary>
	Global
}

public abstract class SelectionTool( MeshTool tool ) : EditorTool
{
	public MeshTool Tool { get; } = tool;

	protected TextureLockTransform _transformKind = TextureLockTransform.Move;

	protected enum TextureLockTransform
	{
		Move,
		Rotate,
		Scale,
	}
	protected virtual bool LockTextureOnMove => Tool.TextureLock;
	protected bool ShouldLockTexture()
	{
		if ( Tool is null )
			return false;

		return _transformKind switch
		{
			TextureLockTransform.Scale => Tool.TextureLockScale,
			TextureLockTransform.Rotate => Tool.TextureLock,
			_ => LockTextureOnMove,
		};
	}

	public virtual void SetMoveMode( MoveMode mode ) { }

	SelectionPivot _pivot;

	/// <summary>
	/// The point transforms happen around.
	/// </summary>
	public SelectionPivot Pivot => _pivot ??= new SelectionPivot( this );

	internal void OnPivotChanged() => Tool?.MoveMode?.OnBegin( this );

	protected void SubscribeUndo()
	{
		UnsubscribeUndo();

		Undo.OnUndo += OnUndoRedo;
		Undo.OnRedo += OnUndoRedo;
	}

	protected void UnsubscribeUndo()
	{
		Undo.OnUndo -= OnUndoRedo;
		Undo.OnRedo -= OnUndoRedo;
	}

	UndoSystem Undo => SceneEditorSession.Active.UndoSystem;

	void OnUndoRedo( UndoSystem.Entry _ )
	{
		OnAfterUndoRedo();

		Pivot.Update();

		OnPivotChanged();
	}

	protected virtual void OnAfterUndoRedo() { }

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
		if ( !DragStarted )
			Pivot.BeginDrag();

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

		// After OnEndDrag, so the transform's undo entry exists for the pivot move to fold into.
		Pivot.EndDrag();
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

	public virtual void Shear( Vector3 origin, Rotation basis, Vector3 shearAxis, Vector3 constraintAxis, float amount )
	{
	}

	public virtual void Nudge( Vector2 delta )
	{
	}

	public virtual void NudgeRotation( Vector2 direction )
	{
	}

	public virtual void AlignDown( bool useLocalDown )
	{
	}

	public virtual void AlignToClosestNormal()
	{
	}

	public override Widget CreateShortcutsWidget() => new SelectionToolShortcutsWidget( this );

	/// <summary>
	/// Stores the previous selection for each tool type so that re-entering the tool restores it.
	/// </summary>
	[SkipHotload]
	protected static readonly Dictionary<Type, SelectionSystem> PreviousSelections = [];

	/// <summary>
	/// Key used to store/restore previous selections. Tools sharing the same
	/// element type will share the same entry, keeping them in sync.
	/// </summary>
	protected virtual Type PreviousSelectionKey => GetType();

	public static IEnumerable<T> GetAllSelected<T>()
	{
		return PreviousSelections.Values
			.SelectMany( s => s )
			.OfType<T>()
			.Distinct();
	}

	/// <summary>
	/// Inject an element into the previous selection entry matching its type.
	/// </summary>
	public static void AddToPreviousSelections( object element )
	{
		var stored = PreviousSelections.GetOrCreate( element.GetType() );
		stored.Add( element );
	}

	public static void ClearPreviousSelections<T>()
	{
		if ( PreviousSelections.TryGetValue( typeof( T ), out var stored ) )
		{
			stored.Clear();
		}
	}

	protected void SaveCurrentSelection<T>() where T : IValid
	{
		var stored = PreviousSelections.GetOrCreate( PreviousSelectionKey );
		stored.Clear();

		foreach ( var element in Selection.OfType<T>().Where( x => x.IsValid() ) )
		{
			stored.Add( element );
		}
	}

	protected void RestorePreviousSelection<T>() where T : IValid
	{
		if ( !PreviousSelections.TryGetValue( PreviousSelectionKey, out var previousSelection ) )
			return;

		foreach ( var element in previousSelection.OfType<T>().Where( x => x.IsValid() ) )
		{
			Selection.Add( element );
		}
	}

	protected virtual bool ShowSelectionBoundsDefault => false;

	public bool ShowSelectionBounds
	{
		get => EditorCookie.Get( $"mesh.{GetType().Name}.show-selection-bounds", ShowSelectionBoundsDefault );
		set => EditorCookie.Set( $"mesh.{GetType().Name}.show-selection-bounds", value );
	}

	/// <summary>
	/// The space the selection position is displayed and edited in, shared by all selection modes.
	/// </summary>
	[Title( "Space" )]
	public PivotSpace PivotSpace
	{
		get => EditorCookie.Get( "mesh.pivot-space", PivotSpace.Global );
		set => EditorCookie.Set( "mesh.pivot-space", value );
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

	[Shortcut( "mesh.selection-rotate-up", "ALT+UP", typeof( SceneViewWidget ) )]
	public void RotateUp() => tool.NudgeRotation( Vector2.Up );

	[Shortcut( "mesh.selection-rotate-down", "ALT+DOWN", typeof( SceneViewWidget ) )]
	public void RotateDown() => tool.NudgeRotation( Vector2.Down );

	[Shortcut( "mesh.selection-rotate-left", "ALT+LEFT", typeof( SceneViewWidget ) )]
	public void RotateLeft() => tool.NudgeRotation( Vector2.Left );

	[Shortcut( "mesh.selection-rotate-right", "ALT+RIGHT", typeof( SceneViewWidget ) )]
	public void RotateRight() => tool.NudgeRotation( Vector2.Right );

	[Shortcut( "mesh.selection-rotate-duplicate-up", "ALT+SHIFT+UP", typeof( SceneViewWidget ) )]
	public void RotateDuplicateUp() => tool.NudgeRotation( Vector2.Up );

	[Shortcut( "mesh.selection-rotate-duplicate-down", "ALT+SHIFT+DOWN", typeof( SceneViewWidget ) )]
	public void RotateDuplicateDown() => tool.NudgeRotation( Vector2.Down );

	[Shortcut( "mesh.selection-rotate-duplicate-left", "ALT+SHIFT+LEFT", typeof( SceneViewWidget ) )]
	public void RotateDuplicateLeft() => tool.NudgeRotation( Vector2.Left );

	[Shortcut( "mesh.selection-rotate-duplicate-right", "ALT+SHIFT+RIGHT", typeof( SceneViewWidget ) )]
	public void RotateDuplicateRight() => tool.NudgeRotation( Vector2.Right );

	[Shortcut( "mesh.align-down-local", "CTRL+KP_1", typeof( SceneViewWidget ) )]
	public void AlignDownLocal() => tool.AlignDown( useLocalDown: true );

	[Shortcut( "mesh.align-down-world", "CTRL+KP_2", typeof( SceneViewWidget ) )]
	public void AlignDownWorld() => tool.AlignDown( useLocalDown: false );

	[Shortcut( "mesh.align-to-closest-normal", "CTRL+KP_3", typeof( SceneViewWidget ) )]
	public void AlignToClosestNormal() => tool.AlignToClosestNormal();
	[Shortcut( "mesh.toggle-selection-bounds", "/", typeof( SceneViewWidget ) )]
	public void ToggleSelectionBounds() => tool.ShowSelectionBounds = !tool.ShowSelectionBounds;
}

public abstract class SelectionTool<T>( MeshTool tool ) : SelectionTool( tool ) where T : IMeshElement
{
	protected override Type PreviousSelectionKey => typeof( T );
	readonly HashSet<MeshVertex> _vertexSelection = [];
	readonly Dictionary<MeshVertex, Vector3> _transformVertices = [];
	List<MeshFace> _transformFaces;
	IDisposable _undoScope;

	protected override bool LockTextureOnMove => Tool.TextureLockComponent;

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
		_transformKind = TextureLockTransform.Move;

		foreach ( var entry in _transformVertices )
		{
			var position = entry.Value + delta;
			var transform = entry.Key.Transform;
			entry.Key.Component.Mesh.SetVertexPosition( entry.Key.Handle, transform.PointToLocal( position ) );
		}

		Pivot.Drag( delta );
	}

	public override void Rotate( Vector3 origin, Rotation basis, Rotation delta )
	{
		_transformKind = TextureLockTransform.Rotate;

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
		_transformKind = TextureLockTransform.Scale;

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

	public override void Resize( Vector3 origin, Rotation basis, Vector3 scale )
	{
		Scale( origin, basis, scale );
	}

	public override void Shear( Vector3 origin, Rotation basis, Vector3 shearAxis, Vector3 constraintAxis, float amount )
	{
		_transformKind = TextureLockTransform.Move;

		foreach ( var entry in _transformVertices )
		{
			var position = (entry.Value - origin) * basis.Inverse;
			var constraintPosition = Vector3.Dot( position, constraintAxis );

			position += shearAxis * (constraintPosition * amount);
			position = position * basis + origin;

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

		RestorePreviousSelection<T>();
		SelectElements();
		CalculateSelectionVertices();
		Pivot.Reset();

		SubscribeUndo();
	}

	public override void OnDisabled()
	{
		Selection.OnItemAdded -= OnMeshSelectionChanged;
		Selection.OnItemRemoved -= OnMeshSelectionChanged;

		EndBoxSelectUndoScope();

		UnsubscribeUndo();

		SaveCurrentSelection<T>();
	}

	public bool IsAllowedToSelect => Tool?.MoveMode?.AllowSceneSelection ?? true;

	public override void BuildSceneContextMenu( Menu menu, Ray ray, SceneTraceResult? trace )
	{
		bool hasSelection = Selection.OfType<IMeshElement>().Any( x => x.IsValid() );

		if ( hasSelection )
		{
			menu.AddSeparator();

			var sel = menu.AddMenu( "Selection", "select_all" );
			AddMenuOption( sel, "Grow Selection (+)", "add", "mesh.grow-selection", true );
			AddMenuOption( sel, "Shrink Selection (-)", "remove", "mesh.shrink-selection", true );

			menu.AddSeparator();

			var transforms = menu.AddMenu( "Transforms", "open_with" );
			AddMenuOption( transforms, "Align Down Local", "vertical_align_bottom", "mesh.align-down-local", true );
			AddMenuOption( transforms, "Align Down World", "vertical_align_bottom", "mesh.align-down-world", true );
			AddMenuOption( transforms, "Align To Closest Normal", "swap_vert", "mesh.align-to-closest-normal", true );
		}

		menu.AddSeparator();
		menu.AddOption( "Lift Material", "colorize", () => LiftMaterialFromContextTrace( trace ), "mesh.lift-material" );
	}

	[Shortcut( "mesh.align-down-local", "CTRL+KP_1", typeof( SceneViewWidget ) )]
	private void AlignDownLocal()
	{
		AlignDown( useLocalDown: true );
	}

	[Shortcut( "mesh.align-down-world", "CTRL+KP_2", typeof( SceneViewWidget ) )]
	private void AlignDownWorld()
	{
		AlignDown( useLocalDown: false );
	}

	[Shortcut( "mesh.align-to-closest-normal", "CTRL+KP_3", typeof( SceneViewWidget ) )]
	public override void AlignToClosestNormal()
	{
		if ( !_vertexSelection.Any() )
			return;

		var components = _vertexSelection
			.Select( x => x.Component )
			.Distinct();

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active.UndoScope( "Align To Closest Normal" )
			.WithComponentChanges( components )
			.Push();

		foreach ( var vertex in _vertexSelection )
		{
			var transform = vertex.Transform;
			var worldPos = vertex.PositionWorld;
			var direction = transform.Rotation.Down;

			var trace = Scene.Trace
				.Ray( worldPos, worldPos + direction * 10000 )
				.WithoutTags( "trigger" )
				.UseRenderMeshes( true )
				.UsePhysicsWorld( false )
				.Run();

			if ( !trace.Hit )
				continue;

			vertex.Component.Mesh.SetVertexPosition( vertex.Handle, transform.PointToLocal( trace.HitPosition ) );
		}
	}

	public override void AlignDown( bool useLocalDown )
	{
		if ( !_vertexSelection.Any() )
			return;

		var components = _vertexSelection
			.Select( x => x.Component )
			.Distinct();

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active.UndoScope( "Align Down" )
			.WithComponentChanges( components )
			.Push();

		foreach ( var vertex in _vertexSelection )
		{
			var transform = vertex.Transform;
			var worldPos = vertex.PositionWorld;

			var direction = useLocalDown
				? transform.Rotation.Down
				: Vector3.Down;

			var trace = Scene.Trace
				.Ray( worldPos, worldPos + direction * 10000 )
				.WithoutTags( "trigger" )
				.UseRenderMeshes( true )
				.UsePhysicsWorld( false )
				.Run();

			if ( !trace.Hit )
				continue;

			vertex.Component.Mesh.SetVertexPosition( vertex.Handle, transform.PointToLocal( trace.HitPosition ) );
		}
	}

	public override void OnUpdate()
	{
		GlobalSpace = Gizmo.Settings.GlobalSpace;

		Pivot.Update();
		UpdateMoveMode();

		if ( !IsBoxSelecting )
		{
			EndBoxSelectUndoScope();
		}

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
			var previous = _vertexSelection.ToArray();

			CalculateSelectionVertices();

			// Undo restores the selection, firing add and remove for the same elements.
			// That isn't a change, so the pivot stays where it is.
			if ( !_vertexSelection.SetEquals( previous ) )
				Pivot.Reset();
		}

		HandleGlobalMaterialOperations();

		if ( IsAllowedToSelect )
			DrawSelection();
	}

	/// <summary>
	/// Handle Shift+RMB (pick material) and Ctrl+RMB (paint material) globally across all mesh tools.
	/// </summary>
	private void HandleGlobalMaterialOperations()
	{
		if ( Gizmo.WasRightMousePressed && Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
		{
			LiftMaterialFromHoveredFace();
		}

		if ( Gizmo.IsRightMouseDown && Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			var face = TraceFace();
			if ( face.IsValid() )
			{
				var mesh = face.Component.Mesh;
				var currentMaterial = mesh.GetFaceMaterial( face.Handle );
				if ( currentMaterial != Tool.ActiveMaterial )
				{
					using ( SceneEditorSession.Active.UndoScope( "Paint Material" )
						.WithComponentChanges( face.Component )
						.Push() )
					{
						mesh.SetFaceMaterial( face.Handle, Tool.ActiveMaterial );
					}
				}
			}
		}
	}

	private void LiftMaterialFromHoveredFace()
	{
		var face = TraceFace();
		if ( face.IsValid() )
		{
			Tool.ActiveMaterial = face.Material;
		}
	}

	[Shortcut( "mesh.lift-material", "SHIFT+RMB", typeof( SceneViewWidget ) )]
	private void LiftMaterial()
	{
		LiftMaterialFromHoveredFace();
	}

	private void LiftMaterialFromContextTrace( SceneTraceResult? trace )
	{
		if ( trace is not { Hit: true } hit )
			return;

		if ( hit.Component is not MeshComponent component || component.Mesh is null )
			return;

		var face = new MeshFace( component, component.Mesh.TriangleToFace( hit.Triangle ) );
		if ( face.IsValid() )
		{
			Tool.ActiveMaterial = face.Material;
		}
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
				Gizmo.Draw.Lines( mesh.Mesh.GetVisibleEdges() );

				Gizmo.Draw.Color = edgeColor;
				Gizmo.Draw.IgnoreDepth = false;
				Gizmo.Draw.LineThickness = 2;
				Gizmo.Draw.Lines( mesh.Mesh.GetVisibleEdges() );
			}

			if ( DrawVertices )
			{
				var vertexColor = new Color( 1.0f, 1.0f, 0.3f, 1f );

				using ( Gizmo.Scope( "Vertices" ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = vertexColor.Darken( 0.3f ).WithAlpha( 0.2f );

					foreach ( var v in mesh.Mesh.GetVisibleVertexPositions() )
					{
						Gizmo.Draw.Sprite( v, 8, null, false );
					}

					Gizmo.Draw.Color = vertexColor;
					Gizmo.Draw.IgnoreDepth = false;

					foreach ( var v in mesh.Mesh.GetVisibleVertexPositions() )
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

	[Shortcut( "mesh.invert-selection", "CTRL+I", typeof( SceneViewWidget ) )]
	protected void InvertCurrentSelection() => InvertSelection();

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

		var rotation = CalculateSelectionBasis();
		var delta = Gizmo.Nudge( rotation, direction );

		Pivot.BeginDrag();

		using ( SceneEditorSession.Active.UndoScope( "Nudge Vertices" ).WithComponentChanges( components ).Push() )
		{
			if ( Gizmo.IsShiftPressed )
			{
				ExtrudeSelection( -delta );
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

				UpdateTexturesAfterNudge();
			}

			Pivot.Translate( -delta );
		}

		Pivot.EndDrag();

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
		var screenRight = -Gizmo.Nudge( basis, Vector2.Right ).Normal;
		var screenUp = -Gizmo.Nudge( basis, Vector2.Up ).Normal;
		var faceNormal = screenRight.Cross( screenUp ).Normal;

		var axis = direction.x != 0.0f
			? faceNormal
			: screenRight;

		var angle = direction.x != 0.0f
			? direction.x * Gizmo.Settings.AngleSpacing
			: -direction.y * Gizmo.Settings.AngleSpacing;

		var delta = Rotation.FromAxis( axis, angle );

		StartDrag();

		try
		{
			Rotate( Pivot.Position, Rotation.Identity, delta );
			UpdateDrag();
		}
		finally
		{
			EndDrag();
		}

		Tool?.MoveMode?.OnBegin( this );
	}

	private void UpdateTexturesAfterNudge()
	{
		if ( !ShouldLockTexture() )
		{
			foreach ( var mesh in _vertexSelection.Select( x => x.Component.Mesh ).Distinct() )
			{
				mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			return;
		}

		foreach ( var group in _vertexSelection.GroupBy( x => x.Component ) )
		{
			var mesh = group.Key.Mesh;
			var faces = new HashSet<FaceHandle>();

			foreach ( var vertex in group )
			{
				if ( mesh.GetFacesConnectedToVertex( vertex.Handle, out var connected ) )
				{
					foreach ( var face in connected )
						faces.Add( face );
				}
			}

			if ( faces.Count > 0 )
				mesh.ComputeFaceTextureParametersFromCoordinates( faces );
		}
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

	/// <summary>
	/// Selection position, relative to the pivot in <see cref="PivotSpace.Local"/> space.
	/// Setting it moves the selection; the pivot stays put, acting as the reference point.
	/// </summary>
	[Title( "Position" ), WideMode]
	public Vector3 PivotPosition
	{
		get => CalculateSelectionOrigin() - PivotOrigin;
		set => MoveSelection( PivotOrigin + value - CalculateSelectionOrigin() );
	}

	Vector3 PivotOrigin => PivotSpace == PivotSpace.Local ? Pivot.Position : Vector3.Zero;

	/// <summary>
	/// Move the selected vertices by a world space delta. The undo scope is held open
	/// until <see cref="EndMoveSelection"/> so a drag records a single undo entry.
	/// </summary>
	public void MoveSelection( Vector3 delta )
	{
		if ( _vertexSelection.Count == 0 || delta.LengthSquared < 0.0001f )
			return;

		var components = _vertexSelection.Select( v => v.Component ).Distinct();

		using var scope = SceneEditorSession.Scope();
		_undoScope ??= SceneEditorSession.Active.UndoScope( "Move Selection" ).WithComponentChanges( components ).Push();

		foreach ( var vertex in _vertexSelection )
			vertex.Component.Mesh.SetVertexPosition( vertex.Handle, vertex.Transform.PointToLocal( vertex.PositionWorld + delta ) );

		Tool?.MoveMode?.OnBegin( this );
	}

	public void EndMoveSelection()
	{
		_undoScope?.Dispose();
		_undoScope = null;
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

	protected void Select( IMeshElement element )
	{
		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			using var scope = SceneEditorSession.Active
				.UndoScope( "Update Selection" ).Push();

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
				using var scope = SceneEditorSession.Active
					.UndoScope( "Update Selection" ).Push();

				Selection.Add( element );
			}

			return;
		}

		if ( !Selection.Contains( element ) || Selection.Count != 1 )
		{
			using var scope = SceneEditorSession.Active
				.UndoScope( "Update Selection" ).Push();

			Selection.Set( element );
		}
	}

	IDisposable _selectionUndoScope;
	IMeshElement _pressedElement;

	public void UpdateSelection( IMeshElement element )
	{
		if ( Tool?.MoveMode?.AllowSceneSelection == false )
			return;

		if ( Gizmo.WasLeftMousePressed )
		{
			_pressedElement = element;

			if ( element.IsValid() )
			{
				Select( element );
			}
			else if ( !IsMultiSelecting && Selection.Any() )
			{
				using var scope = SceneEditorSession.Active
					.UndoScope( "Update Selection" ).Push();

				Selection.Clear();
			}

			return;
		}

		if ( Gizmo.IsLeftMouseDown )
		{
			// The press already selected this one, painting over it again would just undo that
			if ( element.IsValid() && !Equals( element, _pressedElement ) )
			{
				if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
				{
					if ( Selection.Contains( element ) )
					{
						_selectionUndoScope ??= SceneEditorSession.Active
							.UndoScope( "Update Selection" ).Push();

						Selection.Remove( element );
					}
				}
				else
				{
					if ( !Selection.Contains( element ) )
					{
						_selectionUndoScope ??= SceneEditorSession.Active
							.UndoScope( "Update Selection" ).Push();

						Selection.Add( element );
					}
				}
			}

			return;
		}

		_selectionUndoScope?.Dispose();
		_selectionUndoScope = null;
		_pressedElement = null;
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
		var sideFaces = new Dictionary<MeshComponent, HashSet<FaceHandle>>();
		if ( _transformFaces is not null )
		{
			var extrudedFaces = Selection.OfType<MeshFace>()
				.GroupBy( x => x.Component )
				.ToDictionary( g => g.Key, g => g.Select( x => x.Handle ).ToHashSet() );

			foreach ( var group in _transformFaces.GroupBy( x => x.Component ) )
			{
				var mesh = group.Key.Mesh;
				var handles = group.Select( x => x.Handle ).ToHashSet();

				var exclude = new HashSet<FaceHandle>( handles );
				if ( extrudedFaces.TryGetValue( group.Key, out var newFaces ) )
					exclude.UnionWith( newFaces );

				foreach ( var face in group )
				{
					if ( !mesh.TextureWrapFromNeighbour( face.Handle, exclude ) )
						mesh.TextureAlignToGrid( mesh.Transform, face.Handle );
				}

				sideFaces[group.Key] = handles;
			}
		}

		if ( !ShouldLockTexture() )
		{
			foreach ( var mesh in _transformVertices.Select( x => x.Key.Component.Mesh ).Distinct() )
			{
				mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			return;
		}

		foreach ( var group in _transformVertices.Keys.GroupBy( x => x.Component ) )
		{
			var mesh = group.Key.Mesh;
			var faces = new HashSet<FaceHandle>();

			foreach ( var vertex in group )
			{
				if ( mesh.GetFacesConnectedToVertex( vertex.Handle, out var connected ) )
				{
					foreach ( var face in connected )
						faces.Add( face );
				}
			}

			if ( sideFaces.TryGetValue( group.Key, out var excluded ) )
				faces.ExceptWith( excluded );

			if ( faces.Count > 0 )
				mesh.ComputeFaceTextureParametersFromCoordinates( faces );
		}
	}

	protected override void OnEndDrag()
	{
		_transformVertices.Clear();
		_transformFaces = null;
		_transformKind = TextureLockTransform.Move;

		_undoScope?.Dispose();
		_undoScope = null;
	}

	public MeshFace TraceFace()
	{
		if ( IsBoxSelecting || IsLassoSelecting )
			return default;

		return MeshTrace.TraceFace();
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

	public override bool HasBoxSelectionMode() => true;

	protected override void OnLassoSelect( List<Vector2> lassoPoints, bool isFinal )
	{
		if ( !isFinal ) return;

		using var undoScope = SceneEditorSession.Active.UndoScope( "Lasso Selection" ).Push();

		LassoSelection( lassoPoints );
	}

	IDisposable _boxSelectUndoScope;

	void EndBoxSelectUndoScope()
	{
		_boxSelectUndoScope?.Dispose();
		_boxSelectUndoScope = null;
	}

	// A box is just a four point lasso, so vertices, edges and faces all select the same way. This
	// runs every frame of the drag, so hold a single undo scope open for the whole thing
	protected override void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{
		_boxSelectUndoScope ??= SceneEditorSession.Active.UndoScope( "Box Selection" ).Push();

		LassoSelection( [screenRect.TopLeft, screenRect.TopRight, screenRect.BottomRight, screenRect.BottomLeft] );
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

		// previous only exists to drop things that are already selected, so meshes the lasso can't
		// reach don't need their topology walked at all
		var selectedElements = Selection.OfType<T>().ToLookup( x => x.Component );

		foreach ( var component in Scene.GetAllComponents<MeshComponent>() )
		{
			var mesh = component.Mesh;
			if ( mesh == null ) continue;

			if ( component.GameObject.Tags.Has( "hidden" ) ) continue;

			var worldBounds = component.GetWorldBounds();
			var meshScreenBounds = GetScreenRectFromBounds( worldBounds );

			if ( !lassoBounds.IsInside( meshScreenBounds ) )
			{
				foreach ( var element in selectedElements[component] )
					previous.Add( element );

				continue;
			}

			var transform = component.Transform.World;

			if ( typeof( T ) == typeof( MeshVertex ) )
			{
				foreach ( var h in mesh.VertexHandles )
				{
					if ( mesh.IsVertexHidden( h ) )
						continue;

					var worldPos = transform.PointToWorld( mesh.GetVertexPosition( h ) );
					var vertex = (T)(object)new MeshVertex( component, h );

					var toVertex = worldPos - cameraPos;
					if ( Vector3.Dot( toVertex, cameraForward ) < 0.1f )
					{
						previous.Add( vertex );
						continue;
					}

					var screenPos = Gizmo.Camera.ToScreen( worldPos );

					if ( !lassoBounds.IsInside( screenPos ) || !IsPointInLasso( screenPos, lassoPoints ) )
					{
						previous.Add( vertex );
						continue;
					}

					// An occlusion trace is expensive, only pay for it once we know it's a candidate
					if ( !Tool.SelectionThrough && IsVertexOccluded( worldPos, cameraPos ) )
						previous.Add( vertex );
					else
						selection.Add( vertex );
				}
			}
			else if ( typeof( T ) == typeof( MeshEdge ) )
			{
				foreach ( var h in mesh.HalfEdgeHandles )
				{
					if ( h.Index > mesh.GetOppositeHalfEdge( h ).Index )
						continue;

					if ( mesh.IsEdgeHidden( h ) )
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

					var screenPosA = Gizmo.Camera.ToScreen( worldPosA );
					var screenPosB = Gizmo.Camera.ToScreen( worldPosB );

					bool aInLasso = aInFront && lassoBounds.IsInside( screenPosA ) && IsPointInLasso( screenPosA, lassoPoints );
					bool bInLasso = bInFront && lassoBounds.IsInside( screenPosB ) && IsPointInLasso( screenPosB, lassoPoints );

					// Occlusion traces are expensive, only pay for them once we know it's a candidate
					if ( !Tool.SelectionThrough )
					{
						if ( aInLasso && IsVertexOccluded( worldPosA, cameraPos ) )
							aInLasso = false;

						if ( bInLasso && IsVertexOccluded( worldPosB, cameraPos ) )
							bInLasso = false;
					}

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
					if ( mesh.IsFaceHidden( h ) )
						continue;

					mesh.GetVerticesConnectedToFace( h, out var vertices );
					var face = (T)(object)new MeshFace( component, h );

					// A face with nothing in the lasso is never selected, whatever the occlusion
					// traces below would say, so skip them entirely
					var anyInLasso = false;

					foreach ( var v in vertices )
					{
						if ( !IsInLasso( transform.PointToWorld( mesh.GetVertexPosition( v ) ) ) ) continue;

						anyInLasso = true;
						break;
					}

					if ( !anyInLasso )
					{
						previous.Add( face );
						continue;
					}

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

		ApplyDragSelection( selection, previous );

		bool IsInLasso( Vector3 worldPos )
		{
			if ( Vector3.Dot( worldPos - cameraPos, cameraForward ) <= 0 )
				return false;

			var screenPos = Gizmo.Camera.ToScreen( worldPos );
			return lassoBounds.IsInside( screenPos ) && IsPointInLasso( screenPos, lassoPoints );
		}
	}

	private Rect GetScreenRectFromBounds( BBox bounds )
	{
		var min = new Vector2( float.MaxValue, float.MaxValue );
		var max = new Vector2( float.MinValue, float.MinValue );

		foreach ( var corner in bounds.Corners )
		{
			var screenPos = Gizmo.Camera.ToScreen( corner );
			min = Vector2.Min( min, screenPos );
			max = Vector2.Max( max, screenPos );
		}

		return new Rect( min.x, min.y, max.x - min.x, max.y - min.y );
	}

	protected void WrapTextureToSelection( MeshFace sourceFace )
	{
		var faces = Selection.OfType<MeshFace>().ToArray();
		if ( faces.Length == 0 ) return;

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active.UndoScope( "Wrap Texture To Selection" )
			.WithComponentChanges( faces.Select( x => x.Component ).Distinct() )
			.Push();

		foreach ( var face in faces )
		{
			WrapTexture( sourceFace, face );
		}
	}

	protected void WrapTexture( MeshFace targetFace )
	{
		if ( !targetFace.IsValid() || Selection.LastOrDefault() is not MeshFace sourceFace )
			return;

		using var scope = SceneEditorSession.Scope();
		using var undoScope = SceneEditorSession.Active.UndoScope( "Wrap Texture" )
			.WithComponentChanges( [targetFace.Component] )
			.Push();

		WrapTexture( sourceFace, targetFace );
	}

	static void WrapTexture( MeshFace sourceFace, MeshFace targetFace )
	{
		if ( !sourceFace.IsValid() )
			return;

		if ( !targetFace.IsValid() )
			return;

		var sourceMesh = sourceFace.Component.Mesh;
		var targetMesh = targetFace.Component.Mesh;

		targetFace.Material = sourceFace.Material;
		sourceMesh.GetFaceTextureParameters( sourceFace.Handle, out var vAxisU, out var vAxisV, out var vScale );

		PolygonMesh.GetBestPlanesForEdgeBetweenFaces( sourceMesh, sourceFace.Handle, sourceFace.Transform,
			targetMesh, targetFace.Handle, targetFace.Transform,
			out var fromPlane, out var toPlane );

		RotateTextureCoordinatesAroundEdge( fromPlane, toPlane, ref vAxisU, ref vAxisV, vScale );

		targetMesh.SetFaceTextureParameters( targetFace.Handle, vAxisU, vAxisV, vScale );
	}

	static void RotateTextureCoordinatesAroundEdge( Plane fromPlane, Plane toPlane, ref Vector4 pInOutAxisU, ref Vector4 pInOutAxisV, Vector2 scale )
	{
		Vector3 vAxisUOld = (Vector3)pInOutAxisU;
		Vector3 vAxisVOld = (Vector3)pInOutAxisV;
		var flShiftUOld = pInOutAxisU.w * scale.x;
		var flShiftVOld = pInOutAxisV.w * scale.y;

		var vEdge = fromPlane.Normal.Cross( toPlane.Normal ).Normal;
		var vEdgePoint = Plane.GetIntersection( fromPlane, toPlane, new Plane( vEdge, 0.0f ) );

		var vAxisUNew = vAxisUOld;
		var vAxisVNew = vAxisVOld;
		var flShiftUNew = flShiftUOld;
		var flShiftVNew = flShiftVOld;

		if ( vEdgePoint.HasValue )
		{
			var vProjFromNormal = fromPlane.Normal - vEdge * vEdge.Dot( fromPlane.Normal );
			var vProjToNormal = toPlane.Normal - vEdge * vEdge.Dot( toPlane.Normal );

			vProjFromNormal = vProjFromNormal.Normal;
			vProjToNormal = vProjToNormal.Normal;

			var flPlanesDot = vProjFromNormal.Dot( vProjToNormal ).Clamp( -1.0f, 1.0f );
			var flRotationAngle = MathF.Acos( flPlanesDot ) * (180.0f / System.MathF.PI);

			if ( flPlanesDot < 0.0f )
			{
				flRotationAngle = 180.0f - flRotationAngle;
			}

			var mEdgeRotation = Rotation.FromAxis( vEdge, flRotationAngle );
			vAxisUNew = vAxisUOld * mEdgeRotation;
			vAxisVNew = vAxisVOld * mEdgeRotation;

			var edgePoint = vEdgePoint.Value;
			var flPointU = (Vector3.Dot( vAxisUOld, edgePoint ) + flShiftUOld) / scale.x;
			var flPointV = (Vector3.Dot( vAxisVOld, edgePoint ) + flShiftVOld) / scale.y;

			var flNewPointUnshiftedU = Vector3.Dot( vAxisUNew, edgePoint ) / scale.x;
			var flNewPointUnshiftedV = Vector3.Dot( vAxisVNew, edgePoint ) / scale.y;

			var flNeededShiftU = flPointU - flNewPointUnshiftedU;
			var flNeededShiftV = flPointV - flNewPointUnshiftedV;

			flShiftUNew = flNeededShiftU * scale.x;
			flShiftVNew = flNeededShiftV * scale.y;
		}

		pInOutAxisU = new Vector4( vAxisUNew, flShiftUNew / scale.x );
		pInOutAxisV = new Vector4( vAxisVNew, flShiftVNew / scale.y );
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

public static class SelectionToolSidebarExtensions
{
	/// <summary>
	/// Add a "Transform" group for moving the selection relative to its pivot.
	/// </summary>
	public static void AddPivotGroup<T>( this ToolSidebarWidget sidebar, SelectionTool<T> tool ) where T : IMeshElement
	{
		var group = sidebar.AddGroup( "Transform", collapsible: true );
		var hasSelection = tool.Selection.OfType<T>().Any();

		var so = tool.GetSerialized();
		so.OnPropertyFinishEdit += _ => tool.EndMoveSelection();

		var position = ControlWidget.Create( so.GetProperty( nameof( SelectionTool<T>.PivotPosition ) ) );
		position.FixedHeight = Theme.ControlHeight;
		position.Enabled = hasSelection;
		group.Add( position );

		var space = ControlWidget.Create( so.GetProperty( nameof( SelectionTool.PivotSpace ) ) );
		space.Enabled = hasSelection;
		group.Add( space );
	}

	/// <summary>
	/// Add a "Pivot" group for moving the selection's pivot around.
	/// </summary>
	public static void AddPivotButtons( this ToolSidebarWidget sidebar, SelectionTool tool, bool enabled )
	{
		var group = sidebar.AddGroup( "Pivot" );
		group.Add( new PivotButtonsWidget( sidebar, tool, enabled ) );
	}
}
