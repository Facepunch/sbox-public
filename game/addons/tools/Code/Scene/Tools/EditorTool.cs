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
	public SceneTrace MeshTrace => Trace.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
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
			if ( HasBoxSelectionMode() )
			{
				UpdateBoxSelection();
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

	Ray _ray1;
	Ray _ray2;

	protected bool IsBoxSelecting { get; private set; }

	private void UpdateBoxSelection()
	{
		var ray = Gizmo.CurrentRay;

		if ( Gizmo.WasLeftMousePressed )
		{
			_ray1 = ray;
		}

		if ( Gizmo.IsLeftMouseDown && _ray1 != default )
		{
			_ray2 = ray;
		}

		// project to screen position
		var c1 = Gizmo.Camera.ToScreen( _ray1.Project( 100 ) );
		var c2 = Gizmo.Camera.ToScreen( _ray2.Project( 100 ) );

		if ( Vector2.Distance( c1, c2 ) < 5 || Gizmo.Pressed.Any )
		{
			IsBoxSelecting = false;

			if ( !Gizmo.IsLeftMouseDown )
			{
				_ray1 = _ray2 = default;
			}

			return;
		}

		IsBoxSelecting = true;

		var frustum = Gizmo.Camera.GetFrustum( Rect.FromPoints( c1, c2 ) );
		Rect rect = new Rect( MathF.Min( c1.x, c2.x ), MathF.Min( c1.y, c2.y ), MathF.Abs( c1.x - c2.x ), MathF.Abs( c1.y - c2.y ) );

		OnBoxSelect( frustum, rect, Gizmo.WasLeftMouseReleased );

		// Crystalize the box select
		if ( Gizmo.WasLeftMouseReleased )
		{
			_ray1 = _ray2 = default;
			return;
		}

		// Paint the selection rectangle
		{
			Gizmo.Draw.ScreenRect( rect, Theme.Blue.WithAlpha( 0.1f ), new Vector4( 1.0f ), Theme.Blue, new Vector4( 1.0f ) );
		}
	}

	/// <summary>
	/// Called when the box selection changed
	/// </summary>
	protected virtual void OnBoxSelect( Frustum frustum, Rect screenRect, bool isFinal )
	{

	}
}

