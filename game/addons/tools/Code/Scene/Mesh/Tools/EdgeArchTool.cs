using HalfEdgeMesh;
using System.Text.Json.Serialization;

namespace Editor.MeshEditor;

public struct EdgeArchEdges
{
	[Hide, JsonInclude] public MeshComponent Component { get; set; }
	[Hide, JsonInclude] public PolygonMesh Mesh { get; set; }
	[Hide, JsonInclude] public List<int> Edges { get; set; }
}

[Alias( "tools.edge-arch-tool" )]
public partial class EdgeArchTool( EdgeArchEdges[] edges ) : EditorTool
{
	private readonly Dictionary<MeshComponent, PolygonMesh> _originalMeshes = new();
	private readonly Dictionary<MeshComponent, List<VertexHandle>> _edgeVertices = new();
	private readonly Dictionary<MeshComponent, List<HalfEdgeHandle>> _newEdges = new();

	public override void OnEnabled()
	{
		base.OnEnabled();

		foreach ( var edgeGroup in edges )
		{
			if ( !edgeGroup.Component.IsValid() ) continue;

			var originalMesh = new PolygonMesh();
			originalMesh.Transform = edgeGroup.Mesh.Transform;
			originalMesh.MergeMesh( edgeGroup.Mesh, Transform.Zero, out _, out _, out _ );

			_originalMeshes[edgeGroup.Component] = originalMesh;
		}
	}

	public override void OnUpdate()
	{
		if ( edges.Length == 0 ) return;

		foreach ( var group in edges )
		{
			var comp = group.Component;
			if ( !comp.IsValid() ) continue;

			if ( !_newEdges.TryGetValue( comp, out var newEdgeHandles ) )
				continue;

			using ( Gizmo.ObjectScope( comp.GameObject, comp.WorldTransform ) )
			using ( Gizmo.Scope( "EdgeArcs" ) )
			{
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = Color.Yellow;
				Gizmo.Draw.LineThickness = 2;

				foreach ( var edgeHandle in newEdgeHandles )
				{
					comp.Mesh.GetVerticesConnectedToEdge( edgeHandle, out var vertexA, out var vertexB );
					comp.Mesh.GetVertexPosition( vertexA, Transform.Zero, out var posA );
					comp.Mesh.GetVertexPosition( vertexB, Transform.Zero, out var posB );

					Gizmo.Draw.Line( posA, posB );

					if ( edgeHandle != newEdgeHandles[0] )
						Gizmo.Draw.Sprite( posA, 8f, null, false );
				}

				var startVertex = newEdgeHandles[0];
				comp.Mesh.GetVerticesConnectedToEdge( startVertex, out var vertexStart, out var vertexEnd );
				comp.Mesh.GetVertexPosition( vertexStart, Transform.Zero, out var startPos );
				var endVertexHandle = newEdgeHandles[newEdgeHandles.Count - 1];
				comp.Mesh.GetVerticesConnectedToEdge( endVertexHandle, out var vertexStartEnd, out var vertexEndEnd );
				comp.Mesh.GetVertexPosition( vertexEndEnd, Transform.Zero, out var endPos );

				Gizmo.Draw.Color = Color.Cyan;
				Gizmo.Draw.Sprite( startPos, 8f, null, false );
				Gizmo.Draw.Sprite( endPos, 8f, null, false );
			}
		}
	}

	private static Vector3[] ComputeControlPoints( PolygonMesh mesh, HalfEdgeHandle edgeHandle,
		float arcHeight, float arcOffset )
	{
		var usedOpposite = false;
		var faceHandle = mesh.GetHalfEdgeFace( edgeHandle );
		if ( !faceHandle.IsValid )
		{
			var oppositeEdge = mesh.GetOppositeHalfEdge( edgeHandle );
			faceHandle = mesh.GetHalfEdgeFace( oppositeEdge );
			usedOpposite = true;
		}

		if ( !faceHandle.IsValid )
		{
			return new Vector3[4];
		}

		mesh.GetVerticesConnectedToEdge( edgeHandle, out var vertexA, out var vertexB );
		mesh.GetVertexPosition( vertexA, Transform.Zero, out var startPos );
		mesh.GetVertexPosition( vertexB, Transform.Zero, out var endPos );

		mesh.ComputeFaceNormal( faceHandle, out var faceNormal );

		var edgeVec = endPos - startPos;
		var edgeDir = edgeVec.Normal;

		var arcDir = faceNormal.Cross( edgeDir ).Normal;

		if ( usedOpposite )
			arcDir = -arcDir;

		var scaledHeight = arcHeight * (4.0f / 3.0f);
		var scaledOffset = arcOffset * (4.0f / 3.0f);

		scaledOffset = Math.Max( scaledOffset, -edgeVec.Length );

		var controlPoints = new Vector3[4];
		controlPoints[0] = startPos;
		controlPoints[1] = startPos + (arcDir * scaledHeight) - (edgeDir * scaledOffset);
		controlPoints[2] = endPos + (arcDir * scaledHeight) + (edgeDir * scaledOffset);
		controlPoints[3] = endPos;

		var sideA = controlPoints[1] - controlPoints[0];
		var sideADir = sideA.Normal;
		var sideALength = sideA.Length * 0.75f;
		controlPoints[1] = controlPoints[0] + sideADir * sideALength;

		var sideB = controlPoints[2] - controlPoints[3];
		var sideBDir = sideB.Normal;
		var sideBLength = sideB.Length * 0.75f;
		controlPoints[2] = controlPoints[3] + sideBDir * sideBLength;

		return controlPoints;
	}

	private static Vector3 EvaluateBezier( Vector3[] controlPoints, float t )
	{
		var u = 1 - t;
		var tt = t * t;
		var uu = u * u;
		var uuu = uu * u;
		var ttt = tt * t;

		var point = uuu * controlPoints[0];
		point += 3 * uu * t * controlPoints[1];
		point += 3 * u * tt * controlPoints[2];
		point += ttt * controlPoints[3];

		return point;
	}

	public void UpdateArch( int numSteps, float arcHeight, float arcOffset )
	{
		_edgeVertices.Clear();
		_newEdges.Clear();

		foreach ( var edgeGroup in edges )
		{
			var component = edgeGroup.Component;
			if ( !component.IsValid() ) continue;

			if ( !_originalMeshes.TryGetValue( component, out var originalMesh ) )
				continue;

			var mesh = new PolygonMesh();
			mesh.Transform = originalMesh.Transform;
			mesh.MergeMesh( originalMesh, Transform.Zero, out _, out _, out _ );

			var edgeVertices = new List<VertexHandle>();
			var newEdges = new List<HalfEdgeHandle>();

			foreach ( var edgeIndex in edgeGroup.Edges )
			{
				var edgeHandle = mesh.HalfEdgeHandleFromIndex( edgeIndex );

				if ( !mesh.IsEdgeOpen( edgeHandle ) )
					continue;

				mesh.GetVerticesConnectedToEdge( edgeHandle, out var startVertex, out var endVertex );

				var vertexList = SubdivideEdge( mesh, startVertex, endVertex, numSteps, out var edgeList );

				var originalEdgeHandle = originalMesh.HalfEdgeHandleFromIndex( edgeIndex );
				var controlPoints = ComputeControlPoints( originalMesh, originalEdgeHandle, arcHeight, arcOffset );

				ApplyArcToVertices( mesh, vertexList, controlPoints );

				edgeVertices.AddRange( vertexList );
				newEdges.AddRange( edgeList );
			}

			mesh.ComputeFaceTextureCoordinatesFromParameters();
			component.Mesh = mesh;

			_edgeVertices[component] = edgeVertices;
			_newEdges[component] = newEdges;
		}
	}

	private static List<VertexHandle> SubdivideEdge( PolygonMesh mesh, VertexHandle startVertex, VertexHandle endVertex,
		int numSteps, out List<HalfEdgeHandle> edges )
	{
		edges = new List<HalfEdgeHandle>();
		var vertices = new List<VertexHandle> { startVertex };

		if ( numSteps <= 0 )
		{
			vertices.Add( endVertex );
			return vertices;
		}

		var currentVertex = startVertex;

		for ( int i = 0; i < numSteps - 1; i++ )
		{
			mesh.AddVertexToEdge( currentVertex, endVertex, 0.5f, out var newVertex );
			vertices.Add( newVertex );

			var edgeHandles = mesh.HalfEdgeHandles.Where( e =>
			{
				mesh.GetVerticesConnectedToEdge( e, out var a, out var b );
				return (a == currentVertex && b == newVertex) || (a == newVertex && b == currentVertex);
			} ).ToList();

			if ( edgeHandles.Count > 0 )
				edges.Add( edgeHandles[0] );

			currentVertex = newVertex;
		}

		var finalEdgeHandles = mesh.HalfEdgeHandles.Where( e =>
		{
			mesh.GetVerticesConnectedToEdge( e, out var a, out var b );
			return (a == currentVertex && b == endVertex) || (a == endVertex && b == currentVertex);
		} ).ToList();

		if ( finalEdgeHandles.Count > 0 )
			edges.Add( finalEdgeHandles[0] );

		vertices.Add( endVertex );

		return vertices;
	}

	private static void ApplyArcToVertices( PolygonMesh mesh, List<VertexHandle> vertices, Vector3[] controlPoints )
	{
		var numPoints = vertices.Count;
		if ( numPoints < 2 ) return;

		for ( int i = 0; i < numPoints; i++ )
		{
			float t = i / (float)(numPoints - 1);
			var position = EvaluateBezier( controlPoints, t );
			mesh.SetVertexPosition( vertices[i], position );
		}
	}
}
