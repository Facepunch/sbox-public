using System;
using System.Numerics;
using DotRecast.Detour;
using NativeEngine;
using Sandbox.ModelEditor;

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
	private unsafe void DebugCollectTileNavmeshGeometry( Vector2Int tilePosition )
	{
		var cameraToTileDistanceSquared = TilePositionToWorldPosition( tilePosition ).WithZ( 0 ).DistanceSquared( Gizmo.Camera.Position.WithZ( 0 ) );

		if ( navmeshInternal == null )
		{
			return;
		}

		var tile = navmeshInternal.GetTileAt( tilePosition.x, tilePosition.y, 0 );
		if ( tile == null || tile.data.header == null )
		{
			return;
		}

		Span<Vector3> polyVerts = stackalloc Vector3[navmeshInternal.GetMaxVertsPerPoly()];

		for ( int iPoly = 0; iPoly < GetPolyCount( tilePosition ); ++iPoly )
		{
			if ( iPoly >= tile.data.header.polyCount )
				break;

			var poly = tile.data.polys[iPoly];

			if ( poly.type == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION )
				continue;

			int polyVertexCount = poly.vertCount;

			var polyAreaDefintion = AreaIdToDefinition( poly.area );
			var polyColor = polyAreaDefintion != null ? polyAreaDefintion.Color.WithAlpha( 0.6f ) : debugTriangleColor;

			// Simple fan triangulation - create triangles from vertex 0 to all other vertices
			if ( polyVertexCount >= 3 )
			{
				for ( int i = 0; i < polyVertexCount; i++ )
				{
					polyVerts[i] = FromNav( tile.data.verts[poly.verts[i]] ) + debugDrawGroundOffset;
				}

				// Create triangles using fan triangulation
				for ( int j = 2; j < polyVertexCount; j++ )
				{
					var triangle = new Triangle
					{
						A = polyVerts[0],
						B = polyVerts[j - 1],
						C = polyVerts[j]
					};

					if ( !debugTriangles.ContainsKey( polyColor ) )
					{
						debugTriangles[polyColor] = new();
					}
					debugTriangles[polyColor].Add( triangle );
				}
			}

			for ( int vertexIndex = 0; vertexIndex < polyVertexCount; ++vertexIndex )
			{
				bool bIsInterEdge = ((poly.neis[vertexIndex] & DtDetour.DT_EXT_LINK) != 0) && (poly.neis[vertexIndex] != 0);

				// Use the poly vertices directly because we don't use the detail mesh
				// If we ever change that, we need more complex logic to align the poly boundaries with the detail mesh
				int vertIndexV0 = poly.verts[vertexIndex];
				int vertIndexV1 = poly.verts[(vertexIndex + 1) % polyVertexCount];

				Vector3 v0 = FromNav( tile.data.verts[vertIndexV0] ) + debugDrawGroundOffset;
				Vector3 v1 = FromNav( tile.data.verts[vertIndexV1] ) + debugDrawGroundOffset;

				if ( bIsInterEdge )
				{
					debugTileBorders.Add( new Line( v0, v1 ) );
				}
				else
				{
					debugInnerLines.Add( new Line( v0, v1 ) );
				}
			}
		}
	}
}
