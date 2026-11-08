using System;
using Sandbox.SceneRenderer.Gpu;

namespace EngineTests;

[TestClass]
public class DownsampleSpdTest
{
	/// <summary>
	/// GenerateMipMaps' downsample_spd methods against a CPU reduction: odd sizes, chains past 6 mips that take a second dispatch,
	/// and a wide, short source many groups across.
	/// </summary>
	[TestMethod]
	[DataRow( 1280, 720, Graphics.DownsampleMethod.MinMax )]
	[DataRow( 1366, 769, Graphics.DownsampleMethod.MinMax )]
	[DataRow( 4500, 300, Graphics.DownsampleMethod.MinMax )]
	[DataRow( 1366, 769, Graphics.DownsampleMethod.Box )]
	public void MatchesCpuReduction( int width, int height, Graphics.DownsampleMethod method )
	{
		RequireVulkan();

		var random = new Random( width * height );
		var source = new Vector2[width * height];
		for ( int i = 0; i < source.Length; i++ ) source[i] = new Vector2( random.Float(), random.Float() );

		var mips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		using var texture = Texture.Create( width, height, ImageFormat.RG3232F ).WithMips( mips ).WithUAVBinding().WithData<Vector2>( source ).Finish();

		using ( Graphics.Scope.Create() )
		{
			Graphics.GenerateMipMaps( texture, method );
		}

		AssertChain( texture, source, width, height, method );
	}

	/// <summary>
	/// The Hi-Z depth chain, resolved in the first downsample_spd dispatch or by depthresolve_cs before it, min/max reduced to the last mip.
	/// </summary>
	[TestMethod]
	[DataRow( 1366, 769, true )]
	[DataRow( 1366, 769, false )]
	[DataRow( 4500, 300, true )]
	[DataRow( 1, 1, true )]
	[DataRow( 1, 1, false )]
	public void DepthChainMatchesCpuReduction( int width, int height, bool fuseResolve )
	{
		RequireVulkan();

		var random = new Random( width * height );
		var depth = new float[width * height];
		for ( int i = 0; i < depth.Length; i++ ) depth[i] = random.Float();

		using var source = Texture.Create( width, height, ImageFormat.R32F ).WithData<float>( depth ).Finish();
		using var chain = SharedPasses.DepthChainTarget( width, height );

		using ( Graphics.Scope.Create() )
		{
			SharedPasses.BuildDepthChain( Graphics.Context, source, chain.DepthTarget, false, width, height, fuseResolve: fuseResolve );
		}

		AssertChain( chain.DepthTarget, depth.Select( d => new Vector2( d, d ) ).ToArray(), width, height, Graphics.DownsampleMethod.MinMax );
	}

	/// <summary>
	/// The occlusion cull's depth chain: depthresolve_cs writing the min and max of each 4x4 block into a quarter size first mip.
	/// </summary>
	[TestMethod]
	[DataRow( 1366, 769 )]
	[DataRow( 64, 64 )]
	public void ConservativeQuarterChainMatchesCpuReduction( int width, int height )
	{
		RequireVulkan();

		var random = new Random( width * height );
		var depth = new float[width * height];
		for ( int i = 0; i < depth.Length; i++ ) depth[i] = random.Float();

		int quarterWidth = (width + 3) / 4, quarterHeight = (height + 3) / 4;
		using var source = Texture.Create( width, height, ImageFormat.R32F ).WithData<float>( depth ).Finish();
		using var chain = SharedPasses.DepthChainTarget( quarterWidth, quarterHeight );

		using ( Graphics.Scope.Create() )
		{
			SharedPasses.BuildDepthChain( Graphics.Context, source, chain.DepthTarget, false, width, height, DepthResolveMode.ConservativeQuarter );
		}

		// Edge blocks only cover the texels that exist
		var expected = new Vector2[quarterWidth * quarterHeight];
		for ( int y = 0; y < quarterHeight; y++ )
		{
			for ( int x = 0; x < quarterWidth; x++ )
			{
				var block = new Vector2( 1, 0 );
				for ( int j = y * 4; j < Math.Min( y * 4 + 4, height ); j++ )
					for ( int i = x * 4; i < Math.Min( x * 4 + 4, width ); i++ )
						block = new Vector2( MathF.Min( block.x, depth[j * width + i] ), MathF.Max( block.y, depth[j * width + i] ) );

				expected[y * quarterWidth + x] = block;
			}
		}

		AssertChain( chain.DepthTarget, expected, quarterWidth, quarterHeight, Graphics.DownsampleMethod.MinMax );
	}

	static void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );
	}

	/// <summary>
	/// Every mip of <paramref name="texture"/> against <paramref name="expected"/>, its first mip, reduced on the CPU.
	/// </summary>
	static void AssertChain( Texture texture, Vector2[] expected, int width, int height, Graphics.DownsampleMethod method )
	{
		var mips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		for ( int mip = 0; mip < mips; mip++ )
		{
			if ( mip > 0 )
				(expected, width, height) = (Reduce( expected, width, width >> 1, height >> 1, method ), width >> 1, height >> 1);

			var actual = new Vector2[width * height];
			texture.GetPixels<Vector2>( (0, 0, width, height), 0, mip, actual, ImageFormat.RG3232F );

			for ( int i = 0; i < expected.Length; i++ )
			{
				if ( MathF.Abs( expected[i].x - actual[i].x ) > 1e-5f || MathF.Abs( expected[i].y - actual[i].y ) > 1e-5f )
					Assert.Fail( $"Mip {mip} ({width}x{height}) texel {i % width},{i / width}: expected {expected[i]}, got {actual[i]}" );
			}
		}
	}

	static Vector2[] Reduce( Vector2[] source, int sourceWidth, int width, int height, Graphics.DownsampleMethod method )
	{
		var result = new Vector2[width * height];
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				var a = source[2 * y * sourceWidth + 2 * x];
				var b = source[2 * y * sourceWidth + 2 * x + 1];
				var c = source[(2 * y + 1) * sourceWidth + 2 * x];
				var d = source[(2 * y + 1) * sourceWidth + 2 * x + 1];
				result[y * width + x] = method == Graphics.DownsampleMethod.MinMax
					? new Vector2( MathF.Min( MathF.Min( a.x, b.x ), MathF.Min( c.x, d.x ) ), MathF.Max( MathF.Max( a.y, b.y ), MathF.Max( c.y, d.y ) ) )
					: (a + b + c + d) * 0.25f;
			}
		}

		return result;
	}
}
