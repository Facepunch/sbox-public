namespace Editor.TerrainEditor;

/// <summary>
/// Puts a hole in the terrain.<br/> <br/>
/// <b>Ctrl</b> - fill hole
/// </summary>
/// 
[Title( "Hole" )]
[Icon( "trip_origin" )]
[Alias( "tools.terrain.hole" )]
[Group( "1" )]
[Order( 0 )]
public class HoleTool : BaseBrushTool
{
	public HoleTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		Mode = SculptMode.Hole;
		AllowBrushInvert = true;
	}
}
