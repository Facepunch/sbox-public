namespace Editor.TerrainEditor;

/// <summary>
/// Adds a random noise to a terrain.
/// </summary>
/// 
[Title( "Noise" )]
[Icon( "shuffle" )]
[Alias( "tools.terrain.noise" )]
[Group( "1" )]
[Order( 0 )]
public class NoiseTool : BaseBrushTool
{
	public NoiseTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		Mode = SculptMode.Noise;
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
