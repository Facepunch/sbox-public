using HalfEdgeMesh;
using System.Collections.Generic;

namespace SceneTests.Components;

[TestClass]
public class PolygonMeshUvTests
{
	[TestMethod]
	[DataRow( 0, true )]
	[DataRow( 0, false )]
	[DataRow( 2, true )]
	[DataRow( 2, false )]
	public void GenerateUvsForBooleanCutFaces( int generationMode, bool perFace )
	{
		var mesh = CreateBox( new Vector3( -64 ), new Vector3( 64 ) );
		var cutter = CreateBox( new Vector3( -24, -96, -24 ), new Vector3( 24, 96, 24 ) );

		Assert.IsTrue( mesh.PerformBoolean( cutter, Transform.Zero, PolygonMesh.BooleanOperation.Subtract ) );

		var faces = mesh.FaceHandles.Where( face =>
		{
			mesh.GetEdgesConnectedToFace( face, out var edges );
			return edges.Any( edge => mesh.GetOppositeFaceConnectedToEdge( edge, face ) == face );
		} ).ToArray();

		Assert.AreEqual( 2, faces.Length, "The cut should produce two faces with bridge edges around the hole." );

		List<List<FaceHandle>> islands;
		if ( perFace )
			islands = faces.Select( face => new List<FaceHandle> { face } ).ToList();
		else
			mesh.SplitFacesIntoIslandsForUVMapping( faces, out islands );

		Assert.IsTrue( islands.Count > 0 );

		foreach ( var island in islands )
		{
			mesh.GenerateUVsForFaces( island.ToArray(), generationMode, 0, HalfEdgeHandle.Invalid, HalfEdgeHandle.Invalid, out var corners, out var uvs );

			Assert.IsTrue( corners.Count > 0 );
			Assert.AreEqual( corners.Count, uvs.Count );
			Assert.AreEqual( corners.Count, corners.Distinct().Count() );

			foreach ( var face in island )
			{
				mesh.GetFaceVerticesConnectedToFace( face, out var expectedCorners );
				foreach ( var corner in expectedCorners )
					CollectionAssert.Contains( corners, corner );
			}

			foreach ( var uv in uvs )
				Assert.IsTrue( float.IsFinite( uv.x ) && float.IsFinite( uv.y ), "Generated UVs must be finite." );
		}
	}

	static PolygonMesh CreateBox( Vector3 min, Vector3 max )
	{
		var mesh = new PolygonMesh();
		var vertices = new[]
		{
			mesh.AddVertex( new Vector3( min.x, min.y, min.z ) ),
			mesh.AddVertex( new Vector3( max.x, min.y, min.z ) ),
			mesh.AddVertex( new Vector3( max.x, max.y, min.z ) ),
			mesh.AddVertex( new Vector3( min.x, max.y, min.z ) ),
			mesh.AddVertex( new Vector3( min.x, min.y, max.z ) ),
			mesh.AddVertex( new Vector3( max.x, min.y, max.z ) ),
			mesh.AddVertex( new Vector3( max.x, max.y, max.z ) ),
			mesh.AddVertex( new Vector3( min.x, max.y, max.z ) )
		};

		mesh.AddFace( vertices[0], vertices[3], vertices[2], vertices[1] );
		mesh.AddFace( vertices[4], vertices[5], vertices[6], vertices[7] );
		mesh.AddFace( vertices[0], vertices[1], vertices[5], vertices[4] );
		mesh.AddFace( vertices[1], vertices[2], vertices[6], vertices[5] );
		mesh.AddFace( vertices[2], vertices[3], vertices[7], vertices[6] );
		mesh.AddFace( vertices[3], vertices[0], vertices[4], vertices[7] );
		return mesh;
	}
}
