using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The depth chain (<c>DepthChainDownsample</c>, which <c>Depth::Get</c> reads: water's depth below its surface, glass,
/// fog) built on Borealis.Gpu by the shared pass Borealis's depth chain layer runs, through compute dispatches with parameters
/// of their own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class DepthChainTests : GpuTest
{
	const int Width = 128, Height = 64;

	/// <summary>
	/// A box drawn into a multisampled depth target, as a frame's prepass leaves it: the chain's first mip holds its depth
	/// where it is and the far plane (0) where nothing drew, and the chain ends readable.
	/// </summary>
	[TestMethod]
	public void BuildsFromTheFramesDepth()
	{
		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = false };
			using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 4, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Depth chain test" );
			var world = new RenderWorld();
			world.Lighting.SunShadowCascades = 0;
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, backend.CreateMaterial( "Depth chain box", Color.White ) ), new Transform( new Vector3( 300, 0, 0 ) ) ) );
			var view = new RenderView { Name = "Depth chain test", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, Width, Height ) };
			view.Rotation = Rotation.LookAt( Vector3.Forward );

			backend.BeginFrame();
			RenderHost.Current.NextFrame();
			system.Render( world, view, target );
			Device.WaitIdle();

			using var chain = backend.CreateTexture( SharedPasses.DepthChainDesc( Width, Height ), "Depth chain" );
			using var context = new BackendContext( backend, null );
			context.Begin();
			SharedPasses.BuildDepthChain( context, target.Depth, chain, msaaInput: true, Width, Height );
			context.BarrierComputeToPixelShaderRead( chain );
			context.Submit();
			Device.WaitIdle();

			var (handle, layout) = BackendDevice.Unwrap( chain );
			Assert.AreEqual( ImageLayout.ShaderRead, layout, "the chain ends readable" );
			var texels = ReadBack( handle, layout );
			BackendDevice.SetLayout( chain, ImageLayout.CopySource );

			// RG32F: min and max depth
			float Min( int x, int y ) => BitConverter.ToSingle( texels, (y * Width + x) * 8 );
			float Max( int x, int y ) => BitConverter.ToSingle( texels, (y * Width + x) * 8 + 4 );
			Assert.IsTrue( Min( Width / 2, Height / 2 ) > 0, $"the box's depth is in the middle ({Min( Width / 2, Height / 2 )})" );
			Assert.IsTrue( Max( Width / 2, Height / 2 ) >= Min( Width / 2, Height / 2 ), "max is at least min" );
			Assert.AreEqual( 0f, Max( 0, 0 ), "the far plane where nothing drew" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
