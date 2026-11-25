using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The 3D skybox on Borealis.Gpu: its world draws into the skybox's depth range and the 2D sky it supplies draws behind it,
/// at the far plane (the sky pass's [0, 0] depth range).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SkyboxTests : GpuTest
{
	const int Width = 320, Height = 200;

	/// <summary>
	/// A box in the 3D skybox, far enough that the 2D sky's box (half way to the far plane) would be nearer than it if the
	/// sky drew over the whole depth range, shows in front of the sky.
	/// </summary>
	[TestMethod]
	public void SkyboxWorldDrawsInFrontOfTheSky()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var sky = backend.LoadMaterial( "materials/skybox/skybox_day_01.vmat" );
			if ( sky is not BackendMaterial { Shader: not null } ) Assert.Inconclusive( "No compiled sky material" );

			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Skybox test" );
			var white = backend.CreateMaterial( "Skybox box", Color.White );

			var skyboxWorld = new RenderWorld();
			skyboxWorld.Lighting.SunShadowCascades = 0;
			skyboxWorld.Lighting.SkyMaterial = sky;
			var world = new RenderWorld { Skybox3D = new Skybox3D( skyboxWorld ) { Scale = 16 } };
			world.Lighting.SunShadowCascades = 0;
			world.Lighting.SkyMaterial = sky;

			// 400 units out in the skybox's space: the sky's box sits at (near + far) / 2 = 2002, which the skybox's depth
			// range ([0, 0.02]) brings nearer than anything past 40 units
			var view = new RenderView { Name = "Skybox test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			byte[] Frame()
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
				var (texture, layout) = BackendDevice.Unwrap( target.Color );
				var pixels = ReadBack( texture, layout );
				BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
				return pixels;
			}

			var skyAlone = Frame();
			skyboxWorld.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, white ), new Transform( new Vector3( 400, 0, 0 ) ) ) );
			var withBox = Frame();

			// The middle pixel, RGBA16F
			var middle = ((Height / 2) * Width + Width / 2) * 8;
			Assert.IsFalse( withBox.AsSpan( middle, 8 ).SequenceEqual( skyAlone.AsSpan( middle, 8 ) ), "the skybox's box shows in front of the sky" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
