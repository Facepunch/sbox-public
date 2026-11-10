namespace Sandbox.SceneLab;

/// <summary>
/// Textures made at runtime in each format the renderer samples - uncompressed, float, and the block compressed ones,
/// encoded here - with full mip chains, so a format the engine uploads or samples wrong shows as a wrong picture. The
/// encoder is simple (each block's endpoints are its colours' extremes), but the picture is made for it: every 4x4
/// block's colours lie on a line through black, so they come through close to exact.
/// </summary>
internal static class TestTextures
{
	/// <summary>
	/// The test picture at <paramref name="width"/> x <paramref name="height"/>, in sRGB bytes: a 4x4 board of hues,
	/// each cell brightening left to right with a dark border, alpha falling top to bottom. With mips.
	/// </summary>
	public static Texture Create( ImageFormat format, int width, int height, string name )
	{
		var levels = Mips( width, height ).Select( x => Picture( x.Width, x.Height, width ) ).ToArray();
		var data = Enumerable.Reverse( levels ).SelectMany( x => Encode( format, x ) ).ToArray();
		return Texture.Create( width, height, format ).WithMips( levels.Length ).WithStaticUsage().WithData( data ).WithName( name ).Finish();
	}

	/// <summary>
	/// Each mip a colour of its own over a fine checker - grey in the top level, then red, orange, yellow, green, cyan,
	/// blue, magenta - so which mip a pixel sampled, and how it blended them, shows as its colour.
	/// </summary>
	public static Texture MipColors( int size )
	{
		Color32[] tints = [new( 255, 255, 255 ), new( 255, 60, 60 ), new( 255, 150, 40 ), new( 250, 230, 50 ), new( 70, 220, 70 ), new( 60, 220, 230 ), new( 70, 90, 255 ), new( 230, 70, 230 )];
		var levels = Mips( size, size ).Select( ( x, mip ) => Checker( x.Width, x.Height, tints[Math.Min( mip, tints.Length - 1 )] ) ).ToArray();
		var data = Enumerable.Reverse( levels ).SelectMany( x => Encode( ImageFormat.RGBA8888, x ) ).ToArray();
		return Texture.Create( size, size, ImageFormat.RGBA8888 ).WithMips( levels.Length ).WithStaticUsage().WithData( data ).WithName( "scenelab_mip_colors" ).Finish();
	}

	/// <summary>
	/// A 1x1 texture of one colour, for the inputs a material needs that aren't being tested.
	/// </summary>
	public static Texture Solid( ImageFormat format, Color32 color, string name )
	{
		return Texture.Create( 1, 1, format ).WithData( new byte[] { color.r, color.g, color.b, color.a } ).WithName( name ).Finish();
	}

	/// <summary>
	/// The size of each level of a full mip chain, largest first (the data goes smallest first, as a vtex has it), the way <see cref="Texture2DBuilder.WithMips()"/> counts them.
	/// </summary>
	static IEnumerable<(int Width, int Height)> Mips( int width, int height )
	{
		int count = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		for ( int i = 0; i < count; i++ ) yield return (Math.Max( 1, width >> i ), Math.Max( 1, height >> i ));
	}

	sealed record Image( int Width, int Height, Color32[] Pixels )
	{
		public Color32 this[int x, int y] => Pixels[Math.Clamp( y, 0, Height - 1 ) * Width + Math.Clamp( x, 0, Width - 1 )];
	}

	static Image Picture( int width, int height, int topWidth )
	{
		var pixels = new Color32[width * height];
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				// Cells in the texture's own coordinates, so each mip is the same picture smaller
				float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
				int cx = (int)(u * 4), cy = (int)(v * 4);
				float fu = u * 4 - cx, fv = v * 4 - cy;

				// A texel of the top level wide, so the lower levels lose it
				float border = 1.5f / topWidth * 4;
				var lit = fu < border || fv < border ? 0.05f : 0.2f + 0.8f * fu;
				var hue = new ColorHsv( (cx + cy * 4) * 360.0f / 16, 0.75f, 1 ).ToColor();
				var color = (hue * lit).ToColor32();
				color.a = (byte)(255 - fv * 200);
				pixels[y * width + x] = color;
			}
		}

		return new Image( width, height, pixels );
	}

	static Image Checker( int width, int height, Color32 tint )
	{
		var pixels = new Color32[width * height];
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				var dark = ((x / 2) + (y / 2)) % 2 == 1;
				pixels[y * width + x] = dark ? new Color32( (byte)(tint.r / 4), (byte)(tint.g / 4), (byte)(tint.b / 4) ) : tint;
			}
		}

		return new Image( width, height, pixels );
	}

	static byte[] Encode( ImageFormat format, Image image ) => format switch
	{
		ImageFormat.RGBA8888 or ImageFormat.RGBA8888_LINEAR => image.Pixels.SelectMany( c => new[] { c.r, c.g, c.b, c.a } ).ToArray(),
		ImageFormat.BGRA8888 => image.Pixels.SelectMany( c => new[] { c.b, c.g, c.r, c.a } ).ToArray(),
		ImageFormat.RGBA16161616F => image.Pixels.SelectMany( c => Linear( c ).Concat( [c.a / 255.0f] ).SelectMany( x => BitConverter.GetBytes( (Half)x ) ) ).ToArray(),
		ImageFormat.DXT1 => Blocks( image, Bc1 ),
		ImageFormat.DXT5 => Blocks( image, b => [.. Bc4( b, 3 ), .. Bc1( b )] ),
		ImageFormat.ATI1N => Blocks( image, b => Bc4( b, 0 ) ),
		ImageFormat.ATI2N => Blocks( image, b => [.. Bc4( b, 0 ), .. Bc4( b, 1 )] ),
		ImageFormat.BC6H => Blocks( image, Bc6h ),
		ImageFormat.BC7 => Blocks( image, Bc7 ),
		_ => throw new NotSupportedException( $"No encoder for {format}" ),
	};

	static float[] Linear( Color32 c ) => [ToLinear( c.r ), ToLinear( c.g ), ToLinear( c.b )];

	static float ToLinear( byte value )
	{
		var c = value / 255.0f;
		return c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
	}

	/// <summary>
	/// Encode each 4x4 block, left to right and top to bottom. Blocks past the image's edge repeat its last texels.
	/// </summary>
	static byte[] Blocks( Image image, Func<Color32[], byte[]> encode )
	{
		var data = new List<byte>();
		var block = new Color32[16];

		for ( int by = 0; by < (image.Height + 3) / 4; by++ )
		{
			for ( int bx = 0; bx < (image.Width + 3) / 4; bx++ )
			{
				for ( int i = 0; i < 16; i++ ) block[i] = image[bx * 4 + i % 4, by * 4 + i / 4];
				data.AddRange( encode( block ) );
			}
		}

		return data.ToArray();
	}

	static int Channel( Color32 c, int channel ) => channel switch { 0 => c.r, 1 => c.g, 2 => c.b, _ => c.a };

	/// <summary>
	/// Where each value lies between <paramref name="from"/> and <paramref name="to"/>, 0 to 1, along the line between them.
	/// </summary>
	static float[] Project( float[][] values, float[] from, float[] to )
	{
		var axis = from.Zip( to, ( a, b ) => b - a ).ToArray();
		var length = axis.Sum( x => x * x );
		return values.Select( v => length <= 0 ? 0 : Math.Clamp( v.Select( ( x, i ) => (x - from[i]) * axis[i] ).Sum() / length, 0, 1 ) ).ToArray();
	}

	/// <summary>
	/// BC1: two RGB565 endpoints, the brighter first for the four-colour mode, and a 2-bit index per texel.
	/// </summary>
	static byte[] Bc1( Color32[] block )
	{
		static ushort Pack( int r, int g, int b ) => (ushort)(((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255));
		static float[] Unpack( ushort c ) => [(c >> 11) * 255 / 31.0f, (c >> 5 & 63) * 255 / 63.0f, (c & 31) * 255 / 31.0f];

		var c0 = Pack( block.Max( x => x.r ), block.Max( x => x.g ), block.Max( x => x.b ) );
		var c1 = Pack( block.Min( x => x.r ), block.Min( x => x.g ), block.Min( x => x.b ) );

		uint indices = 0;
		if ( c0 != c1 )
		{
			// Palette order: c0, c1, then the two between, nearer c0 first
			int[] order = [0, 2, 3, 1];
			var t = Project( block.Select( x => new float[] { x.r, x.g, x.b } ).ToArray(), Unpack( c0 ), Unpack( c1 ) );
			for ( int i = 0; i < 16; i++ ) indices |= (uint)order[(int)MathF.Round( t[i] * 3 )] << (i * 2);
		}

		return [(byte)c0, (byte)(c0 >> 8), (byte)c1, (byte)(c1 >> 8), .. BitConverter.GetBytes( indices )];
	}

	/// <summary>
	/// BC4, and BC3's alpha: two 8-bit endpoints, the larger first for the eight-value mode, and a 3-bit index per texel.
	/// </summary>
	static byte[] Bc4( Color32[] block, int channel )
	{
		var values = block.Select( x => Channel( x, channel ) ).ToArray();
		int a0 = values.Max(), a1 = values.Min();

		ulong indices = 0;
		if ( a0 != a1 )
		{
			for ( int i = 0; i < 16; i++ )
			{
				// 0 is a0, 1 is a1, 2 to 7 step from a0 toward a1
				var step = (int)MathF.Round( (a0 - values[i]) * 7.0f / (a0 - a1) );
				var index = step == 0 ? 0 : step == 7 ? 1 : step + 1;
				indices |= (ulong)index << (i * 3);
			}
		}

		return [(byte)a0, (byte)a1, .. BitConverter.GetBytes( indices ).Take( 6 )];
	}

	static readonly int[] Weights4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

	/// <summary>
	/// The 4-bit index whose weight is nearest <paramref name="t"/>.
	/// </summary>
	static int Nearest4( float t ) => Enumerable.Range( 0, 16 ).MinBy( i => MathF.Abs( Weights4[i] / 64.0f - t ) );

	/// <summary>
	/// Write <paramref name="bits"/> of <paramref name="value"/> into a 128-bit block at <paramref name="position"/>, least
	/// significant bit first.
	/// </summary>
	static void Put( byte[] block, ref int position, int bits, ulong value )
	{
		for ( int i = 0; i < bits; i++, position++ )
		{
			if ( (value >> i & 1) != 0 ) block[position / 8] |= (byte)(1 << (position % 8));
		}
	}

	/// <summary>
	/// The indices for a one-subset block, with the first texel's index under 8 (its top bit isn't stored): if it isn't,
	/// the endpoints swap and every index flips.
	/// </summary>
	static int[] AnchorIndices( float[] t, out bool swap )
	{
		var indices = t.Select( Nearest4 ).ToArray();
		swap = indices[0] >= 8;
		if ( swap ) indices = indices.Select( x => 15 - x ).ToArray();
		return indices;
	}

	/// <summary>
	/// BC7 mode 6: one subset, RGBA endpoints of 7 bits plus a shared bit each, 4-bit indices.
	/// </summary>
	static byte[] Bc7( Color32[] block )
	{
		var texels = block.Select( x => new float[] { x.r, x.g, x.b, x.a } ).ToArray();
		var lo = Enumerable.Range( 0, 4 ).Select( c => texels.Min( x => x[c] ) ).ToArray();
		var hi = Enumerable.Range( 0, 4 ).Select( c => texels.Max( x => x[c] ) ).ToArray();

		// Each endpoint's shared low bit: whichever gets its four channels closer
		static (int[] Values, int P) Quantize( float[] endpoint )
		{
			int[] Values( int p ) => endpoint.Select( v => Math.Clamp( (int)MathF.Round( (v - p) / 2 ), 0, 127 ) ).ToArray();
			float Error( int p ) => Values( p ).Select( ( q, i ) => MathF.Abs( (q << 1 | p) - endpoint[i] ) ).Sum();
			var best = Error( 0 ) <= Error( 1 ) ? 0 : 1;
			return (Values( best ), best);
		}

		var e0 = Quantize( lo );
		var e1 = Quantize( hi );
		static float[] Decoded( (int[] Values, int P) e ) => e.Values.Select( q => (float)(q << 1 | e.P) ).ToArray();

		var indices = AnchorIndices( Project( texels, Decoded( e0 ), Decoded( e1 ) ), out var swap );
		if ( swap ) (e0, e1) = (e1, e0);

		var data = new byte[16];
		int position = 0;
		Put( data, ref position, 7, 1 << 6 );
		for ( int c = 0; c < 4; c++ )
		{
			Put( data, ref position, 7, (ulong)e0.Values[c] );
			Put( data, ref position, 7, (ulong)e1.Values[c] );
		}

		Put( data, ref position, 1, (ulong)e0.P );
		Put( data, ref position, 1, (ulong)e1.P );
		for ( int i = 0; i < 16; i++ ) Put( data, ref position, i == 0 ? 3 : 4, (ulong)indices[i] );
		return data;
	}

	/// <summary>
	/// What a 10-bit unsigned BC6H endpoint decodes to, as half-float bits - its unquantized value scaled by 31/64.
	/// </summary>
	static readonly int[] Bc6hHalves = Enumerable.Range( 0, 1024 ).Select( x => (x == 0 ? 0 : x == 1023 ? 0xFFFF : ((x << 16) + 0x8000) >> 10) * 31 >> 6 ).ToArray();

	/// <summary>
	/// BC6H mode 11 (unsigned): one region, RGB endpoints of 10 bits, 4-bit indices - the linear colour, as halves.
	/// </summary>
	static byte[] Bc6h( Color32[] block )
	{
		// Positive halves sort as their bits do, so the endpoints and the interpolation work on the bits
		var texels = block.Select( x => Linear( x ).Select( v => (float)BitConverter.HalfToUInt16Bits( (Half)v ) ).ToArray() ).ToArray();

		static int Quantize( float half )
		{
			var index = Array.BinarySearch( Bc6hHalves, (int)half );
			if ( index >= 0 ) return index;
			index = ~index;
			if ( index == 0 ) return 0;
			if ( index == Bc6hHalves.Length ) return Bc6hHalves.Length - 1;
			return half - Bc6hHalves[index - 1] <= Bc6hHalves[index] - half ? index - 1 : index;
		}

		var e0 = Enumerable.Range( 0, 3 ).Select( c => Quantize( texels.Min( x => x[c] ) ) ).ToArray();
		var e1 = Enumerable.Range( 0, 3 ).Select( c => Quantize( texels.Max( x => x[c] ) ) ).ToArray();
		static float[] Decoded( int[] e ) => e.Select( x => (float)Bc6hHalves[x] ).ToArray();

		var indices = AnchorIndices( Project( texels, Decoded( e0 ), Decoded( e1 ) ), out var swap );
		if ( swap ) (e0, e1) = (e1, e0);

		var data = new byte[16];
		int position = 0;
		Put( data, ref position, 5, 0b00011 );
		foreach ( var value in e0 ) Put( data, ref position, 10, (ulong)value );
		foreach ( var value in e1 ) Put( data, ref position, 10, (ulong)value );
		for ( int i = 0; i < 16; i++ ) Put( data, ref position, i == 0 ? 3 : 4, (ulong)indices[i] );
		return data;
	}
}
