using System;

namespace SceneTests;

/// <summary>
/// A mesh object (the editor's tool render context) keeps its vertex and index buffers in managed code with no native object,
/// draws as a custom object, and takes new data, index ranges and bounds.
/// </summary>
[TestClass]
public class SceneMeshObjectTests
{
	static readonly Vector3[] Line = [new( 0, 0, 0 ), new( 10, 0, 0 ), new( 10, 10, 0 )];

	[TestMethod]
	public void MeshObjectsAreManaged()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/default/white.vmat" );
			var bounds = new BBox( Vector3.Zero, new Vector3( 10, 10, 0 ) );
			var so = SceneMeshObject.Create( world, material, Graphics.PrimitiveType.Lines, Line.AsSpan(), new[] { 0, 1, 1, 2 }.AsSpan(), bounds );
			Assert.IsTrue( so.IsValid() );
			Assert.IsTrue( so.native.IsNull, "no native object" );
			Assert.IsTrue( so.OwnsRenderObject, "drawn as a custom object" );
			Assert.AreEqual( 3, so.VertexCount );
			Assert.AreEqual( 4, so.IndexCount );
			Assert.AreEqual( bounds, so.WorldBounds );

			so.Transform = new Transform( new Vector3( 100, 0, 0 ) );
			Assert.AreEqual( bounds.Translate( new Vector3( 100, 0, 0 ) ), so.WorldBounds, "bounds follow the transform" );

			so.SetVertexBufferData( new Vector3[] { new( 1, 1, 1 ), new( 2, 2, 2 ), new( 3, 3, 3 ) }.AsSpan() );
			so.SetIndexBufferData( new[] { 2, 1, 1, 0 }.AsSpan() );
			so.SetIndexRange( 2, 2 );
			Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => so.SetIndexRange( 3, 2 ) );
			Assert.ThrowsException<System.ArgumentException>( () => so.SetVertexBufferData( new Vector3[2].AsSpan() ) );
			Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => so.SetIndexBufferData( new[] { 0, 1, 1, 5 }.AsSpan() ) );

			lock ( world.InternalSceneObjects ) Assert.IsTrue( world.InternalSceneObjects.Contains( so ) );
			so.Delete();
			Assert.IsFalse( so.IsValid() );
			Assert.ThrowsException<System.InvalidOperationException>( () => so.SetIndexRange( 0, 2 ) );
			world.DeletePendingObjects();
			lock ( world.InternalSceneObjects ) Assert.IsFalse( world.InternalSceneObjects.Contains( so ) );
			Assert.AreEqual( 0, so.VertexCount, "its buffers go with it" );
		}
		finally
		{
			world.Delete();
		}
	}
}
