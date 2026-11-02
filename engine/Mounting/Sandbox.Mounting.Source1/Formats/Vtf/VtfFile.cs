using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VtfFile
{
	internal const int HeaderLength = 63;

	internal static VtfHeader ReadHeader( Stream stream )
	{
		Span<byte> bytes = stackalloc byte[HeaderLength];
		stream.ReadExactly( bytes );
		var header = MemoryMarshal.Read<VtfHeader>( bytes );
		if ( header.Signature != 0x00465456 )
			throw new InvalidDataException( "Invalid VTF signature." );
		if ( header.MajorVersion != 7 || header.MinorVersion > 5 )
			throw new NotSupportedException( $"VTF version {header.MajorVersion}.{header.MinorVersion} is not supported." );
		return header;
	}

	public static int GetImageDataSize( int width, int height, int depth, int mipCount, VtfFormat format )
	{
		if ( width < 0 || height < 0 || depth < 0 || mipCount < 0 || mipCount > 32 )
			throw new ArgumentOutOfRangeException( nameof( width ), "Invalid image dimensions or mip count." );
		if ( mipCount == 0 || format == VtfFormat.NONE || (width == 0 && height == 0) )
			return 0;

		var bytesPerPixel = format switch
		{
			VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA => 8,
			VtfFormat.DXT3 or VtfFormat.DXT5 => 16,
			VtfFormat.I8 or VtfFormat.P8 or VtfFormat.A8 => 1,
			VtfFormat.RGB565 or VtfFormat.IA88 or VtfFormat.BGR565
				or VtfFormat.BGRX5551 or VtfFormat.BGRA4444 or VtfFormat.BGRA5551
				or VtfFormat.UV88 => 2,
			VtfFormat.RGB888 or VtfFormat.BGR888 or VtfFormat.RGB888_BLUESCREEN
				or VtfFormat.BGR888_BLUESCREEN => 3,
			VtfFormat.RGBA8888 or VtfFormat.ABGR8888 or VtfFormat.ARGB8888
				or VtfFormat.BGRA8888 or VtfFormat.BGRX8888 or VtfFormat.UVWQ8888
				or VtfFormat.UVLX8888 or VtfFormat.R32F => 4,
			VtfFormat.RGBA16161616F or VtfFormat.RGBA16161616 => 8,
			VtfFormat.RGB323232F => 12,
			VtfFormat.RGBA32323232F => 16,
			_ => throw new NotSupportedException( $"VTF format: {format}" )
		};
		var compressed = format is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA
			or VtfFormat.DXT3 or VtfFormat.DXT5;
		var size = 0;
		checked
		{
			width = Math.Max( 1, width );
			height = Math.Max( 1, height );
			depth = Math.Max( 1, depth );
			for ( var mip = 0; mip < mipCount; mip++ )
			{
				size += compressed
					? ((width + 3) / 4) * ((height + 3) / 4) * depth * bytesPerPixel
					: width * height * depth * bytesPerPixel;
				if ( width == 1 && height == 1 && depth == 1 )
					break;
				width = Math.Max( 1, width >> 1 );
				height = Math.Max( 1, height >> 1 );
				depth = Math.Max( 1, depth >> 1 );
			}
		}
		return size;
	}

	private byte[] _hiResPixelData;
	private ImageData[] _imageData;
	private readonly int? _selectedFrame;
	private int _loadedMipmapCount;
	private readonly long _imageOffset;

	public VtfHeader Header { get; }
	public int MipmapCount { get; }
	public int FrameCount { get; }
	public int FaceCount { get; }
	public int ZSliceCount { get; }

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	private struct ImageData
	{
		public readonly int Offset;
		public readonly int Length;

		public ImageData( int offset, int length )
		{
			Offset = offset;
			Length = length;
		}
	}

	private static void Skip( Stream stream, long count )
	{
		if ( count < 0 )
			throw new InvalidDataException( "VTF data offset precedes the header." );
		if ( count == 0 ) return;
		if ( stream.CanSeek )
		{
			if ( count > stream.Length - stream.Position )
				throw new EndOfStreamException( "VTF data offset exceeds the stream." );
			stream.Seek( count, SeekOrigin.Current );
			return;
		}

		Span<byte> buffer = stackalloc byte[4096];
		while ( count > 0 )
		{
			var length = (int)Math.Min( count, buffer.Length );
			stream.ReadExactly( buffer[..length] );
			count -= length;
		}
	}

	public VtfFile( Stream stream, bool onlyHeader = false, int? frame = null )
		: this( stream, onlyHeader, frame, false )
	{
	}

	internal VtfFile( Stream stream, VtfFile metadata, int? frame, bool textureData = false )
	{
		ArgumentNullException.ThrowIfNull( stream );
		ArgumentNullException.ThrowIfNull( metadata );
		Header = metadata.Header;
		MipmapCount = metadata.MipmapCount;
		FrameCount = metadata.FrameCount;
		FaceCount = metadata.FaceCount;
		ZSliceCount = metadata.ZSliceCount;
		_imageOffset = metadata._imageOffset;
		_selectedFrame = frame;
		if ( frame.HasValue && (uint)frame.Value >= FrameCount )
			throw new ArgumentOutOfRangeException( nameof( frame ), frame, $"VTF frame must be between 0 and {FrameCount - 1}." );
		ReadPixels( stream, frame, textureData );
	}

	private VtfFile( Stream stream, bool onlyHeader, int? frame, bool textureData )
	{
		ArgumentNullException.ThrowIfNull( stream );
		_selectedFrame = frame;
		Header = ReadHeader( stream );

		long readCount = HeaderLength;
		Span<byte> buffer = stackalloc byte[8];
		ZSliceCount = 1;
		if ( Header.MinorVersion >= 2 )
		{
			stream.ReadExactly( buffer[..2] );
			ZSliceCount = BinaryPrimitives.ReadUInt16LittleEndian( buffer );
			readCount += 2;
		}

		MipmapCount = Header.MipMapCount;
		FrameCount = Header.Frames;
		FaceCount = (Header.Flags & VtfFlags.ENVMAP) != 0 ? 6 : 1;
		// Older environment maps can carry an additional spheremap after the six cube faces.
		if ( FaceCount == 6 && Header.MinorVersion < 5 && Header.FirstFrame != ushort.MaxValue )
			FaceCount = 7;
		var maxDimension = Math.Max( Math.Max( Header.Width, Header.Height ), ZSliceCount );
		var maxMips = 0;
		for ( var dimension = maxDimension; dimension > 0; dimension >>= 1 )
			maxMips++;
		if ( Header.Width == 0 || Header.Height == 0 || ZSliceCount == 0 || FrameCount == 0
			|| MipmapCount == 0 || MipmapCount > maxMips || Header.HiResFormat == VtfFormat.NONE )
			throw new InvalidDataException( "Invalid VTF dimensions, frames, mip count or image format." );
		if ( frame.HasValue && (uint)frame.Value >= FrameCount )
			throw new ArgumentOutOfRangeException( nameof( frame ), frame, $"VTF frame must be between 0 and {FrameCount - 1}." );

		long? imageOffset = null;
		if ( Header.MinorVersion >= 3 )
		{
			Skip( stream, 3 );
			stream.ReadExactly( buffer[..4] );
			var resourceCount = BinaryPrimitives.ReadUInt32LittleEndian( buffer );
			Skip( stream, 8 );
			readCount += 15;
			if ( readCount + resourceCount * 8L > Header.HeaderSize )
				throw new InvalidDataException( "VTF resource directory exceeds its header." );
			bool hasSheet = false;
			for ( var i = 0U; i < resourceCount; i++ )
			{
				stream.ReadExactly( buffer );
				readCount += 8;
				var type = BinaryPrimitives.ReadUInt32LittleEndian( buffer );
				if ( (type & 0x00ffffff) == 0x10 )
				{
					if ( hasSheet || (type & 0xff000000) != 0 )
						throw new InvalidDataException( "Invalid or duplicate VTF sheet resource." );
					hasSheet = true;
					var sheetOffset = BinaryPrimitives.ReadUInt32LittleEndian( buffer[4..] );
					if ( sheetOffset < Header.HeaderSize )
						throw new InvalidDataException( "VTF sheet resource overlaps its header." );
				}
				if ( (type & 0x00ffffff) != 0x30 )
					continue;
				if ( imageOffset.HasValue || (type & 0xff000000) != 0 )
					throw new InvalidDataException( "Invalid or duplicate VTF high-resolution image resource." );
				imageOffset = BinaryPrimitives.ReadUInt32LittleEndian( buffer[4..] );
			}

			if ( resourceCount != 0 && !imageOffset.HasValue )
				throw new InvalidDataException( "VTF high-resolution image resource is missing." );
		}

		Skip( stream, Header.HeaderSize - readCount );
		if ( imageOffset.HasValue && imageOffset.Value < Header.HeaderSize )
			throw new InvalidDataException( "VTF image resource overlaps its header." );
		imageOffset ??= Header.HeaderSize + (long)GetImageDataSize( Header.LowResWidth, Header.LowResHeight, 1, 1, Header.LowResFormat );
		_imageOffset = imageOffset.Value;
		if ( onlyHeader ) return;
		ReadPixels( stream, frame, textureData, Header.HeaderSize );
	}

	private void ReadPixels( Stream stream, int? frame, bool textureData, long position = 0 )
	{
		_imageData = new ImageData[MipmapCount];
		var offset = 0;
		var loadedFrames = frame.HasValue ? 1 : FrameCount;
		for ( var mipmap = MipmapCount - 1; mipmap >= 0; --mipmap )
		{
			var length = GetImageDataSize( Math.Max( 1, Header.Width >> mipmap ), Math.Max( 1, Header.Height >> mipmap ), 1, 1, Header.HiResFormat );
			_imageData[mipmap] = new ImageData( offset, length );
			offset = checked(offset + checked(length * loadedFrames * FaceCount * Math.Max( 1, ZSliceCount >> mipmap )));
		}
		Skip( stream, _imageOffset - position );
		var sourceSize = (long)offset * (frame.HasValue ? FrameCount : 1);
		if ( offset > Array.MaxLength || (stream.CanSeek && sourceSize > stream.Length - stream.Position) )
			throw new InvalidDataException( "VTF image data is truncated or too large." );
		_loadedMipmapCount = textureData ? GetTextureMipmapCount() : MipmapCount;
		var skippedSize = _imageData[_loadedMipmapCount - 1].Offset;
		Skip( stream, (long)skippedSize * (frame.HasValue ? FrameCount : 1) );
		offset -= skippedSize;
		for ( var mipmap = 0; mipmap < _loadedMipmapCount; mipmap++ )
		{
			var image = _imageData[mipmap];
			_imageData[mipmap] = new ImageData( image.Offset - skippedSize, image.Length );
		}
		_hiResPixelData = new byte[offset];
		if ( !frame.HasValue || FrameCount == 1 )
		{
			stream.ReadExactly( _hiResPixelData, 0, offset );
			return;
		}

		for ( var mipmap = _loadedMipmapCount - 1; mipmap >= 0; --mipmap )
		{
			var image = _imageData[mipmap];
			var frameSize = image.Length * FaceCount * Math.Max( 1, ZSliceCount >> mipmap );
			Skip( stream, (long)frame.Value * frameSize );
			stream.ReadExactly( _hiResPixelData.AsSpan( image.Offset, frameSize ) );
			Skip( stream, (long)(FrameCount - frame.Value - 1) * frameSize );
		}
	}

	internal int GetTextureMipmapCount()
	{
		var count = (Header.Flags & VtfFlags.NOMIP) != 0 ? 1 : MipmapCount;
		if ( FaceCount == 1 && ZSliceCount == 1
			&& Header.HiResFormat is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA or VtfFormat.DXT3 or VtfFormat.DXT5 )
		{
			while ( count > 1 && (Header.Width >> (count - 1) < 4 || Header.Height >> (count - 1) < 4) )
				count--;
		}
		return count;
	}

	internal byte[] GetTexturePixelData( int mipCount, int faceCount )
	{
		if ( _imageData == null )
			throw new InvalidOperationException( "Only the VTF header was loaded." );
		return ZSliceCount == 1 && FaceCount == faceCount && mipCount == _loadedMipmapCount
			? _hiResPixelData : null;
	}

	public ReadOnlySpan<byte> GetHiResPixelData( int mipmap, int frame = 0, int face = 0, int zslice = 0 )
	{
		if ( _imageData == null )
			throw new InvalidOperationException( "Only the VTF header was loaded." );
		if ( (uint)mipmap >= MipmapCount ) throw new ArgumentOutOfRangeException( nameof( mipmap ) );
		if ( mipmap >= _loadedMipmapCount )
			throw new InvalidOperationException( "The VTF mip level was not loaded." );
		if ( (uint)frame >= FrameCount ) throw new ArgumentOutOfRangeException( nameof( frame ) );
		if ( _selectedFrame.HasValue && frame != _selectedFrame.Value )
			throw new InvalidOperationException( $"Only VTF frame {_selectedFrame.Value} was loaded." );
		if ( (uint)face >= FaceCount ) throw new ArgumentOutOfRangeException( nameof( face ) );
		var depth = Math.Max( 1, ZSliceCount >> mipmap );
		if ( (uint)zslice >= depth ) throw new ArgumentOutOfRangeException( nameof( zslice ) );
		var entry = _imageData[mipmap];
		var offset = entry.Offset + entry.Length * (zslice + depth * (face + FaceCount * (_selectedFrame.HasValue ? 0 : frame)));
		return _hiResPixelData.AsSpan( offset, entry.Length );
	}

	public int GetHiResPixelDataLength( int mipmap )
	{
		return GetHiResPixelData( mipmap, _selectedFrame ?? 0 ).Length;
	}

}
