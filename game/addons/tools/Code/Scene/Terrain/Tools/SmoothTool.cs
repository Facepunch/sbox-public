namespace Editor.TerrainEditor;

/// <summary>
/// Smooth an area of terrain.
/// </summary>
[Title( "Smooth" )]
[Icon( "rounded_corner" )]
[Alias( "tools.terrain.smooth" )]
[Group( "1" )]
[Order( 0 )]
public class SmoothTool : BaseBrushTool
{
	public SmoothTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		Mode = SculptMode.Smooth;
	}

	public override bool GetHitPosition( Terrain terrain, out Vector3 position )
	{
		if ( _dragging )
		{
			var tx = terrain.WorldTransform;
			var hit = StrokePlane.Trace( Gizmo.CurrentRay, true );
			position = tx.PointToLocal( hit.Value );
			return hit.HasValue;
		}

		return base.GetHitPosition( terrain, out position );
	}
}
