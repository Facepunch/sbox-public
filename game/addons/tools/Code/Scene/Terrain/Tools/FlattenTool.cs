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
	/// <summary>
	/// Align the flatten plane to the terrain surface normal where the stroke starts,
	/// instead of the world up axis - lets you flatten along slopes.
	/// </summary>
	[Title( "Align to Surface" )]
	public bool AlignToSurface
	{
		get => _alignToSurface;
		set
		{
			_alignToSurface = value;
			EditorCookie.Set( "TerrainTool.FlattenAlignToSurface", value );
		}
	}
	bool _alignToSurface = EditorCookie.Get( "TerrainTool.FlattenAlignToSurface", false );

	Vector3 _surfaceNormal = Vector3.Up;

	public FlattenTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
		Mode = SculptMode.Flatten;
	}

	protected override Plane CreateStrokePlane( Terrain terrain, Vector3 hitWorldPos )
	{
		if ( AlignToSurface )
			return new Plane( hitWorldPos, _surfaceNormal );

		return base.CreateStrokePlane( terrain, hitWorldPos );
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

		if ( AlignToSurface && terrain.RayIntersects( Gizmo.CurrentRay, Gizmo.RayDepth, out position, out var localNormal ) )
		{
			_surfaceNormal = terrain.WorldTransform.NormalToWorld( localNormal );
			return true;
		}

		return base.GetHitPosition( terrain, out position );
	}

	protected override void DrawToolPreview( Terrain terrain, Vector3 worldHitPos )
	{
		var normal = _dragging ? StrokePlane.Normal
			: AlignToSurface ? _surfaceNormal
			: terrain.WorldTransform.Rotation.Up;
		var color = Color.FromBytes( 150, 150, 250 ).WithAlpha( _dragging ? 0.6f : 0.35f );
		var size = _parent.BrushSettings.Size;

		using ( Gizmo.Scope( "FlattenPlanePreview", worldHitPos, Rotation.LookAt( normal ) ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 2;

			// Rings extending past the brush so the flatten plane reads against the terrain
			for ( int i = 1; i <= 3; i++ )
			{
				Gizmo.Draw.Color = color.WithAlphaMultiplied( 1.0f / i );
				Gizmo.Draw.LineCircle( Vector3.Zero, size * (1.0f + i * 0.25f), sections: 48 );
			}

			var extent = size * 1.75f;
			Gizmo.Draw.Color = color;
			Gizmo.Draw.Line( Vector3.Left * extent, Vector3.Right * extent );
			Gizmo.Draw.Line( Vector3.Up * extent, Vector3.Down * extent );

			// Plane normal
			Gizmo.Draw.Color = color.WithAlpha( 1.0f );
			Gizmo.Draw.Arrow( Vector3.Zero, Vector3.Forward * size * 0.5f );
		}
	}
}
