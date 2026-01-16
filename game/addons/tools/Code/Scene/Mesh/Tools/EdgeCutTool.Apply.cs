using HalfEdgeMesh;
using Sandbox.Diagnostics;

namespace Editor.MeshEditor;

partial class EdgeCutTool
{
	sealed class HalfEdgeHandleComparer : IComparer<HalfEdgeHandle>
	{
		public static readonly HalfEdgeHandleComparer Instance = new();

		public int Compare( HalfEdgeHandle a, HalfEdgeHandle b )
		{
			return a.Index.CompareTo( b.Index );
		}
	}

	void Cancel()
	{
		if ( _cutPoints.Count == 0 )
		{
			EditorToolManager.SetSubTool( _tool );
		}

		_cutPoints.Clear();
	}

	void Apply()
	{
		if ( _cutPoints.Count <= 1 ) return;

		var components = new HashSet<MeshComponent>( _cutPoints.Count );
		foreach ( var cutPoint in _cutPoints )
		{
			var component = cutPoint.Face.Component;
			if ( component.IsValid() ) components.Add( component );
		}

		using var undoScope = SceneEditorSession.Active.UndoScope( "Apply Edge Cut" )
			.WithComponentChanges( components )
			.Push();

		var vertices = new List<MeshVertex>();
		var edges = new List<MeshEdge>();
		if ( ApplyCut( vertices, edges ) == false ) return;

		var selection = SceneEditorSession.Active.Selection;
		selection.Clear();

		foreach ( var vertex in vertices ) selection.Add( vertex );
		foreach ( var edge in edges ) selection.Add( edge );

		foreach ( var component in components )
		{
			var mesh = component.Mesh;
			foreach ( var edge in edges )
			{
				mesh.GetFacesConnectedToEdge( edge.Handle, out var faceA, out var faceB );
				selection.Add( new MeshFace( component, faceA ) );
				selection.Add( new MeshFace( component, faceB ) );
			}
		}

		EditorToolManager.SetSubTool( _tool );
	}

	bool ApplyCut( List<MeshVertex> outCutPathVertices, List<MeshEdge> outCutPathEdges )
	{
		var cutPoints = _cutPoints;
		if ( cutPoints.Count == 0 ) return false;

		var components = new List<MeshComponent>( cutPoints.Count );
		var meshes = new List<PolygonMesh>( cutPoints.Count );

		foreach ( var cp in cutPoints )
		{
			var component = cp.Face.Component;
			if ( !component.IsValid() ) continue;

			var mesh = component.Mesh;
			if ( meshes.Contains( mesh ) ) continue;

			meshes.Add( mesh );
			components.Add( component );
		}

		var edgeTables = new List<SortedSet<HalfEdgeHandle>>( meshes.Count );
		for ( int i = 0; i < meshes.Count; i++ ) edgeTables.Add( new SortedSet<HalfEdgeHandle>( HalfEdgeHandleComparer.Instance ) );

		int startIndex = 0;
		MeshVertex startVertex = default;
		MeshEdge edgeToRemove = default;

		while ( startIndex < cutPoints.Count )
		{
			while ( !startVertex.IsValid() && startIndex < cutPoints.Count )
			{
				var startPoint = cutPoints[startIndex];

				if ( startPoint.IsValid() )
				{
					if ( startPoint.Edge.IsValid() )
					{
						var component = startPoint.Edge.Component;
						int meshIndex = components.IndexOf( component );
						Assert.True( meshIndex != -1 );

						var hNewVertex = AddCutToEdge( startPoint.Edge, startPoint.BasePosition, edgeTables[meshIndex] );
						startVertex = new MeshVertex( component, hNewVertex );
						break;
					}

					if ( startPoint.Vertex.IsValid() )
					{
						startVertex = startPoint.Vertex;
						break;
					}
				}

				startIndex++;
			}

			int endIndex = startIndex + 1;
			MeshVertex endVertex = default;

			if ( endIndex < cutPoints.Count )
			{
				var endPoint = cutPoints[endIndex];
				if ( endPoint.Face.IsValid() && endPoint.Face.Component == startVertex.Component )
				{
					var component = startVertex.Component;
					var mesh = component.Mesh;
					int meshIndex = components.IndexOf( component );
					Assert.True( meshIndex != -1 );

					var edgeTable = edgeTables[meshIndex];
					var targetVertex = mesh.CreateEdgesConnectingVertexToPoint( startVertex.Handle, endPoint.BasePosition,
						out var segmentEdges, out var isLastConnector, edgeTable );

					if ( edgeToRemove.IsValid() && !segmentEdges.Contains( edgeToRemove.Handle ) )
					{
						mesh.GetVerticesConnectedToEdge( edgeToRemove.Handle, out var a, out var b );
						mesh.DissolveEdge( edgeToRemove.Handle );
						edgeTable.Remove( edgeToRemove.Handle );
						mesh.RemoveColinearVertexAndUpdateTable( a, edgeTable );
						mesh.RemoveColinearVertexAndUpdateTable( b, edgeTable );
					}

					if ( endPoint.Face.IsValid() && !endPoint.Vertex.IsValid() && !endPoint.Edge.IsValid() && segmentEdges.Count > 1 && isLastConnector )
					{
						edgeToRemove = new MeshEdge( component, segmentEdges[^1] );
					}

					endVertex = new MeshVertex( component, targetVertex );
				}
			}

			startIndex = endIndex;
			startVertex = endVertex;
		}

		if ( outCutPathEdges is not null || outCutPathVertices is not null )
		{
			var numMeshes = meshes.Count;
			var totalEdgeCount = 0;

			foreach ( var edgeTable in edgeTables )
			{
				totalEdgeCount += edgeTable.Count;
			}

			if ( outCutPathEdges is not null )
			{
				outCutPathEdges.Clear();
				outCutPathEdges.EnsureCapacity( totalEdgeCount );
			}

			var vertexSet = new HashSet<MeshVertex>( totalEdgeCount * 2 );

			for ( int i = 0; i < numMeshes; ++i )
			{
				var component = components[i];
				var mesh = component.Mesh;
				var edgeTable = edgeTables[i];

				foreach ( var hEdge in edgeTable )
				{
					if ( hEdge.IsValid == false ) continue;

					outCutPathEdges?.Add( new MeshEdge( component, hEdge ) );

					if ( outCutPathVertices is not null )
					{
						mesh.GetVerticesConnectedToEdge( hEdge, out var hVertexA, out var hVertexB );
						vertexSet.Add( new MeshVertex( component, hVertexA ) );
						vertexSet.Add( new MeshVertex( component, hVertexB ) );
					}
				}
			}

			if ( outCutPathVertices is not null )
			{
				outCutPathVertices.Clear();
				outCutPathVertices.EnsureCapacity( vertexSet.Count );

				foreach ( var hVertex in vertexSet )
				{
					outCutPathVertices.Add( hVertex );
				}
			}
		}

		foreach ( var mesh in meshes )
		{
			mesh.ComputeFaceTextureCoordinatesFromParameters();
		}

		return true;
	}

	static MeshFace FindSharedFace( MeshCutPoint cutPointA, MeshCutPoint cutPointB )
	{
		if ( cutPointA.IsValid() == false ) return default;
		if ( cutPointB.IsValid() == false ) return default;
		if ( cutPointA.Component != cutPointB.Component ) return default;

		cutPointA.GetConnectedFaces( out var connectedFacesA );
		cutPointB.GetConnectedFaces( out var connectedFacesB );

		foreach ( var face in connectedFacesA )
		{
			if ( face.IsValid && connectedFacesB.Contains( face ) )
			{
				return new MeshFace( cutPointA.Component, face );
			}
		}

		return default;
	}

	static VertexHandle AddCutToEdge( MeshEdge edge, Vector3 targetPosition, SortedSet<HalfEdgeHandle> edgeTable )
	{
		if ( !edge.Component.IsValid() ) return VertexHandle.Invalid;

		var mesh = edge.Component.Mesh;
		var visited = new List<HalfEdgeHandle>( 32 );
		var current = edge.Handle;
		const float eps = 0.001f;

		while ( current != HalfEdgeHandle.Invalid )
		{
			mesh.GetVerticesConnectedToEdge( current, out var a, out var b );
			mesh.GetVertexPosition( a, Transform.Zero, out var pa );
			mesh.GetVertexPosition( b, Transform.Zero, out var pb );
			ClosestPointOnLine( targetPosition, pa, pb, out _, out var t );

			VertexHandle next;
			if ( t > 1f + eps ) next = b;
			else if ( t < -eps ) next = a;
			else if ( t <= eps ) return a;
			else if ( t >= 1f - eps ) return b;
			else
			{
				mesh.AddVertexToEdgeAndUpdateTable( a, b, t, out var v, edgeTable );
				return v;
			}

			visited.Add( current );
			var prev = current;
			current = HalfEdgeHandle.Invalid;

			mesh.GetEdgesConnectedToVertex( next, out var edges );
			foreach ( var e in edges )
			{
				if ( !visited.Contains( e ) && mesh.AreEdgesCoLinear( e, prev, 1.0f ) )
				{
					current = e;
					break;
				}
			}
		}

		return VertexHandle.Invalid;
	}

	static void ClosestPointOnLine( Vector3 p, Vector3 a, Vector3 b, out Vector3 closest, out float t )
	{
		var d = b - a;
		var div = d.Dot( d );
		t = div < 1e-5f ? 0f : (d.Dot( p ) - d.Dot( a )) / div;
		closest = a + d * t;
	}
}
