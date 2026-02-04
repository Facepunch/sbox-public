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

		var closestEdge = MeshTrace.GetClosestEdge( 8 );
		if ( closestEdge.IsValid() )
		{
			Gizmo.Hitbox.TrySetHovered( closestEdge.Transform.PointToWorld( closestEdge.Line.Center ) );
		}
		else
		{
			var result = MeshTrace.Run();
			if ( result.Hit && result.Component is MeshComponent )
			{
				Gizmo.Hitbox.TrySetHovered( result.EndPosition );
			}
		}

		if ( Gizmo.IsHovered && Tool.MoveMode.AllowSceneSelection && !IsLassoSelecting )
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

	protected override IEnumerable<MeshEdge> ConvertSelectionToCurrentType()
	{
		var selectedFaces = Selection.OfType<MeshFace>().ToHashSet();
		var selectedVertices = Selection.OfType<MeshVertex>().ToHashSet();

		foreach ( var face in selectedFaces )
		{
			if ( !face.IsValid() )
				continue;

			var mesh = face.Component.Mesh;
			var edges = mesh.GetFaceEdges( face.Handle );

			foreach ( var edge in edges )
			{
				if ( edge.IsValid )
					yield return new MeshEdge( face.Component, edge );
			}
		}

		var candidateEdges = new HashSet<MeshEdge>();

		foreach ( var vertex in selectedVertices )
		{
			if ( !vertex.IsValid() )
				continue;

			var mesh = vertex.Component.Mesh;
			mesh.GetEdgesConnectedToVertex( vertex.Handle, out var edges );

			foreach ( var edge in edges )
			{
				if ( edge.IsValid )
					candidateEdges.Add( new MeshEdge( vertex.Component, edge ) );
			}
		}

		foreach ( var edge in candidateEdges )
		{
			var mesh = edge.Component.Mesh;
			mesh.GetEdgeVertices( edge.Handle, out var vertexA, out var vertexB );

			bool bothVerticesSelected =
				selectedVertices.Contains( new MeshVertex( edge.Component, vertexA ) ) &&
				selectedVertices.Contains( new MeshVertex( edge.Component, vertexB ) );

			if ( bothVerticesSelected )
			{
				yield return edge;
			}
		}
	}

	private static void DrawOpenEdge( MeshEdge edge )
	{
		var mesh = edge.Component.Mesh;
		var hFace = mesh.GetHalfEdgeFace( edge.Handle );
		var spacing = 1.5f;

		if ( !hFace.IsValid )
		{
			hFace = mesh.GetHalfEdgeFace( mesh.GetOppositeHalfEdge( edge.Handle ) );
			spacing *= -1.0f;
		}

		if ( !hFace.IsValid )
			return;

		var line = edge.Line;
		var a = edge.Transform.PointToWorld( line.Start );
		var b = edge.Transform.PointToWorld( line.End );
		var length = a.Distance( b );

		mesh.ComputeFaceNormal( hFace, out var normal );
		var direction = (b - a).Normal;
		var tangent = normal.Cross( direction );

		var cameraDistance = Gizmo.Camera.Position.Distance( (a + b) * 0.5f );
		var visualScale = (cameraDistance * 0.008f).Clamp( 0.05f, 3f );

		spacing *= visualScale;

		var hashSpacing = (2.5f * visualScale).Clamp( 0.5f, 50f );
		var numHashes = Math.Max( 3, (int)(length / hashSpacing) );

		for ( int i = 0; i < numHashes; i++ )
		{
			var t = i / (float)(numHashes - 1);
			var position = Vector3.Lerp( a, b, t );
			var hashEnd = position + tangent * spacing;
			Gizmo.Draw.Line( position, hashEnd );
		}
	}

	public override Rotation CalculateSelectionBasis()
	{
		if ( GlobalSpace ) return Rotation.Identity;

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
		var targetEdge = MeshTrace.GetClosestEdge( 8 );
		if ( !targetEdge.IsValid() )
			return;

		if ( Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) && TrySelectEdgePath( targetEdge ) )
			return;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift ) )
			Selection.Clear();

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl ) )
		{
			targetEdge.Component.Mesh.FindEdgeLoopForEdges( [targetEdge.Handle], out var hEdges );
			foreach ( var hEdge in hEdges )
				Selection.Add( new MeshEdge( targetEdge.Component, hEdge ) );
		}
	}

	private bool TrySelectEdgePath( MeshEdge targetEdge )
	{
		var selected = Selection.OfType<MeshEdge>()
			.Where( e => e.IsValid() && e.Component == targetEdge.Component )
			.ToList();

		if ( selected.Count == 0 || selected.Count > 2 )
			return false;

		var startEdge = selected.FirstOrDefault( e =>
			e.Handle != targetEdge.Handle &&
			e.Handle != targetEdge.Component.Mesh.GetOppositeHalfEdge( targetEdge.Handle )
		);

		if ( !startEdge.IsValid() )
			return false;

		var path = FindShortestEdgePath( startEdge, targetEdge );
		if ( path == null || path.Count == 0 )
			return false;

		foreach ( var edge in path.Where( e => !Selection.Contains( e ) ) )
			Selection.Add( edge );

		return true;
	}

	private List<MeshEdge> FindShortestEdgePath( MeshEdge start, MeshEdge end )
	{
		if ( start.Component != end.Component )
			return null;

		var mesh = start.Component.Mesh;
		var queue = new Queue<HalfEdgeHandle>();
		var visited = new HashSet<HalfEdgeHandle>();
		var parent = new Dictionary<HalfEdgeHandle, HalfEdgeHandle>();

		var startHandle = start.Handle.Index < mesh.GetOppositeHalfEdge( start.Handle ).Index ?
			start.Handle : mesh.GetOppositeHalfEdge( start.Handle );
		var endHandle = end.Handle.Index < mesh.GetOppositeHalfEdge( end.Handle ).Index ?
			end.Handle : mesh.GetOppositeHalfEdge( end.Handle );

		queue.Enqueue( startHandle );
		visited.Add( startHandle );

		while ( queue.Count > 0 )
		{
			var current = queue.Dequeue();

			if ( current == endHandle || mesh.GetOppositeHalfEdge( current ) == endHandle )
			{
				var path = new List<MeshEdge>();
				var step = current;

				while ( step.IsValid )
				{
					path.Add( new MeshEdge( start.Component, step ) );
					if ( step == startHandle || mesh.GetOppositeHalfEdge( step ) == startHandle )
						break;
					if ( !parent.TryGetValue( step, out step ) )
						break;
				}

				path.Reverse();
				return path;
			}

			mesh.GetEdgeVertices( current, out var vertexA, out var vertexB );

			mesh.GetEdgesConnectedToVertex( vertexA, out var edgesA );
			mesh.GetEdgesConnectedToVertex( vertexB, out var edgesB );

			foreach ( var neighbor in edgesA.Concat( edgesB ) )
			{
				if ( neighbor.IsValid && !visited.Contains( neighbor ) )
				{
					var oppositeNeighbor = mesh.GetOppositeHalfEdge( neighbor );
					if ( !visited.Contains( oppositeNeighbor ) )
					{
						visited.Add( neighbor );
						visited.Add( oppositeNeighbor );
						parent[neighbor] = current;
						parent[oppositeNeighbor] = current;
						queue.Enqueue( neighbor );
					}
				}
			}
		}

		return null;
	}

	private void SelectEdge()
	{
		var edge = MeshTrace.GetClosestEdge( 8 );
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

					var textScope = new TextRendering.Scope
					{
						Text = $"{distance:0.##}",
						TextColor = Color.White,
						FontSize = textSize,
						FontName = "Roboto Mono",
						FontWeight = 400,
						LineHeight = 1,
						Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
					};

					Gizmo.Draw.ScreenText( textScope, edge.Transform.PointToWorld( line.Center ), 0 );
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

	protected override IEnumerable<MeshEdge> GetConnectedSelectionElements()
	{
		var unique = new HashSet<MeshEdge>();

		foreach ( var component in Selection.OfType<GameObject>()
			.Select( x => x.GetComponent<MeshComponent>() )
			.Where( x => x.IsValid() ) )
		{
			foreach ( var edge in component.Mesh.HalfEdgeHandles )
			{
				unique.Add( new MeshEdge( component, edge ) );
			}
		}

		foreach ( var face in Selection.OfType<MeshFace>() )
		{
			face.Component.Mesh.GetEdgesConnectedToFace( face.Handle, out var edges );

			foreach ( var edge in edges )
			{
				unique.Add( new MeshEdge( face.Component, edge ) );
			}
		}

		foreach ( var vertex in Selection.OfType<MeshVertex>() )
		{
			vertex.Component.Mesh.GetEdgesConnectedToVertex( vertex.Handle, out var edges );

			foreach ( var edge in edges )
			{
				unique.Add( new MeshEdge( vertex.Component, edge ) );
			}
		}

		return unique;
	}
}
