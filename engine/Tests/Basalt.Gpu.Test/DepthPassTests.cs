using Basalt;
using Basalt.Backend;
using Basalt.Gpu;
using Sandbox;

namespace BasaltGpuTests;

/// <summary>
/// Depth passes on Basalt.Gpu keep the pixel shader of an alpha-tested material: its depth is what it clips (alpha test,
/// alpha to coverage), so a prepass without it would hide what's behind the cut-out parts.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DepthPassTests : GpuTest
{
	const int Width = 320, Height = 200;

	/// <summary>
	/// An alpha-tested box in front of a white one, multisampled, draws the same frame with the depth prepass as without it.
	/// </summary>
	[TestMethod]
	public void AlphaTestedDrawsMatchWithAndWithoutThePrepass()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var cutOut = backend.LoadMaterial( "materials/dev/simple/simple_rail_32.vmat" );
			if ( cutOut is not BackendMaterial { Shader: not null, IsAlphaTest: true } ) Assert.Inconclusive( "No compiled alpha-tested material" );

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Depth pass test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;

			// Flagged alpha-tested as a compiled model's draws are (BackendModel), so Basalt doesn't strip its pixel shader
			var box = ObjectAttributeTests.BoxMesh( backend, cutOut );
			var draw = box.DrawsForLod( 0 )[0] with { AlphaTest = true };
			world.Add( new MeshObject( new RenderMesh( box.Geometry, box.Bounds, [[draw]] ), Transform.Zero ) );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Behind", Color.White ) ), new Transform( new Vector3( 300, 0, 0 ), Rotation.Identity, 3 ) ) );

			var view = new RenderView { Name = "Depth pass test", FieldOfView = 60, ZNear = 4, ZFar = 4000, ClearColor = Color.Black, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Position = new Vector3( -200, 0, 0 );
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			byte[] Frame( bool prepass )
			{
				system.DepthPrepass = prepass;
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Resolved );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Resolved, ImageLayout.CopySource );
				return pixels;
			}

			var without = Frame( false );
			var with = Frame( true );

			// Pixels (RGBA16F) that differ by more than a little in red
			var different = 0;
			for ( int i = 0; i + 8 <= with.Length; i += 8 )
			{
				if ( MathF.Abs( (float)BitConverter.ToHalf( with, i ) - (float)BitConverter.ToHalf( without, i ) ) > 0.02f ) different++;
			}

			Assert.IsTrue( different < Width * Height / 200, $"{different} pixels differ with the prepass" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
