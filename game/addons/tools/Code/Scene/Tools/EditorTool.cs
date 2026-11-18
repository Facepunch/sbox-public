using System.Text.Json.Serialization;

namespace Editor;

public class EditorTool : IDisposable
{
	[JsonIgnore]
	public EditorToolManager Manager { get; internal set; }
	[JsonIgnore]
	public SelectionSystem Selection => Manager.CurrentSession.Selection;
	[JsonIgnore]
	public Scene Scene => Manager.CurrentSession.Scene;
	[JsonIgnore]
	public Widget SceneOverlay => SceneOverlayWidget.Active;
	[JsonIgnore]
	public CameraComponent Camera { get; private set; }

	List<Widget> overlayWidgets = new();
	List<EditorTool> _tools = new();

	public IEnumerable<EditorTool> Tools => _tools;

	/// <summary>
	/// If true well recreate the sidebar when the selection changes. This is really useful if your tool's sidebar depends on the selection.
	/// But also it can be a waste of energy if your tool's sidebar is static.
	/// </summary>
	public bool RebuildSidebarOnSelectionChange { get; set; } = true;

	private EditorTool _currentTool;
	[JsonIgnore]
	public EditorTool CurrentTool
	{
		get => _currentTool;
		set
		{
			if ( _currentTool == value )
				return;

			_currentTool?.CancelBoxSelection();
			_currentTool?.OnDisabled();
			_currentTool = value;
			_currentTool?.OnEnabled();

			EditorToolManager.SetSubTool( _currentTool?.GetType().Name );
		}
	}

	/// <summary>
	/// Create a scene trace against the current scene, using the current mouse cursor
	/// </summary>
	[JsonIgnore]
	public SceneTrace Trace => Scene.Trace.Ray( Gizmo.CurrentRay, Gizmo.RayDepth );

	/// <summary>
	/// Create a trace that traces against the render meshes but not the physics world, using the current mouse cursor
	/// </summary>
	[JsonIgnore]
	public SceneTrace MeshTrace => Scene.Trace.Ray( Gizmo.Hitbox.Ray, Gizmo.RayDepth ).UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
										.WithoutTags( "hidden" )
										.UsePhysicsWorld( false );


	/// <summary>
	/// Return the selected component of type
	/// </summary>
	protected T GetSelectedComponent<T>() where T : Component
	{
		return Selection.OfType<GameObject>().Select( x => x.Components.Get<T>() ).FirstOrDefault();
	}

	/// <summary>
	/// if true then regular scene object selection will apply
	/// </summary>
	public bool AllowGameObjectSelection { get; set; } = true;

	/// <summary>
	/// allow context menu or not, some tools don't need it.
	/// </summary>
	public bool AllowContextMenu { get; set; } = true;

	public virtual bool TryGetSelectionOrigin( out Vector3 origin )
	{
		origin = default;
		return false;
	}

	internal void InitializeInternal( EditorToolManager manager )
	{
		Manager = manager;
		OnEnabled();

		CreateTools();
	}

	private void CreateTools()
	{
		_tools.Clear();

		var tools = GetSubtools();
		if ( tools == null )
			return;

		foreach ( var tool in tools )
		{
			if ( tool is null )
				continue;

			tool.Manager = Manager;
			_tools.Add( tool );
		}

		CurrentTool = _tools.FirstOrDefault();
	}

	internal void Frame( CameraComponent camera )
	{
		Camera = camera;

		try
		{
			if ( Manager.IsCurrentViewFocused )
			{
				if ( HasLassoSelectionMode() && (IsLassoSelecting || (Gizmo.IsAltPressed && (Gizmo.IsShiftPressed || Gizmo.IsCtrlPressed) && Gizmo.IsLeftMouseDown && Gizmo.Pressed.CursorDelta.Length > 0.1f)) )
				{
					UpdateLassoSelection();
				}
				else if ( HasBoxSelectionMode() )
				{
					UpdateBoxSelection();
				}
			}


			OnUpdate();
			CurrentTool?.Frame( camera );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"{this}.OnUpdate exception: {e.Message}" );
		}
	}

	public virtual void OnUpdate()
	{

	}

	public virtual void OnEnabled()
	{

	}

	public virtual void OnDisabled()
	{

	}

	public virtual void OnSelectionChanged()
	{

	}

	/// <summary>
	/// Allows tools to contribute custom items to the scene viewport context menu.
	/// </summary>
	public virtual void BuildSceneContextMenu( Menu menu, Ray ray, SceneTraceResult? trace )
	{
	}

	protected static void AddMenuOption( Menu m, string name, string icon, string shortcut, bool condition )
	{
		if ( !condition ) return;
		m.AddOption( name, icon, () => InvokeShortcut( shortcut ), shortcut );
	}

	protected static void AddMenuOption( Menu m, string name, string icon, Action action, string shortcut, bool condition )
	{
		if ( !condition ) return;
		m.AddOption( name, icon, action, shortcut );
	}

	internal static bool InvokeShortcut( string identifier )
	{
		foreach ( var shortcut in EditorShortcuts.Entries )
		{
			if ( shortcut.Identifier != identifier ) continue;
			if ( shortcut.Invoke( true ) ) return true;
		}

		return false;
	}

	/// <summary>
	/// Return true here to keep the tool active even if the component is no longer
	/// in the selection.
	/// </summary>
	/// <returns></returns>
	public virtual bool ShouldKeepActive()
	{
		return false;
	}

	public virtual void Dispose()
	{
		CancelBoxSelection();
		OnDisabled();

		foreach ( var w in overlayWidgets )
		{
			if ( w.IsValid() ) w.Destroy();
		}

		overlayWidgets.Clear();

		foreach ( var tool in _tools )
		{
			tool?.Dispose();
		}

		_tools.Clear();
	}

	/// <summary>
	/// Return any tools that this tool wants to use
	/// </summary>
	public virtual IEnumerable<EditorTool> GetSubtools()
	{
		return default;
	}

	[Obsolete]
	protected void EditLog( string v, IEnumerable<object> targets )
	{
		SceneEditorSession.Active.Scene.EditLog( v, this );
	}

	/// <summary>
	/// Duplicates the selected objects and selects the duplicated set
	/// </summary>
	protected void DuplicateSelection()
	{
		SceneEditorMenus.DuplicateInternal();
	}

	public void AddOverlay( Widget widget, TextFlag align = TextFlag.RightTop, Vector2 offset = default )
	{
		widget.Parent = SceneOverlay;

		overlayWidgets.Add( widget );

		widget.AdjustSize();
		widget.AlignToParent( align, offset );
		widget.Show();
	}

	/// <summary>
	/// Create a widget with the intention to create shortcut keys for this tool (and its parents)
	/// </summary>
	public virtual Widget CreateShortcutsWidget()
	{
		return null;
	}

	/// <summary>
	/// Create a widget for this tool to be added next to the left toolbar.
	/// </summary>
	public virtual Widget CreateToolSidebar()
	{
		return null;
	}

	/// <summary>
	/// Create a widget to be added to the viewport toolbar when this tool is active.
	/// This allows tools to add custom controls, options, or information to the main toolbar.
	/// </summary>
	public virtual Widget CreateToolbarWidget()
	{
		return null;
	}

	/// <summary>
	/// Create a widget for this tool to be added at the bottom of the tools
	/// </summary>
	public virtual Widget CreateToolFooter()
	{
		return null;
	}

	/// <summary>
	/// Get the current selection as a SerializedObject
	/// </summary>
	public SerializedObject GetSerializedSelection()
	{
		var o = Selection.ToArray();
		SerializedObject so;

		if ( o.Length == 0 )
		{
			so = o.GetSerialized();
		}
		else if ( o.Length == 1 )
		{
			so = o.GetValue( 0 )?.GetSerialized();
		}
		else
		{
			var mo = new MultiSerializedObject();

			for ( int i = 0; i < o.Length; i++ )
			{
				var val = o?.GetValue( i );
				if ( val is null ) continue;
				mo.Add( val.GetSerialized() );
			}

			mo.Rebuild();
			so = mo;
		}

		return so;
	}

	/// <summary>
	/// If true then this mode uses box selection
	/// </summary>
	public virtual bool HasBoxSelectionMode() => false;

	/// <summary>
	/// If true then this mode uses lasso selection (Alt+Shift+Drag to add, Alt+Ctrl+Drag to remove)
	/// </summary>
	public virtual bool HasLassoSelectionMode() => false;

	Vector2? _boxStart;
	List<Vector2> _lassoPoints = new();

	protected bool IsBoxSelecting { get; private set; }
	protected bool IsLassoSelecting { get; private set; }

	internal bool CancelBoxSelection()
	{
		var canceled = _boxStart.HasValue || IsBoxSelecting;
		_boxStart = null;
		// Suppress the background-click deselection when the canceled drag is released.
		IsBoxSelecting = canceled;
		return CurrentTool?.CancelBoxSelection() == true || canceled;
	}

	private void UpdateBoxSelection()
	{
		if ( Gizmo.WasLeftMousePressed )
		{
			_boxStart = Gizmo.Pressed.CursorPosition;
			IsBoxSelecting = false;
		}
		if ( !Gizmo.IsLeftMouseDown && !Gizmo.WasLeftMouseReleased )
		{
			_boxStart = null;
			IsBoxSelecting = false;
		}
		if ( _boxStart is not { } start ) return;
		if ( Gizmo.Pressed.Any )
		{
			// Object clicks relinquish capture after dragging. Keep the original start for that transition.
			if ( !Gizmo.IsLeftMouseDown ) _boxStart = null;
			IsBoxSelecting = false;
			return;
		}

		var end = Gizmo.CursorPosition;
		if ( !start.IsFinite || !end.IsFinite )
		{
			_boxStart = null;
			IsBoxSelecting = false;
			return;
		}
		if ( !IsBoxSelecting && Vector2.Distance( start, end ) < 5 )
		{
			if ( !Gizmo.IsLeftMouseDown ) _boxStart = null;
			return;
		}

		IsBoxSelecting = true;
		// Input positions are widget coordinates; the camera and overlays use physical pixels.
		var dpiScale = MathF.Max( 1.0f, Gizmo.Camera.Attributes.GetFloat( "EditorGizmoDpiScale" ) );
		var rect = Rect.FromPoints( start, end ) * dpiScale;
		// A line has no selectable area. Do not construct a degenerate frustum.
		if ( rect.Width > 0 && rect.Height > 0 )
			OnBoxSelect( Gizmo.Camera.GetFrustum( rect ), rect, Gizmo.WasLeftMouseReleased );

		if ( Gizmo.WasLeftMouseReleased )
		{
			_boxStart = null;
			return;
		}

		var color = Gizmo.IsCtrlPressed ? (Gizmo.IsShiftPressed ? Theme.Yellow : Theme.Red) : Gizmo.IsShiftPressed ? Theme.Green : Theme.Blue;
		Gizmo.Draw.ScreenRect( rect, color.WithAlpha( 0.1f ), new Vector4( 1.0f ), color, new Vector4( 1.0f ) );
	}

	private void UpdateLassoSelection()
	{
		if ( Gizmo.IsLeftMouseDown && !IsLassoSelecting )
		{
			_lassoPoints.Clear();
			IsLassoSelecting = true;
		}

		if ( Gizmo.IsLeftMouseDown && IsLassoSelecting )
		{
			var currentPoint = Gizmo.Camera.ToScreen( Gizmo.CurrentRay.Project( 100 ) );

			if ( _lassoPoints.Count == 0 || Vector2.Distance( _lassoPoints[^1], currentPoint ) > 5 )
			{
				_lassoPoints.Add( currentPoint );
			}
		}

		if ( Gizmo.WasLeftMouseReleased && IsLassoSelecting )
		{
			if ( _lassoPoints.Count > 2 )
			{
				OnLassoSelect( _lassoPoints, true );
			}

			_lassoPoints.Clear();
			IsLassoSelecting = false;
			return;
		}

		if ( IsLassoSelecting && _lassoPoints.Count > 1 )
		{
			Color lassoColor;
			if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
			{
				lassoColor = Theme.Red;
			}
			else if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			{
				lassoColor = Theme.Green;
			}
			else
			{
				lassoColor = Theme.Blue;
			}

			DrawLasso( _lassoPoints, true, lassoColor );
		}
	}

	private void DrawLasso( List<Vector2> points, bool isClosed, Color color )
	{
		if ( points.Count < 2 )
			return;

		using ( Gizmo.Scope( "Lasso" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = color;
			Gizmo.Draw.LineThickness = 2;

			var worldPoints = new List<Vector3>( points.Count );
			var planeDistance = 100f;

			foreach ( var screenPoint in points )
			{
				var viewportX = screenPoint.x / Gizmo.Camera.Rect.Width;
				var viewportY = screenPoint.y / Gizmo.Camera.Rect.Height;

				var ray = Gizmo.Camera.GetRay( new Vector2( viewportX, viewportY ) );
				var worldPos = ray.Project( planeDistance );
				worldPoints.Add( worldPos );
			}

			//Maybe this should be it's own gizmo draw function?
			for ( int i = 0; i < worldPoints.Count - 1; i++ )
			{
				Gizmo.Draw.Line( worldPoints[i], worldPoints[i + 1] );
			}

			if ( isClosed && worldPoints.Count > 2 )
			{
				Gizmo.Draw.Line( worldPoints[^1], worldPoints[0] );
			}
		}
	}

	/// <summary>
	/// Called when the lasso selection changed or completed
	/// </summary>
	/// <param name="lassoPoints">Screen space points forming the lasso polygon</param>
	/// <param name="isFinal">True when the user releases the mouse</param>
	protected virtual void OnLassoSelect( List<Vector2> lassoPoints, bool isFinal )
	{

	}

	/// <summary>
	/// Called when the box selection changed
	/// </summary>
	protected virtual void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{

	}

	/// <summary>Object selection is committed once on release, like Hammer.</summary>
	protected void SelectObjectsInBox( Frustum frustum, bool isFinal )
	{
		if ( !isFinal ) return;
		var inside = BoxSelection.Find( Scene, frustum, EditorPreferences.BoxSelectIntersecting, Gizmo.Settings.GizmosEnabled );
		using ( Scene.Editor?.UndoScope( "Box Selection" ).Push() )
			BoxSelection.Apply( Selection, inside, Gizmo.IsShiftPressed, Gizmo.IsCtrlPressed );
	}

	/// <summary>
	/// Apply the result of a drag selection. Ctrl deselects whatever the drag covered, shift keeps
	/// what was already selected, otherwise the drag replaces the selection.
	/// </summary>
	/// <param name="inside">Everything the drag covered</param>
	/// <param name="outside">Everything it didn't</param>
	protected void ApplyDragSelection<T>( IEnumerable<T> inside, IEnumerable<T> outside )
	{
		if ( Gizmo.IsCtrlPressed )
		{
			foreach ( var element in inside )
			{
				if ( Selection.Contains( element ) )
					Selection.Remove( element );
			}

			return;
		}

		foreach ( var element in inside )
		{
			if ( !Selection.Contains( element ) )
				Selection.Add( element );
		}

		if ( Gizmo.IsShiftPressed ) return;

		foreach ( var element in outside )
		{
			if ( Selection.Contains( element ) )
				Selection.Remove( element );
		}
	}

	/// <summary>
	/// Helper method to check if a screen point is inside the lasso polygon
	/// Uses the ray casting algorithm
	/// </summary>
	protected static bool IsPointInLasso( Vector2 point, List<Vector2> lassoPoints )
	{
		if ( lassoPoints.Count < 3 )
			return false;

		int intersections = 0;
		for ( int i = 0; i < lassoPoints.Count; i++ )
		{
			var p1 = lassoPoints[i];
			var p2 = lassoPoints[(i + 1) % lassoPoints.Count];

			if ( (p1.y > point.y) != (p2.y > point.y) )
			{
				float intersectX = (p2.x - p1.x) * (point.y - p1.y) / (p2.y - p1.y) + p1.x;
				if ( point.x < intersectX )
					intersections++;
			}
		}

		return (intersections % 2) == 1;
	}
}

