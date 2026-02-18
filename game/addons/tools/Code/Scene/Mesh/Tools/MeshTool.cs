
namespace Editor.MeshEditor;

/// <summary>
/// Mesh tools mode for creating and editing meshes.
/// </summary>
[EditorTool( "tools.mesh-tool" )]
[Title( "Mapping" )]
[Icon( "hardware" )]
[Alias( "mesh" )]
public partial class MeshTool : EditorTool
{
	private Material _activeMaterial;

	public Material ActiveMaterial
	{
		get => _activeMaterial;
		set
		{
			if ( _activeMaterial != value )
			{
				_activeMaterial = value;
				SaveActiveMaterial();
			}
		}
	}

	public MoveMode MoveMode { get; set; }

	public void SetMoveMode<T>() where T : MoveMode
	{
		if ( MoveMode?.GetType() == typeof( T ) ) return;
		MoveMode = EditorTypeLibrary.Create<MoveMode>( typeof( T ) );
	}

	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new PrimitiveTool( this );
		yield return new ObjectSelection( this );
		yield return new VertexTool( this );
		yield return new EdgeTool( this );
		yield return new FaceTool( this );
		yield return new TextureTool( this );
		yield return new VertexPaintTool( this );
	}

	public override void OnEnabled()
	{
		base.OnEnabled();

		AllowGameObjectSelection = false;
		AllowContextMenu = false;

		Selection.Clear();

		SetMoveMode<PositionMode>();

		LoadActiveMaterial();
		LoadToolbarCookies();
	}

	public override void OnUpdate()
	{
		AllowGameObjectSelection = CurrentTool?.GetType() == typeof( ObjectSelection );
	}

	public override void OnSelectionChanged()
	{
		CurrentTool?.OnSelectionChanged();
	}

	[Shortcut( "tools.mesh-tool", "m", typeof( SceneViewWidget ) )]
	public static void ActivateTool()
	{
		EditorToolManager.SetTool( nameof( MeshTool ) );
	}

	private void SaveActiveMaterial()
	{
		if ( _activeMaterial != null && _activeMaterial.IsValid() )
		{
			ProjectCookie.Set( "MeshTool.ActiveMaterial", _activeMaterial.ResourcePath );
		}
	}

	private void LoadActiveMaterial()
	{
		var savedPath = ProjectCookie.Get( "MeshTool.ActiveMaterial", string.Empty );

		if ( !string.IsNullOrEmpty( savedPath ) )
		{
			var material = Material.Load( savedPath );
			if ( material != null && material.IsValid() )
			{
				_activeMaterial = material;
				return;
			}
		}

		_activeMaterial = Material.Load( "materials/dev/reflectivity_30.vmat" );
	}
}
