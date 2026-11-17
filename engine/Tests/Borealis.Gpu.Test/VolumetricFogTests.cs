using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Volumetric fog on Borealis.Gpu: the froxels lit and integrated by <c>cs_volumetric_fog</c> and read by what the frame draws.
/// </summary>
[TestClass]
[DoNotParallelize]
public class VolumetricFogTests : GpuTest
{
	const int Width = 128, Height = 64;

	/// <summary>
	/// A lit box seen through thin fog, as a game's fog volumes set it (strength 0.15 over the whole view, the camera's settings
	/// a camera component gives): the box keeps most of its colour, as it does without fog, and the fog doesn't black it out.
	/// </summary>
	[TestMethod]
	public void ThinFogLeavesTheSceneLit()
	{
		var clear = Render( fog: false );
		var fogged = Render( fog: true );
		TestContext.WriteLine( $"box without fog {clear}, with fog {fogged}" );
		Assert.IsTrue( clear > 0.05f, $"the box is lit without fog ({clear})" );
		Assert.IsFalse( float.IsNaN( fogged ), "the fogged box isn't NaN" );
		Assert.IsTrue( fogged > clear * 0.5f, $"thin fog keeps the box lit: {fogged} against {clear}" );
	}

	/// <summary>The box's brightness in the middle of a frame rendered after a few frames, with or without fog.</summary>
	float Render( bool fog )
	{
		var errors = new List<string>();
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			backend.Passes.Error = e => errors.Add( e );
			backend.Shaders.Unloaded = e => errors.Add( e );
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Fog test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Lighting.SunDirection = new Vector3( -1, 0, -1 ).Normal;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Fog box", Color.White ) ), new Transform( new Vector3( 300, 0, 0 ) ) ) );
			if ( fog )
			{
				world.FogVolumes.Add( new FogVolume { Bounds = new BBox( new Vector3( -4000 ), new Vector3( 4000 ) ), Strength = 0.15f, Exponent = 1 } );
				var settings = world.Lighting.VolumetricFog;
				settings.Enabled = true;
				settings.DrawDistance = 4096;
				settings.FadeInStart = 64;
				settings.FadeInEnd = 256;
				settings.IndirectStrength = 1;
				settings.Anisotropy = 1;
				settings.Scattering = 1;
			}

			var view = new RenderView { Name = "Fog test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			// The fog integrates over frames (its history), as a game's does
			for ( int frame = 0; frame < 4; frame++ )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
			}

			Assert.AreEqual( 0, errors.Count, string.Join( "; ", errors ) );
			var (handle, layout) = BackendDevice.Unwrap( target.Color );
			var texels = ReadBack( handle, layout );
			BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
			float Channel( int x, int y, int c ) => (float)BitConverter.UInt16BitsToHalf( BitConverter.ToUInt16( texels, (y * Width + x) * 8 + c * 2 ) );
			return (Channel( Width / 2, Height / 2, 0 ) + Channel( Width / 2, Height / 2, 1 ) + Channel( Width / 2, Height / 2, 2 )) / 3;
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
