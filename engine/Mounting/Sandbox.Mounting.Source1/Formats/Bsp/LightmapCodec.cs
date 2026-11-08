using System;


namespace Sandbox.Mounting.Source1;

internal static class LightmapCodec
{
	public const int StyleCount = 128;
	private static readonly float[] ExponentScales = BuildExponentScales();
	private static readonly float[] VertexGamma = BuildVertexGamma( false );
	private static readonly float[] LightmapGamma = BuildVertexGamma( true );
	private static float[] BuildExponentScales()
	{
		var result = new float[256];
		for ( int i = 0; i < result.Length; i++ )
			result[i] = (float)(Math.Pow( 2.0, (sbyte)i ) / 255.0);
		return result;
	}

	private static float[] BuildVertexGamma( bool lightmap )
	{
		var result = new float[4096];
		for ( int i = 0; i < result.Length; i++ )
			result[i] = Math.Min( (float)Math.Pow( i / 1024.0, lightmap ? 1.0f / 2.2f : 1.0 / 2.2f ) * 0.5f, 1.0f );
		return result;
	}

	public static Vector3 Decode( ReadOnlySpan<byte> sample )
	{
		if ( sample.Length < 4 )
			throw new ArgumentException( "A lightmap sample requires four bytes.", nameof( sample ) );
		float scale = ExponentScales[sample[3]];
		return new Vector3( sample[0] * scale, sample[1] * scale, sample[2] * scale );
	}

	internal static float VertexLight( float linear ) => VertexGamma[GammaIndex( linear, 4095 )];
	public static float StyleValue( string pattern, int frame )
	{
		if ( string.IsNullOrEmpty( pattern ) )
			return 256.0f * (1.0f / 264.0f);
		int index = (frame % pattern.Length + pattern.Length) % pattern.Length;
		return (pattern[index] - 'a') * 22.0f * (1.0f / 264.0f);
	}

	public static void Encode( ReadOnlySpan<byte> samples, int luxels, int planes, ReadOnlySpan<float> styles, bool hdr, Span<byte> output )
	{
		if ( luxels <= 0 || planes is not (1 or 4) || styles.Length is < 1 or > 4 )
			throw new ArgumentException( "Invalid lightmap dimensions, planes or style count." );
		int planeBytes = checked(luxels * 4);
		int stride = hdr ? 8 : 4;
		if ( samples.Length != checked(planeBytes * planes * styles.Length) || output.Length != checked(luxels * planes * stride) )
			throw new ArgumentException( "Lightmap buffers do not match their dimensions." );
		Span<Vector3> colors = stackalloc Vector3[4];
		for ( int luxel = 0; luxel < luxels; luxel++ )
		{
			colors.Clear();
			for ( int style = 0; style < styles.Length; style++ )
			{
				float weight = styles[style];
				if ( weight <= 0 )
					continue;
				for ( int plane = 0; plane < planes; plane++ )
				{
					int offset = (style * planes + plane) * planeBytes + luxel * 4;
					colors[plane] += Decode( samples.Slice( offset, 4 ) ) * weight;
				}
			}

			if ( !hdr )
				colors[0] = Gamma( colors[0], planes == 4 ? VertexGamma : LightmapGamma, planes == 4 ? 4095 : 4091 );
			if ( planes == 4 )
			{
				var average = (colors[1] + colors[2] + colors[3]) * (1.0f / 3.0f);
				var correction = new Vector3( average.x != 0 ? colors[0].x / average.x : 0, average.y != 0 ? colors[0].y / average.y : 0, average.z != 0 ? colors[0].z / average.z : 0 );
				for ( int plane = 1; plane < 4; plane++ )
					colors[plane] *= correction;
				if ( !hdr )
					ClampBumped( colors[1..4] );
			}

			for ( int plane = 0; plane < planes; plane++ )
			{
				var color = colors[plane];
				int offset = (plane * luxels + luxel) * stride;
				if ( hdr )
				{
					WriteShort( output, offset, color.x );
					WriteShort( output, offset + 2, color.y );
					WriteShort( output, offset + 4, color.z );
					output[offset + 6] = output[offset + 7] = 255;
				}
				else
				{
					color = ClampColor( color );
					output[offset] = ToByte( color.x );
					output[offset + 1] = ToByte( color.y );
					output[offset + 2] = ToByte( color.z );
					output[offset + 3] = 255;
				}
			}
		}
	}

	private static Vector3 Gamma( Vector3 color, float[] table, int maximum ) => new( table[GammaIndex( color.x, maximum )], table[GammaIndex( color.y, maximum )], table[GammaIndex( color.z, maximum )] );
	private static int GammaIndex( float value, int maximum ) => (int)Math.Clamp( MathF.Round( value * 1024.0f, MidpointRounding.ToEven ), 0, maximum );
	private static byte ToByte( float value ) => (byte)Math.Clamp( MathF.Round( value * 255.0f, MidpointRounding.ToEven ), 0, 255 );
	private static void WriteShort( Span<byte> data, int offset, float value )
	{
		ushort encoded = (ushort)Math.Clamp( value * 4096.0f, 0, 65535 );
		data[offset] = (byte)encoded;
		data[offset + 1] = (byte)(encoded >> 8);
	}

	private static float Maximum( Vector3 color ) => Math.Max( color.x, Math.Max( color.y, color.z ) );
	private static Vector3 ClampColor( Vector3 color )
	{
		float maximum = Maximum( color );
		if ( maximum > 1 )
			color *= 1.0f / maximum;
		return Vector3.Max( color, Vector3.Zero );
	}

	private static void ClampBumped( Span<Vector3> colors )
	{
		Span<int> order = stackalloc int[3];
		Span<float> maxima = stackalloc float[3];
		for ( int i = 0; i < 3; i++ )
			maxima[i] = Maximum( colors[i] );
		for ( int a = 0; a < 3; a++ )
			for ( int b = 0; b < 3; b++ )
			{
				if ( a == b )
					continue;
				int c = 3 - a - b;
				if ( maxima[a] >= maxima[b] && maxima[b] >= maxima[c] )
				{
					order[0] = a;
					order[1] = b;
					order[2] = c;
				}
			}

		for ( int i = 0; i < 3; i++ )
		{
			int index = order[i];
			float maximum = Maximum( colors[index] );
			if ( maximum <= 1 )
				continue;
			var excess = colors[index] * ((maximum - 1.0f) / maximum);
			colors[index] -= excess;
			excess *= 0.5f;
			colors[order[(i + 1) % 3]] += excess;
			colors[order[(i + 2) % 3]] += excess;
		}

		for ( int i = 0; i < 3; i++ )
			colors[i] = ClampColor( colors[i] );
	}
}
