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
		// One device for both pictures (each frame's fog starts from a render system of its own): a device each made every
		// pipeline of the frame twice
		using var backend = new BackendDevice( Device );
		var clear = Render( backend, fog: false );
		var fogged = Render( backend, fog: true );
		TestContext.WriteLine( $"box without fog {clear}, with fog {fogged}" );
		Assert.IsTrue( clear > 0.05f, $"the box is lit without fog ({clear})" );
		Assert.IsFalse( float.IsNaN( fogged ), "the fogged box isn't NaN" );
		Assert.IsTrue( fogged > clear * 0.5f, $"thin fog keeps the box lit: {fogged} against {clear}" );
	}

	/// <summary>
	/// A dense fog volume over only the right half of the view, in front of a wall that fills it: the wall dims behind the
	/// volume and stays as it is without fog on the left. The lighting pass lights only the froxels the volumes cover, from
	/// the first of them (VolumetricFog's froxel box): a pass that lit them from the view's corner left this volume unlit,
	/// with no fog at all.
	/// </summary>
	[TestMethod]
	public void FogFillsAVolumeAtTheSideOfTheView()
	{
		// The camera looks along +x; its right is -y. Past the fog's near plane (64), so the lit froxels start mid-view
		var right = new BBox( new Vector3( 100, -600, -300 ), new Vector3( 240, -10, 300 ) );
		using var backend = new BackendDevice( Device );
		var (clearLeft, clearRight) = RenderSides( backend, null );
		var (foggedLeft, foggedRight) = RenderSides( backend, right );
		TestContext.WriteLine( $"wall without fog {clearLeft} | {clearRight}, with the volume on the right {foggedLeft} | {foggedRight}" );
		Assert.IsTrue( clearLeft > 0.02f && clearRight > 0.02f, $"the wall is lit without fog ({clearLeft}, {clearRight})" );
		Assert.IsTrue( foggedRight < clearRight * 0.8f, $"the volume on the right dims the wall behind it: {foggedRight} against {clearRight}" );
		Assert.IsTrue( MathF.Abs( foggedLeft - clearLeft ) < clearLeft * 0.05f, $"the left, outside the volume, has no fog: {foggedLeft} against {clearLeft}" );
	}

	/// <summary>The wall's brightness a quarter of the way in from the left and from the right, with a fog volume or none.</summary>
	(float Left, float Right) RenderSides( BackendDevice backend, BBox? volume )
	{
		var texels = Render( backend, volume is not null, volume, scattering: 8, wall: true );
		return (Brightness( texels, Width / 4, Height / 2 ), Brightness( texels, Width * 3 / 4, Height / 2 ));
	}

	static float Brightness( byte[] texels, int x, int y )
	{
		float Channel( int c ) => (float)BitConverter.UInt16BitsToHalf( BitConverter.ToUInt16( texels, (y * Width + x) * 8 + c * 2 ) );
		return (Channel( 0 ) + Channel( 1 ) + Channel( 2 )) / 3;
	}

	/// <summary>The box's brightness in the middle of a frame rendered after a few frames, with or without fog.</summary>
	float Render( BackendDevice backend, bool fog ) => Brightness( Render( backend, fog, null, scattering: 1, wall: false ), Width / 2, Height / 2 );

	/// <summary>
	/// The frame's texels (RGBA16F) after a few frames: a box ahead, or a wall filling the view, with fog in
	/// <paramref name="volume"/> (by default everywhere) when <paramref name="fog"/> is set.
	/// </summary>
	byte[] Render( BackendDevice backend, bool fog, BBox? volume, float scattering, bool wall )
	{
		var errors = new List<string>();
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
			var box = wall ? new Transform( new Vector3( 300, 0, 0 ), Rotation.Identity, new Vector3( 1, 8, 4 ) ) : new Transform( new Vector3( 300, 0, 0 ) );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Fog box", Color.White ) ), box ) );
			if ( fog )
			{
				world.FogVolumes.Add( volume is { } bounds
					? new FogVolume { Bounds = bounds, Strength = 1, Exponent = 0.25f }
					: new FogVolume { Bounds = new BBox( new Vector3( -4000 ), new Vector3( 4000 ) ), Strength = 0.15f, Exponent = 1 } );
				var settings = world.Lighting.VolumetricFog;
				settings.Enabled = true;
				settings.DrawDistance = 4096;
				settings.FadeInStart = volume is null ? 64 : 1;
				settings.FadeInEnd = volume is null ? 256 : 2;
				settings.IndirectStrength = 1;
				settings.Anisotropy = 1;
				settings.Scattering = scattering;
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
			return texels;
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
