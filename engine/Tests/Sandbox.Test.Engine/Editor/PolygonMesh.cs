using Editor.MeshEditor;
using PolygonMesh = Editor.MeshEditor.PrimitiveBuilder.PolygonMesh;

namespace EditorTests;

[TestClass]
public class PolygonMeshTest
{
	/// <summary>
	/// New vertices should append to the list and return their index.
	/// </summary>
	[TestMethod]
	public void AddVertexAppendsAndReturnsIndex()
	{
		var mesh = new PolygonMesh();

		Assert.AreEqual( 0, mesh.AddVertex( new Vector3( 0, 0, 0 ) ) );
		Assert.AreEqual( 1, mesh.AddVertex( new Vector3( 10, 0, 0 ) ) );
		Assert.AreEqual( 2, mesh.Vertices.Count );
	}

	/// <summary>
	/// Adding a position that's already in the mesh - exactly or within tolerance -
	/// should return the existing index instead of creating a duplicate vertex.
	/// </summary>
	[TestMethod]
	public void AddVertexDeduplicatesNearbyPositions()
	{
		var mesh = new PolygonMesh();

		var index = mesh.AddVertex( new Vector3( 10, 20, 30 ) );

		Assert.AreEqual( index, mesh.AddVertex( new Vector3( 10, 20, 30 ) ) );
		Assert.AreEqual( index, mesh.AddVertex( new Vector3( 10.00005f, 20, 30 ) ) );
		Assert.AreEqual( 1, mesh.Vertices.Count );

		Assert.AreNotEqual( index, mesh.AddVertex( new Vector3( 10.01f, 20, 30 ) ) );
		Assert.AreEqual( 2, mesh.Vertices.Count );
	}

	/// <summary>
	/// A face needs at least three indices - anything less is rejected, anything valid
	/// keeps the winding order it was given.
	/// </summary>
	[TestMethod]
	public void AddFaceByIndexRequiresThreeIndices()
	{
		var mesh = new PolygonMesh();
		mesh.AddVertex( new Vector3( 0, 0, 0 ) );
		mesh.AddVertex( new Vector3( 10, 0, 0 ) );
		mesh.AddVertex( new Vector3( 10, 10, 0 ) );

		Assert.IsNull( mesh.AddFace( 0, 1 ) );
		Assert.AreEqual( 0, mesh.Faces.Count );

		var face = mesh.AddFace( 2, 0, 1 );

		Assert.IsNotNull( face );
		Assert.AreEqual( 1, mesh.Faces.Count );
		CollectionAssert.AreEqual( new[] { 2, 0, 1 }, face.Indices.ToArray() );
	}

	/// <summary>
	/// Building faces from positions should reuse vertices shared between faces - two
	/// triangles sharing an edge make four vertices, not six.
	/// </summary>
	[TestMethod]
	public void AddFaceByPositionSharesVertices()
	{
		var mesh = new PolygonMesh();

		var a = new Vector3( 0, 0, 0 );
		var b = new Vector3( 10, 0, 0 );
		var c = new Vector3( 10, 10, 0 );
		var d = new Vector3( 0, 10, 0 );

		var first = mesh.AddFace( a, b, c );
		var second = mesh.AddFace( a, c, d );

		Assert.AreEqual( 4, mesh.Vertices.Count );
		Assert.AreEqual( 2, mesh.Faces.Count );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, first.Indices.ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 2, 3 }, second.Indices.ToArray() );
	}

	/// <summary>
	/// Rejecting a degenerate position-based face shouldn't leave its vertices behind.
	/// </summary>
	[TestMethod]
	public void AddFaceByPositionRequiresThreePositions()
	{
		var mesh = new PolygonMesh();

		Assert.IsNull( mesh.AddFace( new Vector3( 0, 0, 0 ), new Vector3( 10, 0, 0 ) ) );
		Assert.AreEqual( 0, mesh.Faces.Count );
		Assert.AreEqual( 0, mesh.Vertices.Count );
	}
}
