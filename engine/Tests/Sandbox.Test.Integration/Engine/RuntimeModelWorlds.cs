using System.Collections.Generic;

namespace EngineTests;

/// <summary>
/// A run-time model (one built in code, read by the world renderer from its saved buffers) drawn in two worlds at once.
/// </summary>
[TestClass]
public class RuntimeModelWorldsTest
{
	/// <summary>
	/// Deleting one of the worlds leaves the model drawing in the other, frame after frame: the first world letting it go
	/// mustn't free the geometry the second still draws.
	/// </summary>
	[TestMethod]
	public void DeletingOneWorldKeepsTheOtherDrawing()
	{
		TestGraphics.Require();

		var mesh = new Mesh( Material.Load( "materials/default/white.vmat" ) );
		var vertices = new List<Vertex>
		{
			new( new Vector3( 0, -32, -32 ), Vector3.Backward, Vector3.Up, new Vector4( 0, 0, 0, 0 ) ),
			new( new Vector3( 0, 32, -32 ), Vector3.Backward, Vector3.Up, new Vector4( 1, 0, 0, 0 ) ),
			new( new Vector3( 0, 0, 32 ), Vector3.Backward, Vector3.Up, new Vector4( 0.5f, 1, 0, 0 ) ),
		};
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( 6, new List<int> { 0, 2, 1, 0, 1, 2 } );
		mesh.Bounds = new BBox( new Vector3( -1, -32, -32 ), new Vector3( 1, 32, 32 ) );
		var model = Model.Builder.AddMesh( mesh ).Create();
		Assert.IsTrue( model.IsProcedural );

		int Drawn( SceneWorld world )
		{
			using var camera = new SceneCamera( "runtime model" ) { World = world, Position = new Vector3( -100, 0, 0 ), Rotation = Rotation.Identity, FieldOfView = 60, BackgroundColor = Color.Black };
			camera.DebugMode = SceneCameraDebugMode.FullBright;
			using var bitmap = new Bitmap( 64, 64 );
			for ( int frame = 0; frame < 10; frame++ )
			{
				System.Threading.Thread.Sleep( 20 );
				camera.RenderToBitmap( bitmap );
			}

			return bitmap.GetPixels().Count( p => p.r > 0.2f );
		}

		var first = new SceneWorld();
		var second = new SceneWorld();
		try
		{
			_ = new SceneModel( first, model, Transform.Zero );
			_ = new SceneModel( second, model, Transform.Zero );
			Assert.IsTrue( Drawn( first ) > 100, "the model draws in the first world" );
			Assert.IsTrue( Drawn( second ) > 100, "and in the second" );

			first.Delete();
			first = null;
			for ( int frame = 0; frame < 5; frame++ ) EngineLoop.DrainFrameEndDisposables();

			Assert.IsTrue( Drawn( second ) > 100, "the second world still draws it once the first is gone" );
		}
		finally
		{
			first?.Delete();
			second.Delete();
		}
	}
}
