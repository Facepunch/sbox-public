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

	/// <summary>
	/// The chain against a CPU reduction of random depth: odd sizes, chains past 6 mips that take a second dispatch, a wide,
	/// short source many groups across, the 1x1 chain (resolved, not fused), and the occlusion cull's conservative quarter
	/// chain (min and max of each 4x4 block in a quarter size first mip).
	/// </summary>
	[TestMethod]
	[DataRow( 1366, 769, false )]
	[DataRow( 4500, 300, false )]
	[DataRow( 1, 1, false )]
	[DataRow( 1366, 769, true )]
	[DataRow( 64, 64, true )]
	public void MatchesCpuReduction( int width, int height, bool quarter )
	{
		var random = new Random( width * height );
		var depth = new float[width * height];
		for ( int i = 0; i < depth.Length; i++ ) depth[i] = random.NextSingle();

		int chainWidth = quarter ? (width + 3) / 4 : width, chainHeight = quarter ? (height + 3) / 4 : height;

		// The first mip: each texel's depth, or each 4x4 block's min and max (edge blocks only cover the texels that exist)
		var expected = new (float Min, float Max)[chainWidth * chainHeight];
		for ( int y = 0; y < chainHeight; y++ )
		{
			for ( int x = 0; x < chainWidth; x++ )
			{
				if ( !quarter )
				{
					expected[y * chainWidth + x] = (depth[y * width + x], depth[y * width + x]);
					continue;
				}

				var block = (Min: 1f, Max: 0f);
				for ( int j = y * 4; j < Math.Min( y * 4 + 4, height ); j++ )
					for ( int i = x * 4; i < Math.Min( x * 4 + 4, width ); i++ )
						block = (Math.Min( block.Min, depth[j * width + i] ), Math.Max( block.Max, depth[j * width + i] ));

				expected[y * chainWidth + x] = block;
			}
		}

		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			var bytes = new byte[depth.Length * 4];
			Buffer.BlockCopy( depth, 0, bytes, 0, bytes.Length );
			using var source = backend.CreateTexture( new TextureDesc { Width = width, Height = height, Depth = 1, Format = TextureFormat.R32F, Samples = 1, Mips = 1, Data = bytes }, "Depth chain source" );
			using var chain = backend.CreateTexture( SharedPasses.DepthChainDesc( chainWidth, chainHeight ), "Depth chain" );

			using var context = new BackendContext( backend, null );
			context.Begin();
			SharedPasses.BuildDepthChain( context, source, chain, msaaInput: false, width, height, quarter ? DepthResolveMode.ConservativeQuarter : DepthResolveMode.Sample0 );
			context.BarrierComputeToPixelShaderRead( chain );
			context.Submit();
			Device.WaitIdle();

			var (handle, layout) = BackendDevice.Unwrap( chain );
			Assert.AreEqual( ImageLayout.ShaderRead, layout, "the chain ends readable" );

			int mipWidth = chainWidth, mipHeight = chainHeight;
			for ( int mip = 0; mip < chain.Mips; mip++ )
			{
				if ( mip > 0 ) (expected, mipWidth, mipHeight) = (Reduce( expected, mipWidth, mipWidth >> 1, mipHeight >> 1 ), mipWidth >> 1, mipHeight >> 1);

				var texels = ReadMip( handle, mip, mipWidth * mipHeight * 8 );
				for ( int i = 0; i < expected.Length; i++ )
				{
					float min = BitConverter.ToSingle( texels, i * 8 ), max = BitConverter.ToSingle( texels, i * 8 + 4 );
					if ( MathF.Abs( expected[i].Min - min ) > 1e-5f || MathF.Abs( expected[i].Max - max ) > 1e-5f )
						Assert.Fail( $"Mip {mip} ({mipWidth}x{mipHeight}) texel {i % mipWidth},{i / mipWidth}: expected {expected[i]}, got ({min}, {max})" );
				}
			}
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}

	static (float Min, float Max)[] Reduce( (float Min, float Max)[] source, int sourceWidth, int width, int height )
	{
		var result = new (float Min, float Max)[width * height];
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				var a = source[2 * y * sourceWidth + 2 * x];
				var b = source[2 * y * sourceWidth + 2 * x + 1];
				var c = source[(2 * y + 1) * sourceWidth + 2 * x];
				var d = source[(2 * y + 1) * sourceWidth + 2 * x + 1];
				result[y * width + x] = (Math.Min( Math.Min( a.Min, b.Min ), Math.Min( c.Min, d.Min ) ), Math.Max( Math.Max( a.Max, b.Max ), Math.Max( c.Max, d.Max ) ));
			}
		}

		return result;
	}

	// One mip of a texture in ShaderRead, read back and left in ShaderRead
	byte[] ReadMip( TextureHandle texture, int mip, int bytes )
	{
		var buffer = Device.CreateBuffer( new BufferInfo { Size = bytes, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var list = Device.BeginCommandList( QueueType.Graphics, "Read back" );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.ShaderRead, NewLayout = ImageLayout.CopySource, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.MemoryWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead, BaseMip = mip, MipCount = 1, LayerCount = 1 } );
		list.CopyTextureToBuffer( texture, mip, 0, buffer );
		list.Barrier( new TextureBarrier { Texture = texture, OldLayout = ImageLayout.CopySource, NewLayout = ImageLayout.ShaderRead, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferRead, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.ShaderRead, BaseMip = mip, MipCount = 1, LayerCount = 1 } );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		Device.Wait( Device.Submit( list ) );
		var data = Device.Map( buffer ).ToArray();
		Device.Destroy( buffer );
		return data;
	}
}
