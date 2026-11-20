namespace EngineTests;

/// <summary>
/// A custom object's draws on Borealis.Gpu lit from the light probe volume it's in, as native's
/// <c>CSceneSystem::SetupPerObjectLighting</c> binds it when the object asks (<see cref="Graphics.SetupLighting"/>).
/// </summary>
[TestClass]
public class BorealisGpuCustomProbeTest
{
	const int Size = 64;

	/// <summary>
	/// A box model and a custom object drawing the same model side by side inside a volume of red probes, with nothing else
	/// lighting them: the custom object that asks for its lighting is red as the model is, and isn't without asking.
	/// </summary>
	[TestMethod]
	public void CustomDrawsReadTheirVolumesProbes()
	{
		TestGraphics.Require();

		var model = Model.Load( "models/dev/box.vmdl" );
		var offset = model.Bounds.Size.y;
		var world = new SceneWorld();
		try
		{
			// Six layers of two texels, all red: what the probes say from every direction
			var red = new byte[2 * 2 * 12 * 4];
			for ( int i = 0; i < red.Length; i += 4 ) (red[i], red[i + 3]) = (255, 255);
			using var probes = Texture.CreateVolume( 2, 2, 12 ).WithData( red ).WithName( "red probes" ).Finish();
			_ = new SceneLightProbe( world, probes, null, null, new BBox( new Vector3( -400 ), new Vector3( 400 ) ), Transform.Zero, 0, 0 );

			// The model on the left, the custom object on the right
			_ = new SceneModel( world, model, new Transform( new Vector3( 0, offset, 0 ) ) );
			var lit = true;
			var at = new Transform( new Vector3( 0, -offset, 0 ) );
			var custom = new SceneCustomObject( world ) { Transform = at, Bounds = model.Bounds.Transform( at ) };
			custom.RenderOverride = obj =>
			{
				if ( lit ) Graphics.SetupLighting( obj );
				Graphics.DrawModel( model, at );
			};

			using var camera = new SceneCamera( "custom probe" ) { World = world, Position = new Vector3( -offset * 4, 0, 0 ), Rotation = Rotation.Identity, FieldOfView = 60, BackgroundColor = Color.Black };
			using var bitmap = new Bitmap( Size, Size );

			var (modelLit, customLit) = Halves( camera, bitmap );
			lit = false;
			var (modelAfter, customUnlit) = Halves( camera, bitmap );

			Assert.IsTrue( modelLit.x > 1 && modelLit.x > (modelLit.y + modelLit.z) * 4, $"the model is lit red by its probes ({modelLit})" );
			Assert.IsTrue( customLit.x > modelLit.x * 0.75f && customLit.x < modelLit.x * 1.25f, $"the custom object asking is lit as the model is ({customLit} against {modelLit})" );
			Assert.IsTrue( customLit.x > (customLit.y + customLit.z) * 4, $"red, as the model ({customLit})" );
			Assert.IsTrue( customUnlit.x < customLit.x * 0.5f, $"without asking it isn't ({customUnlit} against {customLit})" );
			Assert.IsTrue( System.Math.Abs( modelAfter.x - modelLit.x ) < modelLit.x * 0.05f, "the model the same both times" );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>The summed colour of each half of a frame of <paramref name="camera"/>'s, once its textures and pipelines are in.</summary>
	static (Vector3 Left, Vector3 Right) Halves( SceneCamera camera, Bitmap bitmap )
	{
		for ( int frame = 0; frame < 10; frame++ )
		{
			System.Threading.Thread.Sleep( 20 );
			camera.RenderToBitmap( bitmap );
		}

		var pixels = bitmap.GetPixels();
		Vector3 left = default, right = default;
		for ( int y = 0; y < Size; y++ )
		{
			for ( int x = 0; x < Size; x++ )
			{
				var p = pixels[y * Size + x];
				var c = new Vector3( p.r, p.g, p.b );
				if ( x < Size / 2 ) left += c;
				else right += c;
			}
		}

		return (left, right);
	}
}
