using HalfEdgeMesh;

namespace Editor.MeshEditor;

/// <summary>
/// Select and edit edges.
/// </summary>
[Title( "Edge Tool" )]
[Icon( "show_chart" )]
[Alias( "tools.edge-tool" )]
[Group( "2" )]
public sealed partial class EdgeTool( MeshTool tool ) : SelectionTool<MeshEdge>( tool )
{
	public override void OnUpdate()
	{
		base.OnUpdate();

		using var scope = Gizmo.Scope( "EdgeTool" );

		var closestEdge = GetClosestEdge( 8 );
		if ( closestEdge.IsValid() )
			Gizmo.Hitbox.TrySetHovered( closestEdge.Transform.PointToWorld( closestEdge.Line.Center ) );

		if ( Gizmo.IsHovered )
		{
			SelectEdge();

			if ( Gizmo.IsDoubleClicked )
				SelectEdgeLoop();
		}

		var edges = Selection.OfType<MeshEdge>().ToList();

		using ( Gizmo.Scope( "Edge Selection" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.Yellow;

			foreach ( var edge in edges )
			{
				Gizmo.Draw.LineThickness = edge.IsOpen ? 2 : 4;
				var line = edge.Line;
				var a = edge.Transform.PointToWorld( line.Start );
				var b = edge.Transform.PointToWorld( line.End );
				Gizmo.Draw.Line( a, b );

				if ( edge.IsOpen )
					DrawOpenEdge( edge );
			}
		}

		if ( edges.Count == 2 )
			AngleFromEdges( edges[0], edges[1] );
	}

	private static void DrawOpenEdge( MeshEdge edge )
	{
		var hFace = edge.Component.Mesh.GetHalfEdgeFace( edge.Handle );
		var spacing = 2.0f;
		if ( !hFace.IsValid )
		{
			hFace = edge.Component.Mesh.GetHalfEdgeFace( edge.Component.Mesh.GetOppositeHalfEdge( edge.Handle ) );
			spacing *= -1.0f;
		}

		var line = edge.Line;
		var a = edge.Transform.PointToWorld( line.Start );
		var b = edge.Transform.PointToWorld( line.End );

		edge.Component.Mesh.ComputeFaceNormal( hFace, out var normal );
		var direction = (line.End - line.Start).Normal;
		var length = a.Distance( b );
		var tangent = normal.Cross( direction );
		var numHashes = length / 5.0f;

		for ( int i = 0; i < numHashes; ++i )
		{
			var d = i / numHashes * length;
			var p = line.Start + direction * d;
			var a2 = edge.Transform.PointToWorld( p );
			var b2 = edge.Transform.PointToWorld( p + tangent * spacing );
			Gizmo.Draw.Line( a2, b2 );
		}
	}

	public override Rotation CalculateSelectionBasis()
	{
		if ( Gizmo.Settings.GlobalSpace )
			return Rotation.Identity;

		var edge = Selection.OfType<MeshEdge>().FirstOrDefault();
		if ( edge.IsValid() )
		{
			var line = edge.Line;
			var normal = (line.End - line.Start).Normal;
			var vAxis = ComputeTextureVAxis( normal );
			var basis = Rotation.LookAt( normal, vAxis * -1.0f );
			return edge.Transform.RotationToWorld( basis );
		}

		return Rotation.Identity;
	}

	private static void AngleFromEdges( MeshEdge edge1, MeshEdge edge2 )
	{
		if ( !edge1.IsValid() || !edge2.IsValid() )
			return;

		var line1 = edge1.Line;
		var line2 = edge2.Line;

		// Convert start and end points to world space
		var a1 = edge1.Transform.PointToWorld( line1.Start );
		var b1 = edge1.Transform.PointToWorld( line1.End );
		var a2 = edge2.Transform.PointToWorld( line2.Start );
		var b2 = edge2.Transform.PointToWorld( line2.End );

		// Check for a shared vertex
		Vector3? sharedVertex = null;
		if ( a1 == a2 || a1 == b2 ) sharedVertex = a1;
		else if ( b1 == a2 || b1 == b2 ) sharedVertex = b1;

		if ( sharedVertex.HasValue )
		{
			var vec1 = (a1 == sharedVertex.Value) ? b1 - a1 : a1 - b1;
			var vec2 = (a2 == sharedVertex.Value) ? b2 - a2 : a2 - b2;

			vec1 = vec1.Normal;
			vec2 = vec2.Normal;

			// Calculate angle
			float dotProduct = Vector3.Dot( vec1, vec2 );
			float angle = MathF.Acos( Math.Clamp( dotProduct, -1.0f, 1.0f ) ) * (180f / MathF.PI);

			// Check if Alt key is pressed and flip the angle
			var newStart = -90f;
			if ( Gizmo.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt ) && Application.FocusWidget.IsValid() )
			{
				angle = 360f - angle;
				newStart = 270f + angle;
			}

			Vector3 midPoint = sharedVertex.Value;

			// Draw angle text at midpoint of the arc
			Gizmo.Draw.Color = Color.White;
			var textSize = 16 * Gizmo.Settings.GizmoScale * Application.DpiScale;
			var cameraDistance = Gizmo.Camera.Position.Distance( midPoint );
			Gizmo.Draw.ScreenText( $"{angle:0.##}°", midPoint + ((vec1 + vec2) / 2).Normal * 7 * (cameraDistance / 100).Clamp( 1, 2f ), 0, size: textSize, flags: TextFlag.Center );

			//Draw line from the center of the arc
			Gizmo.Draw.Line( midPoint + ((vec1 + vec2) / 2).Normal * 4 * (cameraDistance / 100).Clamp( 1, 2f ), midPoint + ((vec1 + vec2) / 2).Normal * 6 * (cameraDistance / 100).Clamp( 1, 2f ) );

			using ( Gizmo.Scope( "Angle Arc" ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = Color.White;
				Gizmo.Draw.LineThickness = 2;

				Gizmo.Transform = new Transform( midPoint, Rotation.LookAt( vec1, -vec2 ) * Rotation.FromYaw( 270f ) * Rotation.FromRoll( newStart ) );
				Gizmo.Draw.LineCircle( 0, (Gizmo.Camera.Position.Distance( midPoint ) / 20).Clamp( 5, 10 ), 0, angle );
				Gizmo.Draw.Color = Color.White.WithAlpha( 0.2f );
				Gizmo.Draw.SolidCircle( 0, (Gizmo.Camera.Position.Distance( midPoint ) / 20).Clamp( 5, 10 ), 0, -angle );
			}
		}
	}

	private void SelectEdgeLoop()
	{
		var edge = GetClosestEdge( 8 );
		if ( !edge.IsValid() )
			return;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			Selection.Clear();

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			edge.Component.Mesh.FindEdgeLoopForEdges( [edge.Handle], out var hEdges );
			foreach ( var hEdge in hEdges )
				Selection.Add( new MeshEdge( edge.Component, hEdge ) );
		}
	}

	private void SelectEdge()
	{
		var edge = GetClosestEdge( 8 );
		if ( edge.IsValid() )
		{
			using ( Gizmo.Scope( "Edge Hover" ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = Color.Green;
				Gizmo.Draw.LineThickness = edge.IsOpen ? 2 : 4;

				if ( edge.IsOpen )
					DrawOpenEdge( edge );
			}

			using ( Gizmo.ObjectScope( edge.Component.GameObject, edge.Transform ) )
			{
				using ( Gizmo.Scope( "Edge Hover" ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = Color.Green;
					Gizmo.Draw.LineThickness = edge.IsOpen ? 2 : 4;

					var line = edge.Line;
					Gizmo.Draw.Line( line );

					var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;
					var distance = line.Start.Distance( line.End );
					Gizmo.Draw.Color = Color.White;
					Gizmo.Draw.ScreenText( $"{distance:0.##}", edge.Transform.PointToWorld( line.Center ), 0, size: textSize );
				}
			}
		}

		UpdateSelection( edge );
	}

	protected override IEnumerable<IMeshElement> GetAllSelectedElements()
	{
		foreach ( var group in Selection.OfType<MeshEdge>()
			.GroupBy( x => x.Component ) )
		{
			var component = group.Key;
			var mesh = component.Mesh;

			foreach ( var hEdge in mesh.HalfEdgeHandles )
			{
				if ( hEdge.Index > mesh.GetOppositeHalfEdge( hEdge ).Index )
					continue;

				yield return new MeshEdge( component, hEdge );
			}
		}
	}

	public override List<MeshFace> ExtrudeSelection( Vector3 delta = default )
	{
		var groups = Selection.OfType<MeshEdge>()
			.GroupBy( face => face.Component );

		var connectingFaces = new List<MeshFace>();
		if ( !groups.Any() )
			return connectingFaces;

		var selectedEdges = new List<MeshEdge>();

		var components = groups.Select( x => x.Key );

		using ( SceneEditorSession.Active.UndoScope( "Extrude Edges" ).WithComponentChanges( components ).Push() )
		{
			var extrudeWidth = EditorScene.GizmoSettings.GridSpacing * 0.25f;

			foreach ( var group in groups )
			{
				var component = group.Key;
				var mesh = component.Mesh;

				if ( group.Any( x => !x.IsOpen ) )
				{
					var newFaces = new List<FaceHandle>();
					var edges = group.Select( x => x.Handle ).ToList();
					if ( !mesh.BevelEdges( edges, PolygonMesh.BevelEdgesMode.LeaveOriginalEdges, 1, extrudeWidth, 0.0f, null, null, newFaces ) )
						continue;

					foreach ( var edge in edges )
					{
						selectedEdges.Add( new MeshEdge( component, edge ) );
					}

					foreach ( var face in newFaces )
					{
						connectingFaces.Add( new MeshFace( component, face ) );
					}
				}
				else
				{
					var edges = group.Select( x => x.Handle ).ToList();
					if ( !mesh.ExtendEdges( edges, 0.0f, out var newEdges, out var newFaces ) )
						continue;

					foreach ( var face in newFaces )
					{
						connectingFaces.Add( new MeshFace( component, face ) );
					}

					foreach ( var edge in newEdges )
					{
						selectedEdges.Add( new MeshEdge( component, edge ) );
					}
				}
			}
		}

		Selection.Clear();

		foreach ( var edge in selectedEdges )
		{
			Selection.Add( edge );
		}

		CalculateSelectionVertices();

		return connectingFaces;
	}
}
