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
	private Dictionary<Color, List<Line>> debugLinkLines;

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
		debugLinkLines ??= new();

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
					DebugCollectTileLinks( new Vector2Int( x, y ) );
				}
			}
		}

		foreach ( var entry in ladders.Values )
		{
			foreach ( var link in entry.Links )
			{
				if ( link.IsValid() && link.WorldPosition.WithZ( 0 ).DistanceSquared( cameraPosition.WithZ( 0 ) ) < debugTileDrawDistance * debugTileDrawDistance ) DebugCollectLink( link );
			}
		}
		DebugFlushGeoemtry();
	}

	private void DebugFlushGeoemtry()
	{
		using ( Gizmo.PushScope( "Navmesh" ) )
		{
			using ( Gizmo.PushScope( "Triangles" ) )
			{
				foreach ( var (color, triangles) in debugTriangles )
				{
					Gizmo.Draw.Color = color;
					Gizmo.Draw.SolidTriangles( triangles );
				}
			}

			using ( Gizmo.PushScope( "Lines" ) )
			{
				Gizmo.Draw.LineThickness = 1f;
				Gizmo.Draw.Color = debugInnerLineColor;
				Gizmo.Draw.Lines( debugInnerLines );


				Gizmo.Draw.LineThickness = 1.5f;
				Gizmo.Draw.Color = debugTileBorderColor;
				Gizmo.Draw.Lines( debugTileBorders );

				Gizmo.Draw.LineThickness = 2f;
				foreach ( var (color, lines) in debugLinkLines )
				{
					Gizmo.Draw.Color = color;
					Gizmo.Draw.Lines( lines );
				}
			}
		}

		foreach ( var (color, triangles) in debugTriangles )
		{
			triangles.Clear();
		}
		foreach ( var (color, lines) in debugLinkLines )
		{
			lines.Clear();
		}
		debugTileBorders.Clear();
		debugInnerLines.Clear();
	}

	// Generated links as arcs in the color of their type, one way ones with a tick at the end they lead to
	private void DebugCollectTileLinks( Vector2Int tilePosition )
	{
		if ( !generatedLinks.TryGetValue( (tilePosition.x, tilePosition.y), out var components ) ) return;
		foreach ( var component in components ) DebugCollectLink( component );
	}

	private void DebugCollectLink( NavMeshLink component )
	{
		var color = (component.Type?.Color ?? debugTileBorderColor).WithAlpha( 1 );
		if ( !debugLinkLines.TryGetValue( color, out var lines ) )
		{
			debugLinkLines[color] = lines = new();
		}

		Vector3 start = (Vector3)component.generated.Start + debugDrawGroundOffset, end = (Vector3)component.generated.End + debugDrawGroundOffset;
		if ( component.Type is not null && component.Type == Sandbox.Engine.Resources.NavMeshLinkType.Ladder )
		{
			var landing = end + Vector3.Up * component.ClimbExitLift;
			var top = start + component.ClimbDirection * ((landing.z - start.z) / component.ClimbDirection.z);
			lines.Add( new Line( start, top ) );
			lines.Add( new Line( top, landing ) );
			lines.Add( new Line( landing, end ) );
			return;
		}
		var previous = start;
		for ( int i = 1; i <= 4; i++ )
		{
			float t = i / 4f;
			var point = Vector3.Lerp( start, end, t ) + Vector3.Up * (MathF.Sin( t * MathF.PI ) * MathF.Abs( start.z - end.z ) * 0.5f);
			lines.Add( new Line( previous, point ) );
			previous = point;
		}
		if ( !component.generated.Bidirectional ) lines.Add( new Line( end, end + Vector3.Up * 8f ) );
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
