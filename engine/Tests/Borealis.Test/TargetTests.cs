using Borealis;
using Borealis.Gpu;
using BorealisTests.Headless;
using System.Collections.Generic;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Rendering into a target: which formats write sRGB, and the per-view constants a viewport within a bigger,
/// multisampled target gives the shaders.
/// </summary>
[TestClass]
public class TargetTests
{
	[TestMethod]
	public void OnlyFixedPointTargetsWriteSrgb()
	{
		// Float targets hold the linear HDR the shaders write; 8-bit ones are encoded like a swap chain
		Assert.IsTrue( TextureFormat.RGBA16161616F.IsFloat() );
		Assert.IsTrue( TextureFormat.RGBA32323232F.IsFloat() );
		Assert.IsTrue( TextureFormat.R16F.IsFloat() );
		Assert.IsFalse( TextureFormat.RGBA8888.IsFloat() );
		Assert.IsFalse( TextureFormat.RGBA1010102.IsFloat() );
		Assert.IsFalse( TextureFormat.RGBA16161616.IsFloat() );
	}

	[TestMethod]
	public void ConstantsDescribeTheViewportWithinItsTarget()
	{
		var view = LookingDownX();
		view.Viewport = new Rect( 100, 50, 800, 600 );
		view.Update();

		var c = new ViewConstants();
		view.FillConstants( ref c, new Vector2( 1600, 1200 ), 0, samples: 4, minZ: RenderSystem.MainWorldMinZ );

		Assert.AreEqual( 4, c.MsaaSampleCount );
		Assert.AreEqual( 0.02f, c.ViewportMinZ );
		Assert.AreEqual( 1.0f, c.ViewportMaxZ );
		Assert.AreEqual( new Vector2( 100, 50 ), c.ViewportOffset );
		Assert.AreEqual( new Vector2( 800, 600 ), c.ViewportSize );
		Assert.AreEqual( new Vector2( 1600, 1200 ), c.RenderTargetSize );
		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), c.ViewportToGBufferRatio );
		Assert.AreEqual( 1.0f / 800, c.InvViewportSize.x, 1e-9f );
		Assert.AreEqual( 1.0f / 1600, c.InvGBufferSize.x, 1e-9f );
	}

	/// <summary>
	/// On a device that knows its materials' stencil use, a view's depth has no stencil until a prepared draw needs one: then
	/// it gets one before the frame records (every bind of the frame has it) and keeps it after that object has gone.
	/// </summary>
	[TestMethod]
	[DoNotParallelize]
	public void DepthGetsAStencilBeforeTheFrameThatNeedsIt()
	{
		var device = HeadlessRenderHost.Headless;
		device.StencilUse = true;
		try
		{
			using var plainMaterial = new HeadlessMaterial( "plain", "complex" );
			using var selection = new HeadlessMaterial( "selection", "tools_selection_overlay" ) { Stencil = true };
			var world = new RenderWorld();
			world.Add( new MeshObject( Mesh( plainMaterial ), new Transform( new Vector3( 20, 0, 0 ) ) ) );
			using var system = new RenderSystem();
			var view = LookingDownX();
			Assert.AreEqual( TextureFormat.D32, system.DepthFormat, "no stencil to start with" );
			using var target = ViewTarget.Create( new Vector2Int( 64, 64 ), 1, TextureFormat.RGBA16161616F, system.DepthFormat );

			List<GpuCommand> Frame()
			{
				using var frame = device.Capture();
				RenderHost.Current.NextFrame();
				system.Render( world, view, target );
				return frame.Commands.ToList();
			}

			var plain = Frame();
			Assert.AreEqual( TextureFormat.D32, target.Depth.Format, "nothing needs a stencil" );
			Assert.IsTrue( plain.Any( c => c.Op == GpuOp.Bind && c.Depth == target.Depth ) );

			var selected = new MeshObject( Mesh( selection ), new Transform( new Vector3( 20, 5, 0 ) ) );
			world.Add( selected );
			var old = target.Depth;
			var first = Frame();
			Assert.AreEqual( TextureFormat.D32FS8, target.Depth.Format, "the frame drawing a stencil material has one" );
			Assert.IsTrue( first.Any( c => c.Op == GpuOp.Bind && c.Depth == target.Depth ) );
			Assert.IsFalse( first.Any( c => c.Op == GpuOp.Bind && c.Depth == old ), "nothing of that frame drew into the depth without one" );
			Assert.AreEqual( TextureFormat.D32FS8, system.DepthFormat );

			world.Remove( selected );
			var depth = target.Depth;
			Frame();
			Assert.AreSame( depth, target.Depth, "kept: not made again" );
		}
		finally
		{
			device.StencilUse = false;
		}

		static RenderMesh Mesh( GpuMaterial material ) => new( new HeadlessMesh( "box" ), UnitBox, [[new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, material )]] );
	}

	sealed class IsolatedHost( GpuDevice device ) : RenderHost
	{
		public override GpuDevice Device => device;
	}

	[TestMethod]
	[DoNotParallelize]
	public void PassParametersAndMaterialsStayWithTheirDevice()
	{
		var previous = RenderHost.Current;
		var first = new IsolatedHost( new HeadlessDevice() );
		var second = new IsolatedHost( new HeadlessDevice() );
		try
		{
			RenderHost.Current = first;
			var material = first.Device.PassMaterial( GpuPassMaterial.ShadowTiles );
			ShaderParameters taken;
			using ( ParameterPool.Borrow( out taken ) )
			{
				RenderHost.Current = second;
				Assert.AreNotSame( material, second.Device.PassMaterial( GpuPassMaterial.ShadowTiles ) );
			}

			using ( ParameterPool.Borrow( out var different ) )
				Assert.AreNotSame( taken, different, "switching hosts does not cross the pools" );

			RenderHost.Current = first;
			using ( ParameterPool.Borrow( out var reused ) )
				Assert.AreSame( taken, reused, "the lease returned it to its original pool" );
			Assert.AreSame( material, first.Device.PassMaterial( GpuPassMaterial.ShadowTiles ) );
		}
		finally
		{
			RenderHost.Current = previous;
		}
	}

	[TestMethod]
	public void ScratchReuseRequiresMatchingRolesAndMultisampleUsage()
	{
		using var scratch = new TransientTextures();
		var desc = TextureDesc.Target( 64, 64, TextureFormat.RGBA8888, 4 );
		var first = scratch.Get( TransientTextureRole.IdsView, desc );
		scratch.EndRender();

		var sampled = scratch.Get( TransientTextureRole.IdsView, desc with { SampledMultisampled = true } );
		Assert.AreNotSame( first, sampled );
		var otherRole = scratch.Get( TransientTextureRole.ContactShadowMask, desc );
		Assert.AreNotSame( first, otherRole );
		scratch.EndRender();

		Assert.AreSame( first, scratch.Get( TransientTextureRole.IdsView, desc ) );
		Assert.AreNotSame( first, scratch.Get( TransientTextureRole.IdsView, desc ), "one texture cannot be loaned twice in one render" );
	}

	[TestMethod]
	[DoNotParallelize]
	public void FixedPointOutputUsesTheSuppliedIntermediateDepth()
	{
		var device = HeadlessRenderHost.Headless;
		using var output = device.CreateTexture( TextureDesc.Target( 64, 64, TextureFormat.RGBA8888 ), "output" );
		using var intermediate = ViewTarget.Create( new Vector2Int( 64, 64 ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8 );
		using var system = new RenderSystem();
		var view = LookingDownX();
		view.Viewport = new Rect( 0, 0, 64, 64 );
		using var frame = device.Capture();
		RenderHost.Current.NextFrame();
		system.Render( new RenderWorld(), view, output, 4, null, intermediate: intermediate );

		Assert.IsTrue( frame.Commands.Any( c => c.Op == GpuOp.Bind && c.Depth == intermediate.Depth ) );
	}

	[TestMethod]
	public void ShadowViewsKeepTheWholeDepthRange()
	{
		// Only the main view leaves room for the 3D skybox
		var c = new ViewConstants();
		LookingDownX().FillConstants( ref c, new Vector2( 1000, 1000 ), 0 );

		Assert.AreEqual( 1, c.MsaaSampleCount );
		Assert.AreEqual( 0.0f, c.ViewportMinZ );
	}
}
