using System;

namespace ResourceTests;

[TestClass]
public class MeshMorphTests
{
	[TestMethod]
	public void AddMorphRejectsTooManyTargets()
	{
		var mesh = new Mesh();
		var deltas = new[] { new MorphDelta( 0, Vector3.Up, Vector3.Zero ) };

		for ( var i = 0; i < 4096; i++ )
			mesh.AddMorph( $"morph{i}", deltas );

		Assert.ThrowsException<InvalidOperationException>( () => mesh.AddMorph( "one_too_many", deltas ) );

		mesh.AddMorph( "morph0", deltas );
		Assert.AreEqual( 4096, mesh.MorphTargets.Count );
	}

	[TestMethod]
	public void ModelBuilderSkipsMorphsThatExceedAtlas()
	{
		const int vertexCount = 2560;

		var mesh = new Mesh( Material.Load( "materials/default/white.vmat" ) );
		mesh.CreateVertexBuffer( vertexCount, new Vertex[vertexCount].AsSpan() );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( new Vector3( -16 ), new Vector3( 16 ) );

		var deltas = new[] { new MorphDelta( vertexCount - 1, Vector3.Up, Vector3.Zero ) };
		for ( var i = 0; i < 4096; i++ )
			mesh.AddMorph( $"morph{i}", deltas );

		var model = Model.Builder.AddMesh( mesh ).Create();
		Assert.IsNotNull( model );
		Assert.AreEqual( 0, model.MorphCount );
	}
}
