using Borealis.Source2;
using Borealis.Source2.Models;

namespace BorealisTests;

/// <summary>
/// A file model's vertices and indices read from its compiled file, as <see cref="Model.GetVertices"/> and
/// <see cref="Model.GetIndices"/> return them under -borealisgpu (Source 2's empty render system has nothing to read back):
/// LOD 0's buffers in turn, decoded the way native reads its buffers back.
/// </summary>
[TestClass]
public class ModelFileGeometryTests
{
	[TestMethod]
	public void FileModelGeometryReadsAsNativeReadsItBack()
	{
		var content = ContentFiles.FindGame();
		if ( content.Find( "models/dev/box.vmdl" ) is null ) Assert.Inconclusive( "No compiled core content" );
		var file = ModelFile.Load( content, "models/dev/box.vmdl" );

		var vertices = ModelFileGeometry.Vertices( file );
		var indices = ModelFileGeometry.Indices( file );
		var lod0 = file.Meshes.Where( m => (m.LodMask & 1) != 0 ).ToArray();
		Assert.AreEqual( lod0.Sum( m => m.VertexBuffers.Sum( b => b.Count ) ), vertices.Length );
		Assert.AreEqual( lod0.Sum( m => m.IndexBuffers.Sum( b => b.Count ) ), indices.Length );
		Assert.IsTrue( indices.All( i => i < vertices.Length ) );

		// Every triangle's corners are the file's positions, inside its bounds, with unit normals
		var mins = file.Mins - new System.Numerics.Vector3( 0.01f );
		var maxs = file.Maxs + new System.Numerics.Vector3( 0.01f );
		foreach ( var v in vertices )
		{
			Assert.IsTrue( v.Position.x >= mins.X && v.Position.y >= mins.Y && v.Position.z >= mins.Z && v.Position.x <= maxs.X && v.Position.y <= maxs.Y && v.Position.z <= maxs.Z, $"{v.Position} outside {file.Mins} {file.Maxs}" );
			Assert.AreEqual( 1f, v.Normal.Length, 0.02f );
		}

		var first = lod0[0].VertexBuffers[0];
		var position = first.Find( "POSITION" ).Value;
		var expected = first.Read( position, 1 );
		Assert.AreEqual( expected.X, vertices[1].Position.x, 1e-5f );
		Assert.AreEqual( expected.Z, vertices[1].Position.z, 1e-5f );
		Assert.AreEqual( lod0[0].IndexBuffers[0][2], indices[2] );
	}
}
