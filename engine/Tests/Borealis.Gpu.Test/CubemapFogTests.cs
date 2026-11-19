using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// Cubemap fog on Borealis.Gpu: surfaces past the fog's end take the colour of its cube (a sky's), read at a blurred mip.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CubemapFogTests : GpuTest
{
	const int Width = 128, Height = 64;

	/// <summary>
	/// A white box past the fog's end, under a light blue fog cube read at its blurriest mip (blur 1, as a game's
	/// <c>CubemapFog</c> sets it), as an HDR sky's half-float cube: the box turns the cube's blue.
	/// </summary>
	[TestMethod]
	public void FogTakesItsCubesColour()
	{
		var clear = Render( fog: false );
		var fogged = Render( fog: true );
		TestContext.WriteLine( $"box without fog {clear}, with fog {fogged}" );
		Assert.IsTrue( clear.x > 0.05f, $"the box is lit without fog ({clear})" );
		Assert.AreEqual( 0.25f, fogged.x, 0.05f, $"red is the cube's ({fogged})" );
		Assert.AreEqual( 0.5f, fogged.y, 0.05f, $"green is the cube's ({fogged})" );
		Assert.AreEqual( 1.0f, fogged.z, 0.05f, $"blue is the cube's ({fogged})" );
	}

	/// <summary>
	/// Fog that starts after the view has drawn a few frames without it (a game's sky cube loads after its first frames):
	/// the box takes the cube's colour from then on, as when the fog was there from the start.
	/// </summary>
	[TestMethod]
	public void FogTurnedOnLaterTakesItsCubesColour()
	{
		var fogged = Render( fog: true, fogFromFrame: 3 );
		TestContext.WriteLine( $"box with fog from frame 3 {fogged}" );
		Assert.AreEqual( 0.25f, fogged.x, 0.05f, $"red is the cube's ({fogged})" );
		Assert.AreEqual( 0.5f, fogged.y, 0.05f, $"green is the cube's ({fogged})" );
		Assert.AreEqual( 1.0f, fogged.z, 0.05f, $"blue is the cube's ({fogged})" );
	}

	/// <summary>The middle of the last of a few frames of a box 300 units ahead, with or without fully opaque cubemap fog (from <paramref name="fogFromFrame"/> on).</summary>
	Vector3 Render( bool fog, int fogFromFrame = 0 )
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
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Cubemap fog test" );
			using var cube = backend.CreateTexture( new TextureDesc { Width = 64, Height = 64, Depth = 1, Mips = 7, Samples = 1, Cube = true, Format = TextureFormat.RGBA16161616F }, "Cubemap fog test sky" );
			backend.Clear( cube, new Color( 0.25f, 0.5f, 1.0f, 0 ) );

			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Lighting.SunDirection = new Vector3( -1, 0, -1 ).Normal;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Cubemap fog box", Color.White ) ), new Transform( new Vector3( 300, 0, 0 ) ) ) );
			void SetFog()
			{
				var settings = world.Lighting.CubemapFog;
				settings.Enabled = true;
				settings.Texture = cube;
				settings.StartDistance = 10;
				settings.EndDistance = 100;
				settings.FalloffExponent = 1;
				settings.LodBias = 0;
				settings.HeightStart = 2000;
				settings.HeightExponent = 2;
				settings.Transform = new Transform( Vector3.Zero );
				settings.Tint = Color.White;
			}

			var view = new RenderView { Name = "Cubemap fog test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			for ( int frame = 0; frame <= fogFromFrame; frame++ )
			{
				if ( fog && frame == fogFromFrame ) SetFog();
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				Device.WaitIdle();
			}

			Assert.AreEqual( 0, errors.Count, string.Join( "; ", errors ) );
			var (handle, layout) = BackendDevice.Unwrap( target.Color );
			var texels = ReadBack( handle, layout );
			BackendDevice.SetLayout( target.Color, ImageLayout.CopySource );
			float Channel( int c ) => (float)BitConverter.UInt16BitsToHalf( BitConverter.ToUInt16( texels, (Height / 2 * Width + Width / 2) * 8 + c * 2 ) );
			return new Vector3( Channel( 0 ), Channel( 1 ), Channel( 2 ) );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
