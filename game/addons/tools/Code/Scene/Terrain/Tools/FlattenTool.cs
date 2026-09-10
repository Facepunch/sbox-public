namespace Editor.TerrainEditor;

/// <summary>
/// Flatten an area of terrain.
/// </summary>
[Title( "Flatten" )]
[Icon( "trending_flat" )]
[Alias( "tools.terrain.flatten" )]
[Group( "1" )]
[Order( 0 )]
public class FlattenTool : BaseBrushTool
{
	public FlattenTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		Mode = SculptMode.Flatten;
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
