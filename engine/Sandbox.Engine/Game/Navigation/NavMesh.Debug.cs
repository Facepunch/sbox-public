namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	internal static Color debugTriangleColor = new Color( 0, 0.15f, 0.32f ).WithAlpha( 0.6f );

	internal static Color debugInnerLineColor = new Color( 0.85f, 0.85f, 0.25f );

	internal static Color debugTileBorderColor = Color.Blue.Desaturate( 0.4f );

	internal static Vector3 debugDrawGroundOffset = new( 0, 0, 4f );

	[ConVar( "nav_debug_draw_distance", ConVarFlags.Protected | ConVarFlags.Cheat, Min = 0, Max = 40000f, Help = "Draw Distance of the nav mesh." )]
	private static float debugTileDrawDistance { get; set; } = 15000f;

	private List<Line> debugTileBorders;
	private List<Line> debugInnerLines;

	private Dictionary<Color, List<Triangle>> debugTriangles;

	internal void DebugDraw()
	{
		if ( !IsEnabled )
		{
			return;
		}

		if ( !DrawMesh )
		{
			return;
		}

		debugTriangles ??= new( 4096 );
		debugTileBorders ??= new( 4096 );
		debugInnerLines ??= new( 4096 * 2 );

		var cameraRotation = Gizmo.Camera.Angles.ToRotation();
		var cameraForward = cameraRotation.Forward;
		var cameraRight = cameraRotation.Right;
		var cameraUp = cameraRotation.Up;
		var cameraPosition = Gizmo.Camera.Position;

		var boxWithPotentiallyVisibleTiles = BBox.FromPoints(
			new Vector3[] {
				cameraUp * debugTileDrawDistance,
				-cameraUp * debugTileDrawDistance,
				cameraForward * debugTileDrawDistance,
				cameraRight * debugTileDrawDistance,
				-cameraRight * debugTileDrawDistance
			} ).Translate( cameraPosition );

		var minMaxCoordsVisibleTiles = CalculateMinMaxTileCoords( boxWithPotentiallyVisibleTiles );

		// Draw Tiles
		for ( int x = minMaxCoordsVisibleTiles.Left; x <= minMaxCoordsVisibleTiles.Right; x++ )
		{
			for ( int y = minMaxCoordsVisibleTiles.Top; y <= minMaxCoordsVisibleTiles.Bottom; y++ )
			{
				var tileWorldPosition = TilePositionToWorldPosition( new Vector2Int( x, y ) );
				if ( tileWorldPosition.WithZ( 0 ).DistanceSquared( cameraPosition.WithZ( 0 ) ) < debugTileDrawDistance * debugTileDrawDistance )
				{
					DebugCollectTileNavmeshGeometry( new Vector2Int( x, y ) );
				}
			}
		}

		DebugFlushGeoemtry();
	}

	private void DebugFlushGeoemtry()
	{
		using ( Gizmo.Scope( "Navmesh" ) )
		{
			using ( Gizmo.Scope( "Triangles" ) )
			{
				foreach ( var (color, triangles) in debugTriangles )
				{
					Gizmo.Draw.Color = color;
					Gizmo.Draw.SolidTriangles( triangles );
				}
			}

			using ( Gizmo.Scope( "Lines" ) )
			{
				Gizmo.Draw.LineThickness = 1f;
				Gizmo.Draw.Color = debugInnerLineColor;
				Gizmo.Draw.Lines( debugInnerLines );


				Gizmo.Draw.LineThickness = 1.5f;
				Gizmo.Draw.Color = debugTileBorderColor;
				Gizmo.Draw.Lines( debugTileBorders );
			}
		}

		foreach ( var (color, triangles) in debugTriangles )
		{
			triangles.Clear();
		}
		debugTileBorders.Clear();
		debugInnerLines.Clear();
	}

	private void DebugCollectTileNavmeshGeometry( Vector2Int tilePosition )
	{
		ReadPolygons( tilePosition );

		var tileMins = new Vector2( tilePosition.x, tilePosition.y ) * TileSizeXYWorldSpace;
		var tileMaxs = tileMins + TileSizeXYWorldSpace;

		int first = 0;
		for ( int iPoly = 0; iPoly < polyVertexCounts.Count; first += polyVertexCounts[iPoly], ++iPoly )
		{
			int polyVertexCount = polyVertexCounts[iPoly];

			var polyAreaDefintion = AreaIdToDefinition( polyAreas[iPoly] );
			var polyColor = polyAreaDefintion != null ? polyAreaDefintion.Color.WithAlpha( 0.6f ) : debugTriangleColor;

			if ( !debugTriangles.TryGetValue( polyColor, out var triangles ) )
			{
				debugTriangles[polyColor] = triangles = new();
			}

			// Simple fan triangulation - create triangles from vertex 0 to all other vertices
			var v0 = (Vector3)polyVertices[first] + debugDrawGroundOffset;
			for ( int j = 2; j < polyVertexCount; j++ )
			{
				triangles.Add( new Triangle
				{
					A = v0,
					B = (Vector3)polyVertices[first + j - 1] + debugDrawGroundOffset,
					C = (Vector3)polyVertices[first + j] + debugDrawGroundOffset
				} );
			}

			for ( int vertexIndex = 0; vertexIndex < polyVertexCount; ++vertexIndex )
			{
				var a = (Vector3)polyVertices[first + vertexIndex] + debugDrawGroundOffset;
				var b = (Vector3)polyVertices[first + (vertexIndex + 1) % polyVertexCount] + debugDrawGroundOffset;

				// An edge along the side of the tile is where the next tile joins
				bool isTileBorder = OnSameLine( a.x, b.x, tileMins.x ) || OnSameLine( a.x, b.x, tileMaxs.x )
					|| OnSameLine( a.y, b.y, tileMins.y ) || OnSameLine( a.y, b.y, tileMaxs.y );

				if ( isTileBorder )
				{
					debugTileBorders.Add( new Line( a, b ) );
				}
				else
				{
					debugInnerLines.Add( new Line( a, b ) );
				}
			}
		}

		static bool OnSameLine( float a, float b, float line ) => MathF.Abs( a - line ) < 0.01f && MathF.Abs( b - line ) < 0.01f;
	}
}
