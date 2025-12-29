
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
	public Material ActiveMaterial { get; set; } = Material.Load( "materials/dev/reflectivity_30.vmat" );

	public MoveMode MoveMode { get; set; }

	public void SetMoveMode<T>() where T : MoveMode
	{
		if ( MoveMode?.GetType() == typeof( T ) ) return;
		MoveMode = EditorTypeLibrary.Create<MoveMode>( typeof( T ) );
	}

	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new PrimitiveTool( this );
		yield return new MeshSelection( this );
		yield return new VertexTool( this );
		yield return new EdgeTool( this );
		yield return new FaceTool( this );
		yield return new TextureTool( this );
	}

	public override void OnEnabled()
	{
		base.OnEnabled();

		AllowGameObjectSelection = false;
		AllowContextMenu = false;

		Selection.Clear();

		SetMoveMode<PositionMode>();
	}

	public override void OnSelectionChanged()
	{
		CurrentTool?.OnSelectionChanged();
	}

	[Shortcut( "tools.mesh-tool", "m", typeof( SceneDock ) )]
	public static void ActivateTool()
	{
		EditorToolManager.SetTool( nameof( MeshTool ) );
	}
}
