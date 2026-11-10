
namespace Editor.TerrainEditor;

/// <summary>
/// Click and drag to raise terrain.<br/> <br/>
/// <b>Ctrl</b> - lower terrain
/// </summary>
/// 
[Title( "Raise / Lower" )]
[Icon( "height" )]
[Alias( "tools.terrain.raise-lower" )]
[Group( "1" )]
[Order( 0 )]
public class RaiseLowerTool : BaseBrushTool
{
	public RaiseLowerTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		Mode = SculptMode.RaiseLower;
		AllowBrushInvert = true;
	}
}
