using HalfEdgeMesh;

namespace Editor.MeshEditor;

partial class EdgeCutTool
{
	bool _loopClosed;

	void UpdateLoopCut()
	{
		_loopPreview = null;

		_previewCutPoint = ShouldSnap() ? FindSnappedCutPoint() : FindCutPoint();

		if ( !_previewCutPoint.IsValid() || !_previewCutPoint.Edge.IsValid() )
			return;

		var cutPoint = _previewCutPoint;
		var component = cutPoint.Component;
		var mesh = component.Mesh;

		mesh.GetVerticesConnectedToEdge( cutPoint.Edge.Handle, cutPoint.Face.Handle, out var vA, out var vB );
		mesh.GetVertexPosition( vA, Transform.Zero, out var posA );
		mesh.GetVertexPosition( vB, Transform.Zero, out var posB );
		ClosestPointOnLine( cutPoint.BasePosition, posA, posB, out _, out var t );
		t = t.Clamp( 0.01f, 0.99f );

		_loopPreview = BuildLoopCutPoints( component, cutPoint.Face.Handle, cutPoint.Edge.Handle, t );
		if ( _loopPreview is null )
			return;

		DrawLoopPreview();
		DrawPreview();

		if ( !Gizmo.Pressed.Any && Gizmo.WasLeftMousePressed )
		{
			_cutPoints.AddRange( _loopPreview );

			if ( _loopClosed )
				_cutPoints.Add( _loopPreview[0] );

			Apply();

			_cutPoints.Clear();
			_loopPreview = null;
		}
	}

	List<MeshCutPoint> BuildLoopCutPoints( MeshComponent component, FaceHandle startFace, HalfEdgeHandle startEdge, float t )
	{
		var mesh = component.Mesh;
		var points = new List<MeshCutPoint>();
		var visitedEdges = new HashSet<HalfEdgeHandle>();

		points.Add( CreateEdgeCutPoint( component, startFace, startEdge, t ) );
		visitedEdges.Add( startEdge );

		var oppositeStart = mesh.GetOppositeHalfEdge( startEdge );
		if ( oppositeStart.IsValid )
			visitedEdges.Add( oppositeStart );

		var closed = WalkLoop( component, startFace, startEdge, t, visitedEdges, points, append: true );

		if ( !closed )
		{
			mesh.GetFacesConnectedToEdge( startEdge, out var faceA, out var faceB );
			var otherFace = faceA == startFace ? faceB : faceA;

			if ( otherFace.IsValid )
				closed = WalkLoop( component, otherFace, startEdge, 1f - t, visitedEdges, points, append: false );
		}
		_loopClosed = closed;

		return points.Count >= 2 ? points : null;
	}

	bool WalkLoop( MeshComponent component, FaceHandle face, HalfEdgeHandle edge, float t, HashSet<HalfEdgeHandle> visitedEdges, List<MeshCutPoint> points, bool append )
	{
		var mesh = component.Mesh;
		var currentFace = face;
		var currentEdge = edge;
		var currentT = t;

		while ( true )
		{
			mesh.GetEdgesConnectedToFace( currentFace, out var faceEdges );
			if ( faceEdges.Count != 4 )
				return false;

			var index = faceEdges.FindIndex( e => e == currentEdge || e == mesh.GetOppositeHalfEdge( currentEdge ) );
			if ( index < 0 )
				return false;

			var nextEdge = faceEdges[(index + 2) % 4];

			var nextT = 1f - currentT;

			var oppositeNextEdge = mesh.GetOppositeHalfEdge( nextEdge );
			if ( visitedEdges.Contains( nextEdge ) || (oppositeNextEdge.IsValid && visitedEdges.Contains( oppositeNextEdge )) )
				return true;

			var cut = CreateEdgeCutPoint( component, currentFace, nextEdge, nextT );

			if ( append ) points.Add( cut );
			else points.Insert( 0, cut );

			visitedEdges.Add( nextEdge );

			if ( oppositeNextEdge.IsValid )
				visitedEdges.Add( oppositeNextEdge );
			mesh.GetFacesConnectedToEdge( nextEdge, out var faceA, out var faceB );
			var next = faceA == currentFace ? faceB : faceA;
			if ( !next.IsValid )
				return false;

			currentFace = next;
			currentEdge = nextEdge;

			currentT = 1f - nextT;
		}
	}

	MeshCutPoint CreateEdgeCutPoint( MeshComponent component, FaceHandle face, HalfEdgeHandle edge, float t )
	{
		var mesh = component.Mesh;
		mesh.GetVerticesConnectedToEdge( edge, face, out var vA, out var vB );
		mesh.GetVertexPosition( vA, component.WorldTransform, out var posA );
		mesh.GetVertexPosition( vB, component.WorldTransform, out var posB );

		return new MeshCutPoint(
			new MeshFace( component, face ),
			new MeshEdge( component, edge ),
			posA.LerpTo( posB, t ) );
	}

	void DrawLoopPreview()
	{
		if ( _loopPreview is null || _loopPreview.Count < 2 )
			return;

		using ( Gizmo.Scope( "LoopPreview" ) )
		{
			Gizmo.Draw.IgnoreDepth = false;
			Gizmo.Draw.LineThickness = 3;
			Gizmo.Draw.Color = Color.Orange;

			for ( var i = 1; i < _loopPreview.Count; i++ )
				Gizmo.Draw.Line( _loopPreview[i - 1].WorldPosition, _loopPreview[i].WorldPosition );

			if ( _loopClosed )
				Gizmo.Draw.Line( _loopPreview[^1].WorldPosition, _loopPreview[0].WorldPosition );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = Color.Orange.WithAlpha( 0.35f );

			for ( var i = 1; i < _loopPreview.Count; i++ )
				Gizmo.Draw.Line( _loopPreview[i - 1].WorldPosition, _loopPreview[i].WorldPosition );

			if ( _loopClosed )
				Gizmo.Draw.Line( _loopPreview[^1].WorldPosition, _loopPreview[0].WorldPosition );
		}
	}
}
