using HalfEdgeMesh;

namespace Sandbox;

public partial class PolygonMesh
{
	public struct InsetResult
	{
		public List<FaceHandle> InsetFaces;
		public List<FaceHandle> ConnectingFaces;
	}

	/// <summary>
	/// Inset a region of faces inward by the given amount with optional intermediate rings.
	/// </summary>
	public bool InsetFaces( FaceHandle[] faces, float amount, int steps, out InsetResult result )
	{
		steps = Math.Max( 0, steps );

		result = new InsetResult
		{
			InsetFaces = [],
			ConnectingFaces = []
		};

		if ( faces.Length == 0 ) return false;

		var ctx = BuildInsetBoundary( faces );
		if ( ctx.BoundaryEdges.Count == 0 ) return false;

		amount = ClampInsetAmount( ctx, amount );

		var faceInfos = GatherInsetFaceInfos( faces, ctx.FaceVerticesMap );
		var orphanedPositions = FindInsetOrphanedPositions( ctx );

		RemoveFaces( faces );

		var vertexReplacement = RecreateInsetOrphanedVertices( orphanedPositions );
		var rings = CreateInsetVertexRings( ctx, amount, steps );

		CreateInsetConnectingQuads( ctx, rings, result.ConnectingFaces );
		RecreateInsetCenterFaces( faceInfos, rings[^1], vertexReplacement, result.InsetFaces );

		var allNewVertices = CollectInsetVertices( rings, vertexReplacement );
		if ( allNewVertices.Count > 1 )
		{
			MergeVerticesWithinDistance( allNewVertices, 0.001f, bPreConnect: true, bAveragePositions: false, out _ );
		}

		ComputeFaceTextureCoordinatesFromParameters( [.. result.InsetFaces, .. result.ConnectingFaces] );

		return true;
	}

	List<VertexHandle> CollectInsetVertices( Dictionary<VertexHandle, VertexHandle>[] rings, Dictionary<VertexHandle, VertexHandle> vertexReplacement )
	{
		var vertices = new HashSet<VertexHandle>();

		foreach ( var ring in rings )
			foreach ( var v in ring.Values )
				vertices.Add( v );

		foreach ( var v in vertexReplacement.Values )
			vertices.Add( v );

		return vertices.ToList();
	}

	InsetBoundaryContext BuildInsetBoundary( FaceHandle[] faces )
	{
		var directedEdges = new HashSet<(int, int)>();
		var faceVerticesMap = new Dictionary<FaceHandle, VertexHandle[]>();

		foreach ( var face in faces )
		{
			var verts = GetFaceVertices( face );
			if ( verts is not { Length: >= 3 } ) continue;

			faceVerticesMap[face] = verts;

			for ( int i = 0; i < verts.Length; i++ )
				directedEdges.Add( (verts[i].Index, verts[(i + 1) % verts.Length].Index) );
		}

		var boundaryEdges = new List<InsetBoundaryEdge>();
		var boundaryVertices = new HashSet<VertexHandle>();
		var incoming = new Dictionary<VertexHandle, int>();
		var outgoing = new Dictionary<VertexHandle, int>();

		foreach ( var face in faces )
		{
			if ( !faceVerticesMap.TryGetValue( face, out var verts ) ) continue;

			ComputeFaceNormal( face, out var faceNormal );
			faceNormal = faceNormal.Normal;
			var faceCenter = GetFaceCenter( face );
			var material = GetFaceMaterial( face );
			GetFaceTextureParameters( face, out var axisU, out var axisV, out var texScale );

			for ( int i = 0; i < verts.Length; i++ )
			{
				var vertA = verts[i];
				var vertB = verts[(i + 1) % verts.Length];

				if ( directedEdges.Contains( (vertB.Index, vertA.Index) ) )
					continue;

				var posA = GetVertexPosition( vertA );
				var posB = GetVertexPosition( vertB );
				var edgeDir = (posB - posA).Normal;
				var perp = Vector3.Cross( faceNormal, edgeDir ).Normal;

				if ( Vector3.Dot( perp, faceCenter - (posA + posB) * 0.5f ) < 0 )
					perp = -perp;

				int idx = boundaryEdges.Count;
				boundaryEdges.Add( new InsetBoundaryEdge( vertA, vertB, perp, material, axisU, axisV, texScale ) );

				boundaryVertices.Add( vertA );
				boundaryVertices.Add( vertB );
				outgoing[vertA] = idx;
				incoming[vertB] = idx;
			}
		}

		var miterDir = new Dictionary<VertexHandle, Vector3>();
		var miterScale = new Dictionary<VertexHandle, float>();

		foreach ( var vertex in boundaryVertices )
		{
			if ( !incoming.TryGetValue( vertex, out var inIdx ) ) continue;
			if ( !outgoing.TryGetValue( vertex, out var outIdx ) ) continue;

			var bisector = boundaryEdges[inIdx].InwardPerp + boundaryEdges[outIdx].InwardPerp;
			bisector = bisector.LengthSquared < 0.0001f
				? boundaryEdges[outIdx].InwardPerp
				: bisector.Normal;

			var dot = Vector3.Dot( bisector, boundaryEdges[outIdx].InwardPerp );
			var scale = MathF.Min( dot > 0.1f ? 1.0f / dot : 1.0f, 4.0f );

			miterDir[vertex] = bisector;
			miterScale[vertex] = scale;
		}

		return new InsetBoundaryContext( faceVerticesMap, boundaryEdges, boundaryVertices, miterDir, miterScale );
	}

	float ClampInsetAmount( InsetBoundaryContext ctx, float amount )
	{
		float maxSafe = float.MaxValue;

		foreach ( var info in ctx.BoundaryEdges )
		{
			var edgeVec = GetVertexPosition( info.VertexB ) - GetVertexPosition( info.VertexA );
			var edgeLen = edgeVec.Length;
			if ( edgeLen < 0.001f ) continue;

			var edgeDir = edgeVec / edgeLen;
			var moveA = ctx.MiterDir[info.VertexA] * ctx.MiterScale[info.VertexA];
			var moveB = ctx.MiterDir[info.VertexB] * ctx.MiterScale[info.VertexB];

			var shrinkRate = Vector3.Dot( moveA, edgeDir ) - Vector3.Dot( moveB, edgeDir );
			if ( shrinkRate > 0.001f )
				maxSafe = MathF.Min( maxSafe, edgeLen / shrinkRate );
		}

		if ( maxSafe >= float.MaxValue ) return amount;

		return amount > 0 ? MathF.Min( amount, maxSafe ) : MathF.Max( amount, -maxSafe );
	}

	List<InsetFaceInfo> GatherInsetFaceInfos( FaceHandle[] faces, Dictionary<FaceHandle, VertexHandle[]> faceVerticesMap )
	{
		var infos = new List<InsetFaceInfo>( faces.Length );

		foreach ( var face in faces )
		{
			if ( !faceVerticesMap.TryGetValue( face, out var vertices ) ) continue;

			var material = GetFaceMaterial( face );
			GetFaceTextureParameters( face, out var axisU, out var axisV, out var texScale );
			infos.Add( new InsetFaceInfo( vertices, material, axisU, axisV, texScale ) );
		}

		return infos;
	}

	Dictionary<VertexHandle, Vector3> FindInsetOrphanedPositions( InsetBoundaryContext ctx )
	{
		var positions = new Dictionary<VertexHandle, Vector3>();

		foreach ( var verts in ctx.FaceVerticesMap.Values )
			foreach ( var v in verts )
				if ( !ctx.BoundaryVertices.Contains( v ) && !positions.ContainsKey( v ) )
					positions[v] = GetVertexPosition( v );

		return positions;
	}

	Dictionary<VertexHandle, VertexHandle> RecreateInsetOrphanedVertices( Dictionary<VertexHandle, Vector3> orphaned )
	{
		var replacements = new Dictionary<VertexHandle, VertexHandle>();

		foreach ( var (vertex, pos) in orphaned )
			replacements[vertex] = AddVertices( [pos] )[0];

		return replacements;
	}

	Dictionary<VertexHandle, VertexHandle>[] CreateInsetVertexRings( InsetBoundaryContext ctx, float amount, int steps )
	{
		int totalSteps = Math.Max( 1, steps + 1 );
		var rings = new Dictionary<VertexHandle, VertexHandle>[totalSteps];

		for ( int step = 0; step < totalSteps; step++ )
		{
			float t = amount * (step + 1) / totalSteps;
			rings[step] = [];

			foreach ( var vertex in ctx.BoundaryVertices )
			{
				if ( !ctx.MiterDir.TryGetValue( vertex, out var dir ) ) continue;

				var pos = GetVertexPosition( vertex );
				rings[step][vertex] = AddVertices( [pos + dir * (t * ctx.MiterScale[vertex])] )[0];
			}
		}

		return rings;
	}

	void CreateInsetConnectingQuads( InsetBoundaryContext ctx, Dictionary<VertexHandle, VertexHandle>[] rings, List<FaceHandle> outFaces )
	{
		for ( int step = 0; step < rings.Length; step++ )
		{
			foreach ( var info in ctx.BoundaryEdges )
			{
				var (outerA, outerB) = step == 0
					? (info.VertexA, info.VertexB)
					: (rings[step - 1][info.VertexA], rings[step - 1][info.VertexB]);

				var quadFace = AddFace( [outerA, outerB, rings[step][info.VertexB], rings[step][info.VertexA]] );
				if ( !quadFace.IsValid ) continue;

				SetFaceMaterial( quadFace, info.Material );
				SetFaceTextureParameters( quadFace, info.AxisU, info.AxisV, info.TexScale );
				outFaces.Add( quadFace );
			}
		}
	}

	void RecreateInsetCenterFaces( List<InsetFaceInfo> faceInfos, Dictionary<VertexHandle, VertexHandle> finalRing, Dictionary<VertexHandle, VertexHandle> vertexReplacement, List<FaceHandle> outFaces )
	{
		foreach ( var info in faceInfos )
		{
			var newVerts = info.Vertices.Select( v =>
				finalRing.TryGetValue( v, out var inset ) ? inset
				: vertexReplacement.TryGetValue( v, out var replacement ) ? replacement
				: v
			).ToArray();

			var newFace = AddFace( newVerts );
			if ( !newFace.IsValid ) continue;

			SetFaceMaterial( newFace, info.Material );
			SetFaceTextureParameters( newFace, info.AxisU, info.AxisV, info.TexScale );
			outFaces.Add( newFace );
		}
	}

	record struct InsetFaceInfo( VertexHandle[] Vertices, Material Material, Vector4 AxisU, Vector4 AxisV, Vector2 TexScale );
	record struct InsetBoundaryEdge( VertexHandle VertexA, VertexHandle VertexB, Vector3 InwardPerp, Material Material, Vector4 AxisU, Vector4 AxisV, Vector2 TexScale );
	record struct InsetBoundaryContext( Dictionary<FaceHandle, VertexHandle[]> FaceVerticesMap, List<InsetBoundaryEdge> BoundaryEdges, HashSet<VertexHandle> BoundaryVertices, Dictionary<VertexHandle, Vector3> MiterDir, Dictionary<VertexHandle, float> MiterScale );
}
