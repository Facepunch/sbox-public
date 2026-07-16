using HalfEdgeMesh;

namespace Editor.MeshEditor;

/// <summary>
/// Find all faces using a material and replace it with another,
/// across the current selection or the whole scene.
/// </summary>
[Alias( "tools.find-replace-material-tool" )]
public partial class FindReplaceMaterialTool : SelectionTool<MeshFace>
{
	readonly string _returnTool;

	public FindReplaceMaterialTool( MeshTool meshTool, string returnTool ) : base( meshTool )
	{
		_returnTool = returnTool;
	}

	public enum ReplaceScope
	{
		Selection,
		Scene
	}

	private Material _findMaterial;
	private Material _replaceMaterial;
	private ReplaceScope _scope = ReplaceScope.Scene;

	[WideMode]
	public Material FindMaterial
	{
		get => _findMaterial;
		set { if ( _findMaterial == value ) return; _findMaterial = value; RefreshMatches(); }
	}

	[WideMode]
	public Material ReplaceMaterial
	{
		get => _replaceMaterial;
		set => _replaceMaterial = value;
	}

	[WideMode]
	public ReplaceScope Scope
	{
		get => _scope;
		set { if ( _scope == value ) return; _scope = value; RefreshMatches(); }
	}

	public int MatchCount { get; private set; }
	public int MatchComponentCount { get; private set; }

	readonly Dictionary<MeshComponent, List<FaceHandle>> _matches = [];

	SceneDynamicObject _faceObject;

	public override void OnEnabled()
	{
		base.OnEnabled();

		CreateFaceObject();

		// Default the search material to the mesh tool's active material
		_findMaterial = Tool?.ActiveMaterial;

		RefreshMatches();
	}

	void CreateFaceObject()
	{
		_faceObject = new SceneDynamicObject( Scene.SceneWorld );
		_faceObject.Material = Material.Load( "materials/tools/vertex_color_translucent.vmat" );
		_faceObject.Attributes.SetCombo( "D_DEPTH_BIAS", 1 );
		_faceObject.Attributes.SetCombo( "D_NO_CULLING", 1 );
		_faceObject.Flags.CastShadows = false;
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		_faceObject?.Delete();
		_faceObject = null;

		_matches.Clear();
	}

	public override void OnSelectionChanged()
	{
		base.OnSelectionChanged();

		if ( Scope == ReplaceScope.Selection )
			RefreshMatches();
	}

	IEnumerable<MeshComponent> GetTargetComponents()
	{
		if ( Scope == ReplaceScope.Scene )
			return Scene.GetAllComponents<MeshComponent>();

		return Selection.OfType<GameObject>()
			.SelectMany( go => go.GetComponentsInChildren<MeshComponent>() )
			.Concat( Selection.OfType<MeshFace>().Select( f => f.Component ) )
			.Distinct()
			.Where( c => c.IsValid() );
	}

	static bool MaterialMatches( Material a, Material b )
	{
		if ( a is null && b is null ) return true;
		if ( a is null || b is null ) return false;
		return a.ResourcePath == b.ResourcePath;
	}

	void RefreshMatches()
	{
		_matches.Clear();

		foreach ( var component in GetTargetComponents() )
		{
			var mesh = component.Mesh;
			if ( mesh is null ) continue;

			List<FaceHandle> faces = null;

			foreach ( var hFace in mesh.FaceHandles )
			{
				if ( !MaterialMatches( mesh.GetFaceMaterial( hFace ), FindMaterial ) )
					continue;

				faces ??= [];
				faces.Add( hFace );
			}

			if ( faces is not null )
				_matches[component] = faces;
		}

		MatchCount = _matches.Sum( kv => kv.Value.Count );
		MatchComponentCount = _matches.Count;
	}

	public bool CanApply => ReplaceMaterial.IsValid() && MatchCount > 0 && !MaterialMatches( FindMaterial, ReplaceMaterial );

	public void Apply()
	{
		if ( !CanApply ) return;

		using var scope = SceneEditorSession.Scope();

		var components = _matches.Keys.Where( x => x.IsValid() ).ToArray();

		using ( SceneEditorSession.Active.UndoScope( "Replace Material" )
			.WithComponentChanges( components )
			.Push() )
		{
			foreach ( var (component, faces) in _matches )
			{
				if ( !component.IsValid() ) continue;

				var mesh = component.Mesh;

				foreach ( var hFace in faces )
					mesh.SetFaceMaterial( hFace, ReplaceMaterial );
			}
		}

		RefreshMatches();
	}

	public bool CanSelect => MatchCount > 0;

	/// <summary>
	/// Replace the current selection with all faces matching the find material,
	/// within the current scope. Faces not using the material are deselected.
	/// </summary>
	public void SelectMatches()
	{
		if ( !CanSelect ) return;

		using var scope = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Select Faces By Material" ).Push() )
		{
			Selection.Clear();

			foreach ( var (component, faces) in _matches )
			{
				if ( !component.IsValid() ) continue;

				foreach ( var hFace in faces )
					Selection.Add( new MeshFace( component, hFace ) );
			}
		}
	}

	bool _escape;

	public override void OnUpdate()
	{
		base.OnUpdate();

		var escape = Application.IsKeyDown( KeyCode.Escape );
		if ( escape && !_escape ) Close();
		_escape = escape;

		using var scope = Gizmo.Scope( "FindReplaceMaterialTool" );
		UpdateFaceSelectionAndOverlay();
	}

	protected void UpdateFaceSelectionAndOverlay()
	{
		if ( _faceObject is null || (_faceObject.IsValid() && _faceObject.World != Scene.SceneWorld) )
		{
			_faceObject?.Delete();
			CreateFaceObject();
		}

		var hoverFace = MeshTrace.TraceFace( out var hitPosition );
		if ( hoverFace.IsValid() )
			Gizmo.Hitbox.TrySetHovered( hitPosition );

		if ( Gizmo.IsHovered && Tool.MoveMode.AllowSceneSelection && !IsLassoSelecting )
			UpdateSelection( hoverFace );

		_faceObject.Init( Graphics.PrimitiveType.Triangles );

		if ( hoverFace.IsValid() )
			AddFaceOverlay( hoverFace, Color.Green.WithAlpha( 0.1f ) );

		var selectionColor = Tool.OverlaySelection ? Color.Yellow.WithAlpha( 0.1f ) : Color.Transparent;

		foreach ( var face in Selection.OfType<MeshFace>() )
			AddFaceOverlay( face, selectionColor );
	}

	void AddFaceOverlay( MeshFace face, Color color )
	{
		if ( !face.IsValid() ) return;

		var mesh = face.Component.Mesh;
		var vertices = mesh.CreateFace( face.Handle, face.Transform, color );
		if ( vertices is not null )
			_faceObject.AddVertex( vertices.AsSpan() );
	}

	public void Close()
	{
		EditorToolManager.SetSubTool( _returnTool );
	}
}
