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
			OnUpdate();
			CurrentTool?.OnUpdate();
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
	/// Create a widget for this tool to be added next to the left toolbar.
	/// NOTE: This is only called for main tools, not subtools.
	/// </summary>
	public virtual Widget CreateToolWidget()
	{
		return null;
	}
}

