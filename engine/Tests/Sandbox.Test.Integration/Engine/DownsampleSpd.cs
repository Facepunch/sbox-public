using System;

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
	/// The same methods through the engine's Borealis.Gpu backend: downsample_spd dispatched by Borealis.Backend's
	/// GraphicsContext, read back mip by mip.
	/// </summary>
	[TestMethod]
	[DataRow( 1280, 720, Graphics.DownsampleMethod.MinMax )]
	[DataRow( 4500, 300, Graphics.DownsampleMethod.MinMax )]
	[DataRow( 1366, 769, Graphics.DownsampleMethod.Box )]
	public void MatchesCpuReductionOnBorealisGpu( int width, int height, Graphics.DownsampleMethod method )
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var random = new Random( width * height );
		var source = new Vector2[width * height];
		for ( int i = 0; i < source.Length; i++ ) source[i] = new Vector2( random.Float(), random.Float() );

		// Made after the Borealis.Gpu device, so the texture keeps its pixels for that device's copy
		var mips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		using var texture = Texture.Create( width, height, ImageFormat.RG3232F ).WithMips( mips ).WithUAVBinding().WithData<Vector2>( source ).Finish();

		using ( Graphics.Scope.CreateOn( borealis ) )
		{
			Graphics.GenerateMipMaps( texture, method );
		}

		AssertChain( texture, source, width, height, method, ( mip, actual ) => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Vector2>( borealis.ReadBytes( texture, mip ) ).CopyTo( actual ) );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	static void RequireVulkan()
	{
		TestGraphics.Require();
	}

	/// <summary>
	/// Every mip of <paramref name="texture"/> against <paramref name="expected"/>, its first mip, reduced on the CPU.
	/// </summary>
	static void AssertChain( Texture texture, Vector2[] expected, int width, int height, Graphics.DownsampleMethod method, Action<int, Vector2[]> read = null )
	{
		var mips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		for ( int mip = 0; mip < mips; mip++ )
		{
			if ( mip > 0 )
				(expected, width, height) = (Reduce( expected, width, width >> 1, height >> 1, method ), width >> 1, height >> 1);

			var actual = new Vector2[width * height];
			if ( read is null ) texture.GetPixels<Vector2>( (0, 0, width, height), 0, mip, actual, ImageFormat.RG3232F );
			else read( mip, actual );

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
