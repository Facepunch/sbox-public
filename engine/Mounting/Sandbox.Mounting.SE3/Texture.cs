using System;
using System.Numerics;

namespace SeriousEngine3;

public sealed class TextureLoader( string name ) : ResourceLoader<GameMount>
{
	enum TextureFormat
	{
		Opaque = 2,
		Translucent = 3,
		GrayscaleTranslucent = 6,
		AlphaOnly = 8,
		CompressedOpaque = 9,
		CompressedTranslucent = 10
	}

	protected override object Load()
	{
		var meta = CTSEMeta.Read( Host.GetBytes( name ) );
		var obj = FindTextureObject( meta );
		if ( obj == null ) return Texture.Black;

		var members = obj.Value.Struct.Base.Struct.Members;

		var width = members[0].SLONG;
		var height = members[1].SLONG;
		if ( width <= 0 || height <= 0 ) return Texture.Black;

		var formatId = (TextureFormat)members[3].SLONGEnum;
		var bytes = members[4].BulkBytes ?? members[4].Elements.Select( b => b.UBYTE ).ToArray();

		return formatId switch
		{
			TextureFormat.Opaque => LoadOpaque( width, height, bytes ),
			TextureFormat.Translucent => LoadTranslucent( width, height, bytes ),
			TextureFormat.GrayscaleTranslucent => LoadGrayscaleTranslucent( width, height, bytes ),
			TextureFormat.AlphaOnly => LoadAlphaOnly( width, height, bytes ),
			TextureFormat.CompressedOpaque => LoadCompressedOpaque( width, height, bytes ),
			TextureFormat.CompressedTranslucent => LoadCompressedTranslucent( width, height, bytes ),
			_ => Texture.Black
		};
	}

	static InternalObject FindTextureObject( CTSEMeta meta )
	{
		foreach ( var typeName in TextureTypes )
		{
			var list = meta.GetObjectsOfType( typeName );
			if ( list.Count > 0 ) return list[0];
		}
		return null;
	}

	static readonly string[] TextureTypes = { "CStaticTexture", "CAnimTexture" };

	private static byte[] DecodePixels( byte[] src, int width, int height, int srcChannels, ReadOnlySpan<int> channelMap )
	{
		var mipCount = BitOperations.Log2( (uint)Math.Max( width, height ) ) + 1;
		var pixelsPerChannel = src.Length / srcChannels;
		int dstChannels = channelMap.Length;

		for ( int c = 0; c < srcChannels; c++ )
		{
			int offset = c * pixelsPerChannel;
			byte acc = 0;

			for ( int i = 0; i < pixelsPerChannel; i++ )
			{
				acc = (byte)(acc + src[offset + i]);
				src[offset + i] = acc;
			}
		}

		var dst = new byte[pixelsPerChannel * dstChannels];
		int srcPixel = 0;
		int dstOffset = dst.Length;

		for ( int m = 0; m < mipCount; m++ )
		{
			int w = Math.Max( 1, width >> m );
			int h = Math.Max( 1, height >> m );
			int pixels = w * h;

			dstOffset -= pixels * dstChannels;
			int d = dstOffset;

			for ( int p = 0; p < pixels; p++, srcPixel++ )
			{
				for ( int c = 0; c < dstChannels; c++ )
					dst[d++] = src[channelMap[c] * pixelsPerChannel + srcPixel];
			}
		}

		return dst;
	}

	private static Texture LoadOpaque( int width, int height, byte[] src )
	{
		var mipCount = BitOperations.Log2( (uint)Math.Max( width, height ) ) + 1;
		var dst = DecodePixels( src, width, height, 3, [0, 1, 2] );

		return Texture.Create( width, height, ImageFormat.RGB888 )
			.WithMips( mipCount )
			.WithStaticUsage()
			.WithData( dst )
			.Finish();
	}

	private static Texture LoadTranslucent( int width, int height, byte[] src )
	{
		var mipCount = BitOperations.Log2( (uint)Math.Max( width, height ) ) + 1;
		var dst = DecodePixels( src, width, height, 4, [0, 1, 2, 3] );

		return Texture.Create( width, height, ImageFormat.RGBA8888 )
			.WithMips( mipCount )
			.WithStaticUsage()
			.WithData( dst )
			.Finish();
	}

	private static Texture LoadGrayscaleTranslucent( int width, int height, byte[] src )
	{
		var mipCount = BitOperations.Log2( (uint)Math.Max( width, height ) ) + 1;
		var dst = DecodePixels( src, width, height, 2, [0, 0, 0, 1] );

		return Texture.Create( width, height, ImageFormat.RGBA8888 )
			.WithMips( mipCount )
			.WithStaticUsage()
			.WithData( dst )
			.Finish();
	}

	private static Texture LoadAlphaOnly( int width, int height, byte[] src )
	{
		var mipCount = BitOperations.Log2( (uint)Math.Max( width, height ) ) + 1;
		var dst = DecodePixels( src, width, height, 1, [0] );

		return Texture.Create( width, height, ImageFormat.A8 )
			.WithMips( mipCount )
			.WithStaticUsage()
			.WithData( dst )
			.Finish();
	}

	private static int CompressedMipCount( int width, int height )
		=> Math.Max( 1, BitOperations.Log2( (uint)Math.Min( width, height ) ) - 1 );

	private static Texture LoadCompressedOpaque( int width, int height, byte[] src )
	{
		var mipCount = CompressedMipCount( width, height );

		unsafe
		{
			fixed ( byte* p = src )
			{
				var totalEntries = src.Length / 8;
				var colorBytes = totalEntries * 2;

				var color0 = (ushort*)(p + 0);
				var color1 = (ushort*)(p + colorBytes);
				var lookup = (uint*)(p + (colorBytes << 1));

				var nColors = totalEntries;

				ushort acc = 0;
				for ( var i = 0; i < nColors; i++ )
				{
					acc = (ushort)(acc + color0[i]);
					color0[i] = acc;
				}

				acc = 0;
				for ( var i = 0; i < nColors; i++ )
				{
					acc = (ushort)(acc + color1[i]);
					color1[i] = acc;
				}

				Span<int> blocksPerMip = stackalloc int[mipCount];
				Span<int> colorEntry = stackalloc int[mipCount];

				var totalBytes = 0;
				var colorIdx = 0;
				var w = width;
				var h = height;

				for ( var m = 0; m < mipCount; m++, w = Math.Max( 1, w >> 1 ), h = Math.Max( 1, h >> 1 ) )
				{
					var blocksX = Math.Max( 1, (w + 3) >> 2 );
					var blocksY = Math.Max( 1, (h + 3) >> 2 );
					var blockCount = blocksX * blocksY;

					blocksPerMip[m] = blockCount;
					colorEntry[m] = colorIdx;

					colorIdx += blockCount;
					totalBytes += blockCount * 8;
				}

				var packed = new byte[totalBytes];

				fixed ( byte* q = packed )
				{
					var dst = q;

					for ( var m = mipCount - 1; m >= 0; m-- )
					{
						var blocks = blocksPerMip[m];
						var c0 = color0 + colorEntry[m];
						var c1 = color1 + colorEntry[m];
						var lu = lookup + colorEntry[m];

						for ( var i = 0; i < blocks; i++ )
						{
							*(ushort*)dst = c0[i]; dst += 2;
							*(ushort*)dst = c1[i]; dst += 2;
							*(uint*)dst = lu[i]; dst += 4;
						}
					}

					return Texture.Create( width, height, ImageFormat.DXT1 )
						.WithMips( mipCount )
						.WithStaticUsage()
						.WithData( packed )
						.Finish();
				}
			}
		}
	}

	private static Texture LoadCompressedTranslucent( int width, int height, byte[] src )
	{
		var alphaOffset = src.Length >> 1;
		var lookupBytes = alphaOffset >> 1;
		var colorBytes = lookupBytes >> 1;
		var mipCount = CompressedMipCount( width, height );

		unsafe
		{
			fixed ( byte* p = src )
			{
				var color0 = (ushort*)(p + 0);
				var color1 = (ushort*)(p + colorBytes);
				var lookup = (uint*)(p + (colorBytes << 1));
				var alpha = p + alphaOffset;
				var nColors = colorBytes >> 1;

				ushort acc = 0;
				for ( var i = 0; i < nColors; i++ ) { acc = (ushort)(acc + color0[i]); color0[i] = acc; }

				acc = 0;
				for ( var i = 0; i < nColors; i++ ) { acc = (ushort)(acc + color1[i]); color1[i] = acc; }

				Span<int> blocksPerMip = stackalloc int[mipCount];
				Span<int> colorEntry = stackalloc int[mipCount];
				Span<int> alphaBytesPerMip = stackalloc int[mipCount];

				var totalBytes = 0;
				var colorIdx = 0;
				var alphaIdx = 0;
				var w = width;
				var h = height;

				for ( var m = 0; m < mipCount; m++, w = Math.Max( 1, w >> 1 ), h = Math.Max( 1, h >> 1 ) )
				{
					var blocksX = Math.Max( 1, (w + 3) >> 2 );
					var blocksY = Math.Max( 1, (h + 3) >> 2 );
					var blockCount = blocksX * blocksY;

					blocksPerMip[m] = blockCount;
					colorEntry[m] = colorIdx;
					alphaBytesPerMip[m] = alphaIdx;

					colorIdx += blockCount;
					alphaIdx += blockCount * 8;
					totalBytes += blockCount * 16;
				}

				var packed = new byte[totalBytes];

				fixed ( byte* q = packed )
				{
					var dst = q;

					for ( var m = mipCount - 1; m >= 0; m-- )
					{
						var blocks = blocksPerMip[m];
						var c0 = color0 + colorEntry[m];
						var c1 = color1 + colorEntry[m];
						var lu = lookup + colorEntry[m];
						var al = alpha + alphaBytesPerMip[m];

						for ( var i = 0; i < blocks; i++ )
						{
							*(ulong*)dst = *(ulong*)(al + (i << 3)); dst += 8;
							*(ushort*)dst = c0[i]; dst += 2;
							*(ushort*)dst = c1[i]; dst += 2;
							*(uint*)dst = lu[i]; dst += 4;
						}
					}

					return Texture.Create( width, height, ImageFormat.DXT5 )
						.WithMips( mipCount )
						.WithStaticUsage()
						.WithData( packed )
						.Finish();
				}
			}
		}
	}
}
