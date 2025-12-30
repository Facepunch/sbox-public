using HalfEdgeMesh;

namespace Editor.MeshEditor;

[Alias( "tools.edge-cut-tool" )]
public partial class EdgeCutTool( string tool ) : EditorTool
{
	static int SelectionSampleRadius => 8;

	MeshComponent _hoveredMesh;
	MeshCutPoint _previewCutPoint;
	readonly List<MeshCutPoint> _cutPoints = [];

	struct MeshCutPoint : IValid
	{
		public MeshFace Face;
		public MeshVertex Vertex;
		public MeshEdge Edge;
		public Vector3 WorldPosition;
		public Vector3 LocalPosition;
		public Vector3 BasePosition;
		public MeshComponent Component;

		public readonly bool IsValid => Face.IsValid();

		void SetWorldPosition( Vector3 position )
		{
			WorldPosition = position;
			LocalPosition = Face.Component.WorldTransform.PointToLocal( position );

			if ( Vertex.IsValid() )
			{
				BasePosition = Vertex.PositionLocal;
			}
			else if ( Edge.IsValid() )
			{
				var mesh = Edge.Component.Mesh;
				mesh.GetVerticesConnectedToEdge( Edge.Handle, out var hVertexA, out var hVertexB );
				mesh.ComputeClosestPointOnEdge( hVertexA, hVertexB, LocalPosition, out var flBaseParam );
				mesh.GetVertexPosition( hVertexA, Transform.Zero, out var vPositionA );
				mesh.GetVertexPosition( hVertexB, Transform.Zero, out var vPositionB );
				BasePosition = vPositionA.LerpTo( vPositionB, flBaseParam );
			}
			else
			{
				BasePosition = LocalPosition;
			}
		}

		public MeshCutPoint( MeshFace face, MeshVertex vertex )
		{
			Face = face;
			Vertex = vertex;
			Component = Face.Component;
			SetWorldPosition( vertex.PositionWorld );
		}

		public MeshCutPoint( MeshFace face, MeshEdge edge, Vector3 point )
		{
			Face = face;
			Edge = edge;
			Component = Face.Component;
			SetWorldPosition( point );
		}

		public MeshCutPoint( MeshFace face, Vector3 point )
		{
			Face = face;
			Component = Face.Component;
			SetWorldPosition( point );
		}

		public readonly void GetConnectedFaces( out List<FaceHandle> outFaces )
		{
			outFaces = [];

			if ( Component.IsValid() == false ) return;

			var mesh = Component.Mesh;

			if ( Vertex.IsValid() )
			{
				mesh.GetFacesConnectedToVertex( Vertex.Handle, out outFaces );
			}
			else if ( Edge.IsValid() )
			{
				mesh.GetFacesConnectedToEdge( Edge.Handle, out var hFaceA, out var hFaceB );
				outFaces.Add( hFaceA );
				outFaces.Add( hFaceB );
			}
			else
			{
				outFaces.Add( Face.Handle );
			}
		}
	}

	readonly string _tool = tool;
	bool _cancel;

	public override void OnDisabled()
	{
		Cancel();
	}

	public override void OnUpdate()
	{
		var escape = Application.IsKeyDown( KeyCode.Escape );
		if ( escape && !_cancel ) Cancel();
		_cancel = escape;

		_previewCutPoint = ShouldSnap() ? FindSnappedCutPoint() : FindCutPoint();

		MeshCutPoint previousCutPoint = default;
		if ( _cutPoints.Count > 0 )
		{
			previousCutPoint = _cutPoints.Last();
		}

		if ( previousCutPoint.IsValid() )
		{
			var sharedFace = FindSharedFace( previousCutPoint, _previewCutPoint );
			if ( sharedFace.IsValid() == false )
			{
				_previewCutPoint = default;
			}
		}

		if ( !Gizmo.Pressed.Any )
		{
			if ( Gizmo.WasLeftMousePressed )
			{
				PlaceCutPoint();
			}
		}

		DrawCutPoints();
		DrawPreview();
		DrawMesh( _hoveredMesh );
	}

	void PlaceCutPoint()
	{
		if ( _previewCutPoint.IsValid() == false ) return;

		_cutPoints.Add( _previewCutPoint );
	}

	MeshCutPoint FindCutPoint()
	{
		var trace = MeshTrace;
		MeshCutPoint newCutPoint = default;
		{
			var vertex = trace.GetClosestVertex( SelectionSampleRadius, out var face );
			if ( vertex.IsValid() ) newCutPoint = new MeshCutPoint( face, vertex );
		}

		if ( newCutPoint.IsValid() == false )
		{
			var edge = trace.GetClosestEdge( SelectionSampleRadius, out var face, out var hitPosition );
			var transform = edge.Transform;
			var pointOnEdge = edge.Line.ClosestPoint( transform.PointToLocal( hitPosition ) );
			if ( edge.IsValid() ) newCutPoint = new MeshCutPoint( face, edge, transform.PointToWorld( pointOnEdge ) );
		}

		if ( _cutPoints.Count > 0 && newCutPoint.IsValid() == false )
		{
			var face = trace.TraceFace( out var hitPosition );
			if ( face.IsValid() ) newCutPoint = new MeshCutPoint( face, hitPosition );
		}

		return newCutPoint;
	}
}
