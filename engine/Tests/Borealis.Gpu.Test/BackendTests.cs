using Borealis;
using Sandbox;
using Borealis.Gpu;
using BorealisGpuSample;

namespace BorealisGpuTests;

/// <summary>
/// Borealis's own renderer, unmodified, rendering through Borealis.Gpu (<c>Borealis.Backend</c>), offscreen.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BackendTests : GpuTest
{
	/// <summary>
	/// The sample's world through a whole Borealis frame: the sky is the view's clear colour, the ground and objects are lit
	/// by the sun, something stands in shadow, and the depth prepass, shadow maps and forward pass all drew.
	/// </summary>
	[TestMethod]
	public void RenderSystemDrawsTheWorld()
	{
		const int width = 640, height = 400;
		using var scene = new BorealisScene( Device );

		Device.BeginFrame();
		var (_, _, color) = scene.Render( width, height, 1.0f );
		var stats = scene.Stats;
		Device.WaitIdle();

		var pixels = ToRgba8( ReadBack( Borealis.Backend.BackendDevice.Unwrap( color ).Texture, Borealis.Backend.BackendDevice.Unwrap( color ).Layout ), width, height );
		Save( pixels, width, height, "borealis-scene" );

		Assert.IsTrue( stats.Draws > 0, "forward drew" );
		Assert.IsTrue( stats.DepthDraws > 0, "the prepass drew" );
		Assert.IsTrue( stats.ShadowDraws > 0, "shadow maps drew" );

		// Sky at the top, ground below it
		var sky = Pixel( pixels, width, width / 2, 2 );
		var ground = Pixel( pixels, width, width / 2, height - 3 );
		Assert.IsTrue( sky.B > sky.R, $"sky is the clear colour: {sky}" );
		Assert.AreNotEqual( sky, ground, "the ground is drawn" );

		// Lit and shadowed ground: the brightest and darkest ground-coloured pixels differ a lot
		int brightest = 0, darkest = 255;
		for ( int y = height / 2; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				var p = Pixel( pixels, width, x, y );
				if ( Math.Abs( p.R - p.G ) > 12 || Math.Abs( p.G - p.B ) > 24 ) continue;
				brightest = Math.Max( brightest, p.G );
				darkest = Math.Min( darkest, p.G );
			}
		}

		Assert.IsTrue( brightest - darkest > 60, $"sunlit and shadowed ground: {darkest}..{brightest}" );
	}

	/// <summary>
	/// Frames keep coming without growing memory once warm: Borealis's upload rings, the backend's constants and the
	/// device's frame ring all recycle.
	/// </summary>
	[TestMethod]
	public void FramesSettle()
	{
		using var scene = new BorealisScene( Device );
		long reserved = 0;

		for ( int frame = 0; frame < 40; frame++ )
		{
			Device.BeginFrame();
			scene.Render( 320, 200, frame / 30f );
			if ( frame == 20 ) reserved = Device.MemoryUsage.Reserved;
		}

		Device.WaitIdle();
		Assert.AreEqual( reserved, Device.MemoryUsage.Reserved );
	}

	/// <summary>
	/// s&amp;box's compiled models (<c>.vmdl_c</c>) read and drawn by Borealis through Borealis.Gpu: each, alone in front of
	/// the camera and lit by the sun, covers the middle of the frame with something that isn't the clear colour, every
	/// one of its LOD 0 draws is drawn, and validation stays clean. The citizen is skinned and draws in its bind pose.
	/// </summary>
	[TestMethod]
	[DataRow( "models/dev/box.vmdl" )]
	[DataRow( "models/dev/gordon_at_desk.vmdl" )]
	[DataRow( "models/citizen_props/trashcan01.vmdl" )]
	[DataRow( "models/citizen/citizen.vmdl" )]
	public void CompiledModelDraws( string path )
	{
		const int width = 256, height = 256;
		var backend = new Borealis.Backend.BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new Borealis.Backend.BackendRenderHost( backend );
		var system = new RenderSystem { ParallelRecording = false };
		ViewTarget target = null;

		try
		{
			var mesh = backend.LoadModel( path );
			if ( mesh is null ) Assert.Inconclusive( $"{path} isn't built" );

			var clear = new Color( 0.1f, 0.2f, 0.9f );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -0.4f, 0.6f, -0.7f ).Normal;
			world.Lighting.AmbientColor = new Color( 0.2f, 0.2f, 0.2f, 1 );
			world.Add( new MeshObject( mesh, Transform.Zero ) );

			var view = new RenderView { Name = "Model", FieldOfView = 60, ClearColor = clear, Viewport = new Rect( 0, 0, width, height ) };
			var size = mesh.Bounds.Size.Length;
			view.Rotation = Rotation.From( 20, 160, 0 );
			view.Position = mesh.Bounds.Center - view.Rotation.Forward * size * 1.2f;
			view.ZNear = size * 0.05f;
			view.ZFar = size * 10;

			target = ViewTarget.Create( new Vector2Int( width, height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Model" );
			Device.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Device.WaitIdle();

			var (texture, layout) = Borealis.Backend.BackendDevice.Unwrap( target.Color );
			var pixels = ToRgba8( ReadBack( texture, layout ), width, height );
			Save( pixels, width, height, $"model-{System.IO.Path.GetFileNameWithoutExtension( path )}" );

			Assert.AreEqual( mesh.Draws.Count, system.Stats.Draws, "every LOD 0 draw drew" );
			var background = Pixel( pixels, width, 0, 0 );
			var covered = 0;
			for ( int y = height / 4; y < height * 3 / 4; y++ )
			{
				for ( int x = width / 4; x < width * 3 / 4; x++ )
				{
					if ( Pixel( pixels, width, x, y ) != background ) covered++;
				}
			}

			Assert.IsTrue( covered > width * height / 32, $"the model covers {covered} pixels of the middle quarter" );
		}
		finally
		{
			Device.WaitIdle();
			target?.Dispose();
			system.Dispose();
			backend.Dispose();
			RenderHost.Current = previousHost;
		}
	}

	/// <summary>Half-float RGBA to 8-bit sRGB, for looking at.</summary>
	static byte[] ToRgba8( byte[] halfs, int width, int height )
	{
		var pixels = new byte[width * height * 4];
		for ( int i = 0; i < width * height * 4; i++ )
		{
			var linear = (float)BitConverter.ToHalf( halfs, i * 2 );
			var encoded = (i & 3) == 3 ? linear : linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow( linear, 1 / 2.4f ) - 0.055f;
			pixels[i] = (byte)Math.Clamp( encoded * 255 + 0.5f, 0, 255 );
		}

		return pixels;
	}
}
