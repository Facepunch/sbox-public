
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
					var color = new Color( 0.3137f, 0.7843f, 1.0f, 1f );
					var prev = _cutPoints[i - 1];
					var curr = _cutPoints[i];

					if ( prev.Edge.IsValid() && curr.Edge.IsValid() && prev.Component == curr.Component )
					{
						var meshObj = prev.Component.Mesh;
						meshObj.GetVerticesConnectedToEdge( prev.Edge.Handle, out var pvA, out var pvB );
						meshObj.GetVertexPosition( pvA, prev.Component.WorldTransform, out var pp0 );
						meshObj.GetVertexPosition( pvB, prev.Component.WorldTransform, out var pp1 );

						meshObj.GetVerticesConnectedToEdge( curr.Edge.Handle, out var cvA, out var cvB );
						meshObj.GetVertexPosition( cvA, curr.Component.WorldTransform, out var cp0 );
						meshObj.GetVertexPosition( cvB, curr.Component.WorldTransform, out var cp1 );

						var prevDir = (pp1 - pp0).Normal;
						var currDir = (cp1 - cp0).Normal;
						var angle = MathF.Acos( Vector3.Dot( prevDir, currDir ) ) * (180f / MathF.PI);

						if ( MathF.Abs( angle - 90f ) < 1f ) // Close to 90 degrees
						{
							color = Color.Orange;
						}
					}

					Gizmo.Draw.Color = color;
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
		var component = _previewCutPoint.Component;
		var meshObj = component.Mesh;
		Vector3 p0 = default, p1 = default;
		if ( edge.IsValid() )
		{
			meshObj.GetVerticesConnectedToEdge( edge.Handle, out var vA, out var vB );
			meshObj.GetVertexPosition( vA, component.WorldTransform, out p0 );
			meshObj.GetVertexPosition( vB, component.WorldTransform, out p1 );

			// Determine color based on angle if previous cut exists
			var lineColor = Color.Green;

			using ( Gizmo.Scope( "Edge Hover", _previewCutPoint.Edge.Transform ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = lineColor;
				Gizmo.Draw.LineThickness = 4;
				Gizmo.Draw.Line( edge.Line );
			}

			// Add snapping feedback
			var edgeLength = p0.Distance( p1 );
			var cutPos = _previewCutPoint.WorldPosition;
			var distFromA = p0.Distance( cutPos );
			var ratio = distFromA / edgeLength;

			var unitsText = $"{distFromA:F2} units";
			var ratioText = $"{ratio:P0}";

			var textColor = Color.White;

			// Draw text near the cut point
			var textPos = cutPos + Vector3.Up * 10f; // Offset above
			if ( _showSnappingInfo )
			{
				Gizmo.Draw.Color = textColor;
				Gizmo.Draw.Text( unitsText, new Transform( textPos ), "default", 12f );
				Gizmo.Draw.Text( ratioText, new Transform( textPos + Vector3.Up * 15f ), "default", 12f );
			}
		}

		using ( Gizmo.Scope( "Point" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			if ( _cutPoints.Count > 0 )
			{
				var lastCutPoint = _cutPoints.Last();
				Gizmo.Draw.LineThickness = 4;
				var previewLineColor = Color.White;
				if ( lastCutPoint.Edge.IsValid() && edge.IsValid() && lastCutPoint.Component == component )
				{
					meshObj.GetVerticesConnectedToEdge( lastCutPoint.Edge.Handle, out var lvA, out var lvB );
					meshObj.GetVertexPosition( lvA, component.WorldTransform, out var lp0 );
					meshObj.GetVertexPosition( lvB, component.WorldTransform, out var lp1 );

					var lastDir = (lp1 - lp0).Normal;
					var currentDir = (p1 - p0).Normal;
					var angle = MathF.Acos( Vector3.Dot( lastDir, currentDir ) ) * (180f / MathF.PI);

					if ( MathF.Abs( angle - 90f ) < 5f ) // Close to 90 degrees
					{
						previewLineColor = Color.Orange;
					}
				}
				Gizmo.Draw.Color = previewLineColor;
				Gizmo.Draw.Line( _previewCutPoint.WorldPosition, lastCutPoint.WorldPosition );
			}

			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.Sprite( _previewCutPoint.WorldPosition, 10, null, false );
		}
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
