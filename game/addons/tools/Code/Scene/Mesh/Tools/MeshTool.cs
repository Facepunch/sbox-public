
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

	public MoveMode CurrentMoveMode { get; set; }

	public override IEnumerable<EditorTool> GetSubtools()
	{
		yield return new BlockTool( this );
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

		CurrentMoveMode = EditorTypeLibrary.Create<MoveMode>( "PositionMode" );
	}

	[Shortcut( "tools.mesh-tool", "m", typeof( SceneDock ) )]
	public static void ActivateTool()
	{
		EditorToolManager.SetTool( nameof( MeshTool ) );
	}
}
