using System;
using System.Linq;
using Borealis.Source2.Models;

namespace ResourceTests;

/// <summary>
/// A mesh made in code keeps its vertices and indices itself: a model built from it reads its draws back, returns its
/// vertices and saves without Source 2's render device holding the buffers (the game's is the empty one).
/// </summary>
[TestClass]
public class ProceduralMeshBufferTests
{
	static Vertex At( float x, float y, byte shade ) => new( new Vector3( x, y, 0 ), new Color32( shade, shade, shade, 255 ) ) { Normal = Vector3.Up, Tangent = new Vector4( 1, 0, 0, 1 ) };

	static Vertex[] Strip( int count ) => Enumerable.Range( 0, count ).Select( i => At( i, i * 2, (byte)(i * 10) ) ).ToArray();

	static Material White => Material.Load( "materials/default/white.vmat" );

	static Mesh MeshOf( Vertex[] vertices, int[] indices )
	{
		var mesh = new Mesh( White );
		mesh.CreateVertexBuffer( vertices.Length, vertices.AsSpan() );
		mesh.CreateIndexBuffer( indices.Length, indices.AsSpan() );
		mesh.Bounds = new BBox( new Vector3( -100 ), new Vector3( 100 ) );
		return mesh;
	}

	[TestMethod]
	public void DrawCallReadsBackItsUsedVerticesCompacted()
	{
		var vertices = Strip( 6 );
		var model = Model.Builder.AddMesh( MeshOf( vertices, [0, 1, 2, 2, 1, 5] ) ).Create();

		Assert.IsTrue( model.TryReadDrawCall( 0, 0, out var read, out var indices, out var streams ) );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 2, 1, 3 }, indices );
		Assert.AreEqual( 4, read.Length );
		var used = new[] { 0, 1, 2, 5 };
		for ( int i = 0; i < used.Length; i++ )
		{
			Assert.AreEqual( vertices[used[i]].Position, read[i].Position );
			Assert.AreEqual( vertices[used[i]].Color, read[i].Color );
			Assert.AreEqual( vertices[used[i]].Normal, read[i].Normal );
		}

		Assert.IsTrue( streams.HasFlag( VertexStreams.Position ) && streams.HasFlag( VertexStreams.Color ) && streams.HasFlag( VertexStreams.Normal ) && streams.HasFlag( VertexStreams.Tangent ) );
	}

	[TestMethod]
	public void DrawCallOutsideItsVerticesDoesNotReadBack()
	{
		var mesh = MeshOf( Strip( 3 ), [0, 1, 2] );
		mesh.SetVertexRange( 0, 2 );
		var model = Model.Builder.AddMesh( mesh ).Create();

		Assert.IsFalse( model.TryReadDrawCall( 0, 0, out _, out _, out _ ) );
		Assert.IsFalse( model.TryReadDrawCall( 0, 1, out _, out _, out _ ) );
		Assert.IsFalse( model.TryReadDrawCall( 1, 0, out _, out _, out _ ) );
	}

	[TestMethod]
	public void SubMeshesKeepTheirRangesInsideTheBuffers()
	{
		var mesh = MeshOf( Strip( 6 ), [0, 1, 2, 3, 4, 5] );
		mesh.AddSubMesh( White, 0, 3 );
		mesh.AddSubMesh( White, 3, 99, 0, 0 );
		var model = Model.Builder.AddMesh( mesh ).Create();

		// The mesh's own draw (it has a material) and the two added
		var draws = model.MeshInfo.Meshes[0].DrawCalls;
		Assert.AreEqual( 3, draws.Length );
		Assert.AreEqual( 6, draws[0].Indices );
		Assert.AreEqual( 3, draws[1].Indices );
		Assert.AreEqual( 3, draws[2].StartIndex );
		Assert.AreEqual( 3, draws[2].Indices );
		Assert.AreEqual( 6, draws[2].Vertices );

		Assert.IsTrue( model.TryReadDrawCall( 0, 2, out var read, out var indices, out _ ) );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, indices );
		Assert.AreEqual( new Vector3( 3, 6, 0 ), read[0].Position );
	}

	[TestMethod]
	public void LockedBuffersAreWhatTheModelReturns()
	{
		var mesh = new Mesh( White );
		mesh.CreateVertexBuffer<Vertex>( 4 );
		mesh.SetIndexBufferSize( 6 );
		mesh.LockVertexBuffer<Vertex>( v =>
		{
			for ( int i = 0; i < v.Length; i++ ) v[i] = At( i, -i, 200 );
		} );
		mesh.LockIndexBuffer( i => new[] { 0, 1, 2, 0, 2, 3 }.CopyTo( i ) );
		mesh.SetVertexBufferData( new[] { At( 7, 7, 1 ) }.AsSpan(), 3 );

		var second = MeshOf( Strip( 3 ), [2, 1, 0] );
		var model = Model.Builder.AddMesh( mesh ).AddMesh( second ).Create();

		var vertices = model.GetVertices();
		Assert.AreEqual( 7, vertices.Length );
		Assert.AreEqual( new Vector3( 2, -2, 0 ), vertices[2].Position );
		Assert.AreEqual( new Vector3( 7, 7, 0 ), vertices[3].Position );
		Assert.AreEqual( new Vector3( 1, 2, 0 ), vertices[5].Position );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 0, 2, 3, 6, 5, 4 }, model.GetIndices() );
	}

	[TestMethod]
	public void LockedRegionStartsFromZero()
	{
		var mesh = MeshOf( Strip( 4 ), [0, 1, 2] );
		mesh.LockVertexBuffer<Vertex>( 1, 2, v => v[1].Position = new Vector3( 9, 9, 9 ) );
		var vertices = Model.Builder.AddMesh( mesh ).Create().GetVertices();

		Assert.AreEqual( new Vector3( 0, 0, 0 ), vertices[0].Position );
		Assert.AreEqual( default, vertices[1].Position );
		Assert.AreEqual( new Vector3( 9, 9, 9 ), vertices[2].Position );
		Assert.AreEqual( new Vector3( 3, 6, 0 ), vertices[3].Position );
	}

	[TestMethod]
	public void ResizedBuffersStartAgain()
	{
		var mesh = MeshOf( Strip( 3 ), [0, 1, 2] );
		mesh.SetVertexBufferSize( 5 );
		mesh.SetIndexBufferSize( 9 );

		Assert.AreEqual( 5, mesh.VertexCount );
		Assert.AreEqual( 9, mesh.IndexCount );
		var model = Model.Builder.AddMesh( mesh ).Create();
		Assert.IsTrue( model.GetVertices().All( v => v.Position == default ) );
		Assert.IsTrue( model.GetIndices().All( i => i == 0 ) );
	}

	[TestMethod]
	public void SavedModelCarriesTheMeshBuffers()
	{
		var vertices = Strip( 5 );
		int[] indices = [0, 1, 2, 2, 3, 4];
		var model = Model.Builder.AddMesh( MeshOf( vertices, indices ) ).Create();

		var file = ModelFile.Read( model.SaveToVmdl() );
		Assert.AreEqual( 1, file.Meshes.Count );
		var mesh = file.Meshes[0];
		var vb = mesh.VertexBuffers.Single();
		Assert.AreEqual( 5, vb.Count );
		Assert.AreEqual( System.Runtime.CompilerServices.Unsafe.SizeOf<Vertex>(), vb.Stride );
		CollectionAssert.AreEqual( System.Runtime.InteropServices.MemoryMarshal.AsBytes( vertices.AsSpan() ).ToArray(), vb.Data );
		CollectionAssert.AreEqual( new[] { "position", "color", "normal", "texcoord", "texcoord", "tangent" }, vb.Fields.Select( f => f.Semantic ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 0, 1, 0 }, vb.Fields.Select( f => f.SemanticIndex ).ToArray() );

		var ib = mesh.IndexBuffers.Single();
		CollectionAssert.AreEqual( indices.Select( i => (uint)i ).ToArray(), Enumerable.Range( 0, ib.Count ).Select( i => ib[i] ).ToArray() );

		var draw = mesh.DrawCalls.Single();
		CollectionAssert.AreEqual( new[] { (0, 0) }, draw.VertexBuffers.ToArray() );
		Assert.AreEqual( 6, draw.IndexCount );
		Assert.AreEqual( 5, draw.VertexCount );
	}

	[TestMethod]
	public void SavedModelWithoutIndicesHasNoIndexBuffer()
	{
		var mesh = new Mesh( White );
		mesh.CreateVertexBuffer( 3, Strip( 3 ).AsSpan() );
		var file = ModelFile.Read( Model.Builder.AddMesh( mesh ).Create().SaveToVmdl() );

		Assert.AreEqual( 0, file.Meshes[0].IndexBuffers.Count );
		Assert.AreEqual( 3, file.Meshes[0].VertexBuffers[0].Count );
	}

#pragma warning disable CS0618 // the layout-array overloads are obsolete but still public
	[TestMethod]
	public void AttributeLayoutsKeepTheirFields()
	{
		var mesh = new Mesh( White );
		var data = new[] { new SimpleVertex( new Vector3( 1, 2, 3 ), Vector3.Up, Vector3.Forward, new Vector2( 0.25f, 0.75f ) ), default, default };
		mesh.CreateVertexBuffer( 3, SimpleVertex.Layout, data.AsSpan() );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );

		var model = Model.Builder.AddMesh( mesh ).Create();
		Assert.IsTrue( model.TryReadDrawCall( 0, 0, out var read, out _, out var streams ) );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), read[0].Position );
		Assert.AreEqual( Vector3.Up, read[0].Normal );
		Assert.AreEqual( new Vector4( 0.25f, 0.75f, 0, 0 ), read[0].TexCoord0 );
		Assert.IsTrue( streams.HasFlag( VertexStreams.TexCoord0 ) );
	}
#pragma warning restore CS0618
}
