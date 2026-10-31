using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class PplFile
{
	public sealed record Mesh( int Lod, int Width, int Height, int MipCount, byte[] Pixels );
	public int Checksum { get; }
	public VtfFormat Format { get; }
	public Mesh[] Meshes { get; }

	public PplFile( Stream stream, int checksum )
	{
		const int headerSize = 32;
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();
		VhvFile.ValidateRange( 0, headerSize, data.Length );
		if ( BitConverter.ToInt32( data, 0 ) != 0 )
			throw new NotSupportedException( "Unsupported PPL version; expected 0." );
		Checksum = BitConverter.ToInt32( data, 4 );
		if ( Checksum != checksum )
			throw new InvalidDataException( "PPL and MDL checksums do not match." );
		Format = (VtfFormat)BitConverter.ToInt32( data, 8 );
		int count = BitConverter.ToInt32( data, 12 );
		long directoryEnd = headerSize + (long)count * 32;
		VhvFile.ValidateRange( headerSize, (long)count * 32, data.Length );
		Meshes = new Mesh[count];
		for ( int mesh = 0; mesh < count; mesh++ )
		{
			int entry = headerSize + mesh * 32;
			int lod = BitConverter.ToInt32( data, entry );
			int offset = BitConverter.ToInt32( data, entry + 4 );
			int bytes = BitConverter.ToInt32( data, entry + 8 );
			int width = BitConverter.ToInt32( data, entry + 12 );
			int height = BitConverter.ToInt32( data, entry + 16 );
			if ( lod is < 0 or > 7 || offset < directoryEnd || width is <= 0 or > short.MaxValue || height is <= 0 or > short.MaxValue )
				throw new InvalidDataException( "Invalid PPL mesh header." );
			int mips = 1;
			for ( int size = Math.Max( width, height ); size > 1; size >>= 1 )
				mips++;
			int expected = VtfFile.GetImageDataSize( width, height, 1, mips, Format );
			if ( bytes != expected )
				throw new InvalidDataException( "PPL mesh pixels do not match their format and dimensions." );
			VhvFile.ValidateRange( offset, bytes, data.Length );
			Meshes[mesh] = new Mesh( lod, width, height, mips, data.AsSpan( offset, bytes ).ToArray() );
		}
	}
}
