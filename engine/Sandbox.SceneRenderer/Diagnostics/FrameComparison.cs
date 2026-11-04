using System.Linq;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Pixel parity checks for SceneLab and <c>r_managed_scene_compare</c>.
/// </summary>
internal static class FrameComparison
{
	public readonly record struct Parity( int Compared, double MeanDifference, int Different )
	{
		/// <summary>
		/// At most 0.1% of compared pixels exceed tolerance.
		/// </summary>
		public bool Matches => Different <= Compared / 1000;

		public override string ToString()
			=> $"parity {(Matches ? "OK" : "FAILED")}: {Compared} pixels compared, mean difference {MeanDifference:0.00}/255, {Different} off by more than {Tolerance}";
	}

	const int Tolerance = 16;

	/// <summary>
	/// Compare maximum RGB differences. Infer backgrounds from the first pixel and skip shared background;
	/// background in only one frame counts as a full mismatch.
	/// </summary>
	public static Parity Compare( Color32[] managed, Color32[] native )
	{
		if ( managed.Length != native.Length ) return new Parity( 0, 0, int.MaxValue );

		var nativeBackground = native[0];
		var managedBackground = managed[0];
		long total = 0;
		int compared = 0, different = 0;

		for ( int i = 0; i < native.Length; i++ )
		{
			var n = native[i];
			var m = managed[i];
			var nativeClear = Same( n, nativeBackground );
			var managedClear = Same( m, managedBackground );
			if ( nativeClear && managedClear ) continue;

			if ( nativeClear != managedClear )
			{
				total += 255;
				compared++;
				different++;
				continue;
			}

			var difference = Math.Max( Math.Abs( m.r - n.r ), Math.Max( Math.Abs( m.g - n.g ), Math.Abs( m.b - n.b ) ) );
			total += difference;
			compared++;
			if ( difference > Tolerance ) different++;
		}

		return new Parity( compared, compared > 0 ? (double)total / compared : 0, different );
	}

	static bool Same( Color32 a, Color32 b ) => a.r == b.r && a.g == b.g && a.b == b.b;

	/// <summary>
	/// Dim the managed frame to one third and highlight differences in red at 8x strength.
	/// </summary>
	public static Color32[] Difference( Color32[] managed, Color32[] native )
	{
		var pixels = new Color32[managed.Length];
		for ( int i = 0; i < pixels.Length && i < native.Length; i++ )
		{
			var m = managed[i];
			var n = native[i];
			var difference = Math.Max( Math.Abs( m.r - n.r ), Math.Max( Math.Abs( m.g - n.g ), Math.Abs( m.b - n.b ) ) );
			pixels[i] = new Color32( (byte)Math.Max( m.r / 3, Math.Min( 255, difference * 8 ) ), (byte)(m.g / 3), (byte)(m.b / 3), 255 );
		}

		return pixels;
	}

	/// <summary>
	/// Read HDR as 8-bit sRGB: clamp, encode and round to nearest, matching <c>ResolveHDRToFinalSDR</c>.
	/// </summary>
	public static Color32[] ReadLinear( Texture texture )
	{
		var width = texture.Width;
		var height = texture.Height;
		var halves = new ushort[width * height * 4];
		texture.GetPixels( (0, 0, width, height), 0, 0, halves.AsSpan(), ImageFormat.RGBA16161616F );

		var pixels = new Color32[width * height];
		for ( int i = 0; i < pixels.Length; i++ )
		{
			pixels[i] = new Color32( Encode( halves[i * 4] ), Encode( halves[i * 4 + 1] ), Encode( halves[i * 4 + 2] ), 255 );
		}

		return pixels;
	}

	static byte Encode( ushort half )
	{
		var linear = Math.Clamp( (float)BitConverter.UInt16BitsToHalf( half ), 0, 1 );
		var gamma = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow( linear, 1.0f / 2.4f ) - 0.055f;
		return (byte)MathF.Round( gamma * 255 );
	}

	/// <summary>
	/// Read resolved colour as sRGB, encoding float targets and reading 8-bit targets directly.
	/// </summary>
	public static Color32[] ReadTarget( ViewTarget target )
	{
		return ViewTarget.IsFloatFormat( target.Resolved.ImageFormat ) ? ReadLinear( target.Resolved ) : target.Resolved.GetPixels();
	}

	/// <summary>
	/// Write pixels to a PNG.
	/// </summary>
	public static void SavePng( Color32[] pixels, int width, int height, string path )
	{
		using var bitmap = new Bitmap( width, height );
		bitmap.SetPixels( pixels.Select( x => x.ToColor() ).ToArray() );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	}
}
