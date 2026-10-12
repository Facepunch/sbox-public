using System;
using System.Linq;

namespace TextureTests;

[TestClass]
public class CompressedMipTests
{
	[TestInitialize]
	public void RequireGraphics()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to verify uploaded GPU pixels." );
	}

	/// <summary>
	/// The large mips of a compiled texture are stored compressed on disk while the small ones are
	/// stored raw. A broken mip codec garbles mip 0, so it stops matching the raw 64x64 mip when downsampled.
	/// </summary>
	[TestMethod]
	[DataRow( "models/citizen_clothes/dress/posh_dress/Textures/posh_dress.vmat" )]
	[DataRow( "models/citizen_clothes/dress/simple_dress/textures/simple_dress_blue.vmat" )]
	public void CompiledTextureMipsDecode( string materialPath )
	{
		Texture texture = null;
		Sandbox.Rendering.TextureStreaming.ExecuteWithDisabled( () =>
		{
			var material = Material.Load( materialPath );
			Assert.IsNotNull( material, "Citizen fixture missing." );
			texture = material.FirstTexture;
		} );

		Assert.IsNotNull( texture );
		Assert.IsTrue( texture.Width >= 256 && texture.Width == texture.Height, "Expected a square texture large enough to have compressed mips." );

		const int smallSize = 64;
		var width = texture.Width;
		var smallMip = System.Numerics.BitOperations.Log2( (uint)(width / smallSize) );
		var mip0 = new Color32[width * width];
		var small = new Color32[smallSize * smallSize];

		// Texture bits are hooked up asynchronously, readback fails until they are
		for ( var attempt = 0; ; attempt++ )
		{
			try
			{
				texture.GetPixels( (0, 0, width, width), 0, 0, mip0.AsSpan(), ImageFormat.RGBA8888 );
				texture.GetPixels( (0, 0, smallSize, smallSize), 0, smallMip, small.AsSpan(), ImageFormat.RGBA8888 );
				break;
			}
			catch ( Exception ) when ( attempt < 200 )
			{
				NativeEngine.g_pResourceSystem.UpdateSimple();
				System.Threading.Thread.Sleep( 10 );
			}
		}

		var scale = width / smallSize;
		double error = 0, spread = 0;
		var smallMean = small.Average( c => (c.r + c.g + c.b) / 3.0 );
		for ( var y = 0; y < smallSize; y++ )
			for ( var x = 0; x < smallSize; x++ )
			{
				double r = 0, g = 0, b = 0;
				for ( var sy = 0; sy < scale; sy++ )
					for ( var sx = 0; sx < scale; sx++ )
					{
						var p = mip0[(y * scale + sy) * width + x * scale + sx];
						r += p.r; g += p.g; b += p.b;
					}

				var n = scale * scale;
				var m = small[y * smallSize + x];
				error += Math.Abs( r / n - m.r ) + Math.Abs( g / n - m.g ) + Math.Abs( b / n - m.b );
				spread += Math.Abs( (m.r + m.g + m.b) / 3.0 - smallMean ) * 3;
			}

		// Correct mips land well under the spread of the small mip, garbled ones several times over it
		Assert.IsTrue( error < spread, $"Mip 0 doesn't match the raw {smallSize}x{smallSize} mip (error {error:0}, spread {spread:0})." );
	}
}
