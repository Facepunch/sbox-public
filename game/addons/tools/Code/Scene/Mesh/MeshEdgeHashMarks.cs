namespace Editor.MeshEditor;

readonly record struct MeshEdgeHashMarks( Vector3 Start, Vector3 End, Vector3 Normal )
{
	public static bool TryCreate( MeshEdge edge, out MeshEdgeHashMarks marks )
	{
		marks = default;
		if ( !edge.IsValid() )
			return false;

		var mesh = edge.Component.Mesh;
		var halfEdge = edge.Handle;
		var opposite = mesh.GetOppositeHalfEdge( halfEdge );
		var face = mesh.GetHalfEdgeFace( halfEdge );
		var oppositeFace = mesh.GetHalfEdgeFace( opposite );
		if ( face.IsValid == oppositeFace.IsValid )
			return false;
		if ( !face.IsValid )
		{
			halfEdge = opposite;
			face = oppositeFace;
		}

		var line = mesh.GetEdgeLine( halfEdge );
		var transform = edge.Transform;
		var a = transform.PointToWorld( line.Start );
		var b = transform.PointToWorld( line.End );
		if ( a == b )
			return false;

		mesh.ComputeFaceNormal( face, out var normal );
		var scale = transform.Scale;
		normal *= new Vector3( scale.y * scale.z, scale.x * scale.z, scale.x * scale.y );
		marks = new MeshEdgeHashMarks( a, b, transform.NormalToWorld( normal ) );
		return true;
	}

	public void AddPositions( in ToolRenderView view, List<Vector2> positions )
	{
		var a = Start;
		var b = End;
		if ( !view.Orthographic && !view.ClipLineToFrustum( ref a, ref b ) )
			return;
		if ( !view.ToScreen( a, out var screenA ) || !view.ToScreen( b, out var screenB )
			|| !view.ClipLineToViewport( ref screenA, ref screenB ) )
			return;

		var length = screenA.Distance( screenB );
		if ( length < 1 )
			return;

		var center = (a + b) * 0.5f;
		var intoFace = Normal.Cross( (b - a).Normal );
		if ( !view.ToScreen( center, out var screenCenter ) || !view.ToScreen( center + intoFace, out var screenDirection ) )
			return;
		var direction = (screenDirection - screenCenter).Normal;
		if ( direction == Vector2.Zero )
			return;

		var count = Math.Max( 4, (int)(length / 6) );
		for ( var i = 0; i < count; ++i )
		{
			var position = Vector2.Lerp( screenA, screenB, (float)i / (count - 1) );
			positions.Add( position );
			positions.Add( position + direction * 6 );
		}
	}
}
