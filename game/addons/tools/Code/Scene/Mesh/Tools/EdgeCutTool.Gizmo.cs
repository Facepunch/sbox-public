using Microsoft.CodeAnalysis.Text;
using static Sandbox.Gizmo;

namespace Editor.MeshEditor;

partial class EdgeCutTool
{
	void DrawCutPoints()
	{
		using ( Gizmo.Scope( "Points" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			if ( _cutPoints.Count > 0 )
			{
				Gizmo.Draw.LineThickness = 2;
				Gizmo.Draw.Color = new Color( 0.3137f, 0.7843f, 1.0f, 1f );
				for ( int i = 1; i < _cutPoints.Count; i++ )
				{
					Gizmo.Draw.Line( _cutPoints[i - 1].WorldPosition, _cutPoints[i].WorldPosition );
				}
			}

			Gizmo.Draw.Color = Color.White;

			foreach ( var cutPoint in _cutPoints )
			{
				Gizmo.Draw.Sprite( cutPoint.WorldPosition, 10, null, false );
			}
		}
	}

	void DrawPreview()
	{
		if ( _previewCutPoint.IsValid() == false ) return;

		var mesh = _previewCutPoint.Face.Component;
		if ( _hoveredMesh != mesh ) _hoveredMesh = mesh;

		var edge = _previewCutPoint.Edge;
		if ( edge.IsValid() )
		{
			using ( Gizmo.Scope( "Edge Hover", _previewCutPoint.Edge.Transform ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = Color.Green;
				Gizmo.Draw.LineThickness = 4;
				Gizmo.Draw.Line( edge.Line );
			}

			DrawEdgeCutLengthText();
			DrawPerpendicularCutLine( _previewCutPoint );
		}

		using ( Gizmo.Scope( "Point" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			if ( _cutPoints.Count > 0 )
			{
				var lastCutPoint = _cutPoints.Last();
				Gizmo.Draw.LineThickness = 4;
				Gizmo.Draw.Color = Color.White;
				Gizmo.Draw.Line( _previewCutPoint.WorldPosition, lastCutPoint.WorldPosition );

				if ( !_previewCutPoint.Edge.IsValid() && !_previewCutPoint.Vertex.IsValid() )
				{
					DrawFaceCutLengthText();
				}
			}

			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.Sprite( _previewCutPoint.WorldPosition, 10, null, false );
		}
	}

	void DrawPerpendicularCutLine( MeshCutPoint cutPoint )
	{
		if ( !cutPoint.Edge.IsValid() || !cutPoint.Face.IsValid() ) return;

		var mesh = cutPoint.Component.Mesh;
		mesh.GetVerticesConnectedToEdge( cutPoint.Edge.Handle, cutPoint.Face.Handle, out var vA, out var vB );
		mesh.GetVertexPosition( vA, Transform.Zero, out var posA );
		mesh.GetVertexPosition( vB, Transform.Zero, out var posB );
		var edgeDir = (posB - posA).Normal;

		mesh.ComputeFaceNormal( cutPoint.Face.Handle, out var faceNormal );
		var perpDir = faceNormal.Cross( edgeDir ).Normal;

		var faceVerts = mesh.GetFaceVertexPositions( cutPoint.Face.Handle, Transform.Zero ).ToList();
		var startPos = cutPoint.BasePosition;

		var maxDist = 0.0f;
		foreach ( var vert in faceVerts )
		{
			maxDist = MathF.Max( maxDist, perpDir.Dot( vert - startPos ) );
		}

		for ( int i = 0; i < faceVerts.Count; i++ )
		{
			var vEdgeA = faceVerts[i];
			var vEdgeB = faceVerts[(i + 1) % faceVerts.Count];

			var edgePlaneNormal = faceNormal.Cross( vEdgeB - vEdgeA ).Normal;

			if ( edgePlaneNormal.Dot( perpDir ) >= 0.0f ) continue;

			var plane = new Plane( vEdgeA, edgePlaneNormal );
			var intersection = plane.IntersectLine( startPos, startPos + perpDir * maxDist );

			if ( intersection.HasValue )
			{
				var distToIntersection = perpDir.Dot( intersection.Value - startPos );
				if ( distToIntersection > 0.0f && distToIntersection < maxDist )
				{
					maxDist = distToIntersection;
				}
			}
		}

		using ( Gizmo.Scope( "PerpendicularLine", cutPoint.Component.WorldTransform ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.Orange.WithAlpha( 0.5f );
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Line( startPos, startPos + perpDir * maxDist );
		}
	}

	void DrawEdgeCutLengthText()
	{
		if ( !_previewCutPoint.Edge.IsValid() ) return;

		var edge = _previewCutPoint.Edge;
		var mesh = edge.Component.Mesh;

		mesh.GetVerticesConnectedToEdge( edge.Handle, out var hVertexA, out var hVertexB );
		mesh.GetVertexPosition( hVertexA, Transform.Zero, out var vPositionA );
		mesh.GetVertexPosition( hVertexB, Transform.Zero, out var vPositionB );

		var edgeLength = vPositionA.Distance( vPositionB );
		mesh.ComputeClosestPointOnEdge( hVertexA, hVertexB, _previewCutPoint.LocalPosition, out var edgeParam );

		var distanceA = (1.0f - edgeParam) * edgeLength;
		var distanceB = edgeParam * edgeLength;

		var screenPos = Gizmo.Camera.ToScreen( _previewCutPoint.WorldPosition );
		if ( !screenPos.IsNearlyZero() )
		{
			var textPos = screenPos + new Vector2( 8, -24 );
			var text = $"({distanceA:F0} : {distanceB:F0})";
			var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

			using ( Gizmo.Scope( "LengthText" ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				DrawText( text, textPos, textSize );
			}
		}
	}

	void DrawFaceCutLengthText()
	{
		if ( _cutPoints.Count == 0 ) return;

		var lastCutPoint = _cutPoints.Last();
		var distance = lastCutPoint.WorldPosition.Distance( _previewCutPoint.WorldPosition );

		var screenPos = Gizmo.Camera.ToScreen( _previewCutPoint.WorldPosition );
		if ( !screenPos.IsNearlyZero() )
		{
			var textPos = screenPos + new Vector2( 8, -24 );
			var text = $"{distance:F0}";
			var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

			using ( Gizmo.Scope( "LengthText" ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				DrawText( text, textPos, textSize );
			}
		}
	}

	void DrawText( string text, Vector3 worldPosition, float textSize )
	{
		var textScope = new TextRendering.Scope
		{
			Text = text,
			TextColor = Color.White,
			FontSize = textSize,
			FontName = "Roboto Mono",
			FontWeight = 400,
			LineHeight = 1,
			Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
		};

		Gizmo.Draw.ScreenText( textScope, worldPosition );
	}

	static void DrawMesh( MeshComponent mesh )
	{
		if ( mesh.IsValid() == false ) return;

		using ( Gizmo.ObjectScope( mesh.GameObject, mesh.WorldTransform ) )
		{
			using ( Gizmo.Scope( "Edges" ) )
			{
				var edgeColor = new Color( 0.3137f, 0.7843f, 1.0f, 1f );

				Gizmo.Draw.LineThickness = 1;
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = edgeColor.Darken( 0.3f ).WithAlpha( 0.2f );

				foreach ( var v in mesh.Mesh.GetEdges() )
				{
					Gizmo.Draw.Line( v );
				}

				Gizmo.Draw.Color = edgeColor;
				Gizmo.Draw.IgnoreDepth = false;
				Gizmo.Draw.LineThickness = 2;

				foreach ( var v in mesh.Mesh.GetEdges() )
				{
					Gizmo.Draw.Line( v );
				}
			}

			using ( Gizmo.Scope( "Vertices" ) )
			{
				var vertexColor = new Color( 1.0f, 1.0f, 0.3f, 1f );

				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = vertexColor.Darken( 0.3f ).WithAlpha( 0.2f );

				foreach ( var v in mesh.Mesh.GetVertexPositions() )
				{
					Gizmo.Draw.Sprite( v, 8, null, false );
				}

				Gizmo.Draw.Color = vertexColor;
				Gizmo.Draw.IgnoreDepth = false;

				foreach ( var v in mesh.Mesh.GetVertexPositions() )
				{
					Gizmo.Draw.Sprite( v, 8, null, false );
				}
			}
		}
	}
}
