public sealed partial class SnapGrid
{
	/// <summary>
	/// Draw the grid on an explicit plane with exact <paramref name="cellSize"/> spacing, for tools that do
	/// their own snapping (the Builder). Unlike <see cref="Update"/> it doesn't fit cells to the hovered face.
	/// </summary>
	public void UpdatePlane( SceneWorld world, Vector3 origin, Vector3 right, Vector3 up, Vector3 normal, float cellSize, Vector3 aim, Vector3 snapPos )
	{
		if ( !_sceneObj.IsValid() )
		{
			_material ??= Material.FromShader( "shaders/snap_grid.shader" );
			_sceneObj = new SnapGridSceneObject( world ) { Material = _material };
		}

		if ( Vector3.Dot( Vector3.Cross( right, up ), normal ) > 0 )
			(right, up) = (up, right);

		var visibleRadius = MathF.Max( 256f, cellSize * 8f );
		var maskRadius = visibleRadius / 0.3f;
		var halfSize = MathF.Ceiling( (visibleRadius + cellSize) / cellSize ) * cellSize;
		var halfExtents = new Vector2( halfSize, halfSize );

		var offset = snapPos - origin;
		var cornerX = MathF.Round( Vector3.Dot( offset, right ) / cellSize );
		var cornerY = MathF.Round( Vector3.Dot( offset, up ) / cellSize );
		var patchCenter = origin + right * (cornerX * cellSize) + up * (cornerY * cellSize);

		HasValidSnap = true;
		LastSnapWorldPos = snapPos;
		LastSnapWorldNormal = normal;

		_sceneObj.RenderingEnabled = true;
		_sceneObj.Write( patchCenter, right, up, halfExtents );
		_sceneObj.Attributes.Set( "MeshSurface", 0f );
		_sceneObj.Attributes.Set( "PlanarSurface", 0f );
		_sceneObj.Attributes.Set( "RoundSurface", 0f );
		_sceneObj.Attributes.Set( "SnapPoint", snapPos );
		_sceneObj.Attributes.Set( "SnapNormal", normal );
		_sceneObj.Attributes.Set( "GridOrigin", origin );
		_sceneObj.Attributes.Set( "BoundsOrigin", patchCenter );
		_sceneObj.Attributes.Set( "GridRight", right );
		_sceneObj.Attributes.Set( "GridUp", up );
		_sceneObj.Attributes.Set( "AimPoint", aim );
		_sceneObj.Attributes.Set( "MaskRadius", NoFade ? float.MaxValue : maskRadius );
		_sceneObj.Attributes.Set( "HalfExtents", halfExtents );
		_sceneObj.Attributes.Set( "CellSize", cellSize );
		_sceneObj.Attributes.Set( "CellSpacing", new Vector2( cellSize, cellSize ) );
		_sceneObj.Attributes.Set( "GridColor", new Vector4( 1, 1, 1, GridOpacity( cellSize ) ) );
		_sceneObj.Attributes.Set( "SnapCornerX", Vector3.Dot( snapPos - origin, right ) / cellSize );
		_sceneObj.Attributes.Set( "SnapCornerY", Vector3.Dot( snapPos - origin, up ) / cellSize );
	}

	/// <summary>
	/// Fine grids get fainter, so dense lines don't bury the surface: 20% at 4 units, rising to 50% at 64 and above.
	/// </summary>
	static float GridOpacity( float cellSize ) => MathX.Lerp( 0.2f, 0.5f, Math.Clamp( (cellSize - 4f) / 60f, 0f, 1f ) );
}
