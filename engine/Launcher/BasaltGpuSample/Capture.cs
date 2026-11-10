using Basalt.Gpu;
using System.IO;
using System.IO.Compression;

namespace BasaltGpuSample;

/// <summary>
/// Saves a texture as a PNG: half-float colour becomes 8-bit sRGB, 8-bit colour is written as it is.
/// </summary>
internal static class Capture
{
	/// <summary>
	/// Read <paramref name="texture"/> back (it was last used in <paramref name="layout"/>, and is left in
	/// <see cref="ImageLayout.CopySource"/>) and write it to <paramref name="path"/>.
	/// </summary>
	public static void Save( Device device, TextureHandle texture, ImageLayout layout, string path )
	{
		var info = device.InfoOf( texture );
		var data = Read( device, texture, layout );
		var pixels = new byte[info.Width * info.Height * 4];
		if ( info.Format == Format.RGBA16Float )
		{
			for ( int i = 0; i < pixels.Length; i++ )
			{
				var linear = (float)BitConverter.ToHalf( data, i * 2 );
				var encoded = (i & 3) == 3 ? 1 : linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow( linear, 1 / 2.4f ) - 0.055f;
				pixels[i] = (byte)Math.Clamp( encoded * 255 + 0.5f, 0, 255 );
			}
		}
		else
		{
			data.AsSpan( 0, pixels.Length ).CopyTo( pixels );
		}

		Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( path ) ) );
		File.WriteAllBytes( path, Png( pixels, info.Width, info.Height ) );
	}

	/// <summary>
	/// Mip 0 of <paramref name="texture"/>'s bytes (it was last used in <paramref name="layout"/>, and is left in
	/// <see cref="ImageLayout.CopySource"/>).
	/// </summary>
	public static byte[] Read( Device device, TextureHandle texture, ImageLayout layout )
	{
		var info = device.InfoOf( texture );
		var size = info.Width * info.Height * info.Format.BytesPerBlock();
		var buffer = device.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Capture" } );

		var list = device.BeginCommandList( QueueType.Graphics, "Capture" );
		list.Barrier( TextureBarrier.Transition( texture, layout, ImageLayout.CopySource, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead ) );
		list.CopyTextureToBuffer( texture, 0, 0, buffer );
		list.Barrier( new BufferBarrier { Buffer = buffer, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.Host, DestinationAccess = MemoryAccess.HostRead } );
		device.Wait( device.Submit( list ) );

		var data = device.Map( buffer ).ToArray();
		device.Destroy( buffer );
		return data;
	}

	static byte[] Png( byte[] rgba, int width, int height )
	{
		var raw = new MemoryStream();
		for ( int y = 0; y < height; y++ )
		{
			raw.WriteByte( 0 );
			raw.Write( rgba, y * width * 4, width * 4 );
		}

		var compressed = new MemoryStream();
		using ( var z = new ZLibStream( compressed, CompressionLevel.Fastest, leaveOpen: true ) ) raw.WriteTo( z );

		var png = new MemoryStream();
		png.Write( [137, 80, 78, 71, 13, 10, 26, 10] );
		var header = new byte[13];
		BigEndian( header, 0, (uint)width );
		BigEndian( header, 4, (uint)height );
		header[8] = 8;
		header[9] = 6;
		Chunk( png, "IHDR", header );
		Chunk( png, "IDAT", compressed.ToArray() );
		Chunk( png, "IEND", [] );
		return png.ToArray();
	}

	static void Chunk( Stream stream, string type, byte[] data )
	{
		var length = new byte[4];
		BigEndian( length, 0, (uint)data.Length );
		stream.Write( length );

		var typed = new byte[4 + data.Length];
		for ( int i = 0; i < 4; i++ ) typed[i] = (byte)type[i];
		data.CopyTo( typed, 4 );
		stream.Write( typed );

		var crc = 0xFFFFFFFFu;
		foreach ( var b in typed )
		{
			crc ^= b;
			for ( int k = 0; k < 8; k++ ) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
		}

		var sum = new byte[4];
		BigEndian( sum, 0, ~crc );
		stream.Write( sum );
	}

	static void BigEndian( byte[] buffer, int offset, uint value )
	{
		buffer[offset] = (byte)(value >> 24);
		buffer[offset + 1] = (byte)(value >> 16);
		buffer[offset + 2] = (byte)(value >> 8);
		buffer[offset + 3] = (byte)value;
	}
}
