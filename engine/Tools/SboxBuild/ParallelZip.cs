using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using System.Threading.Channels;

namespace Facepunch;

/// <summary>
/// Writes a zip with every core compressing files at once. ZipArchive deflates one entry at a
/// time, which made zipping the build the slowest part of uploading it. Entries are written
/// in the order they finish, which zip doesn't care about.
/// </summary>
internal static class ParallelZip
{
	const uint LocalHeaderSignature = 0x04034b50;
	const uint CentralHeaderSignature = 0x02014b50;
	const uint EndOfCentralDirectorySignature = 0x06054b50;
	const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
	const uint Zip64LocatorSignature = 0x07064b50;

	const ushort Zip64ExtraId = 0x0001;
	const ushort Utf8NamesFlag = 0x0800;
	const ushort VersionDefault = 20;
	const ushort VersionZip64 = 45;
	const ushort MethodStore = 0;
	const ushort MethodDeflate = 8;
	const uint DirectoryAttribute = 0x10;

	const int ReadBufferSize = 1024 * 1024;
	const long StoreAbove = 1024L * 1024 * 1024;

	/// <summary>
	/// A file that's been compressed and is waiting to be written. <see cref="Compressed"/> is
	/// null when deflating didn't make it smaller, then the file is stored as is.
	/// </summary>
	record CompressedFile( string EntryPath, string AbsolutePath, uint Crc, long Length, MemoryStream Compressed, DateTime LastWrite );

	record CentralEntry( byte[] Name, ushort Method, uint Crc, long CompressedLength, long Length, long Offset, uint DosTime, uint ExternalAttributes );

	/// <param name="files">Zip entry path and the file on disk for each entry.</param>
	/// <param name="emptyFolders">Folder entries to add, each ending in a slash.</param>
	internal static void Create( string zipPath, IReadOnlyCollection<(string EntryPath, string AbsolutePath)> files, IEnumerable<string> emptyFolders = null )
	{
		var workers = Environment.ProcessorCount;
		var central = new List<CentralEntry>( files.Count );

		using var output = new FileStream( zipPath, FileMode.Create, FileAccess.Write, FileShare.None, ReadBufferSize );

		// Bounded, so finished files can't pile up in memory faster than they're written
		var compressed = Channel.CreateBounded<CompressedFile>( workers );

		// Biggest first, so the slowest files aren't left until the end with one core busy
		var ordered = files.OrderByDescending( x => new FileInfo( x.AbsolutePath ).Length ).ToList();

		var producer = Task.Run( async () =>
		{
			try
			{
				await Parallel.ForEachAsync( ordered, new ParallelOptions { MaxDegreeOfParallelism = workers }, async ( file, token ) =>
				{
					await compressed.Writer.WriteAsync( Compress( file.EntryPath, file.AbsolutePath ), token );
				} );

				compressed.Writer.Complete();
			}
			catch ( Exception e )
			{
				compressed.Writer.Complete( e );
			}
		} );

		foreach ( var file in compressed.Reader.ReadAllAsync().ToBlockingEnumerable() )
		{
			central.Add( WriteFile( output, file ) );
		}

		// Surface a compression failure rather than writing a zip that's missing files
		producer.GetAwaiter().GetResult();

		foreach ( var folder in emptyFolders ?? [] )
		{
			central.Add( WriteFolder( output, folder ) );
		}

		WriteCentralDirectory( output, central );
	}

	static CompressedFile Compress( string entryPath, string absolutePath )
	{
		var info = new FileInfo( absolutePath );
		var crc = new Crc32();
		var buffer = new byte[ReadBufferSize];
		long length = 0;

		// Compressed files are held in memory until they're written, which can't go past 2GB.
		// Nothing in the build is near this, but store anything that big rather than fail.
		var compressed = info.Length < StoreAbove ? new MemoryStream() : null;

		using ( var input = new FileStream( absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan ) )
		using ( var deflate = compressed is null ? null : new DeflateStream( compressed, CompressionLevel.Optimal, leaveOpen: true ) )
		{
			int read;
			while ( (read = input.Read( buffer, 0, buffer.Length )) > 0 )
			{
				crc.Append( buffer.AsSpan( 0, read ) );
				deflate?.Write( buffer, 0, read );
				length += read;
			}
		}

		// Already compressed data gets bigger when deflated, store those
		if ( compressed is not null && compressed.Length >= length )
		{
			compressed.Dispose();
			compressed = null;
		}

		return new CompressedFile( entryPath, absolutePath, crc.GetCurrentHashAsUInt32(), length, compressed, info.LastWriteTime );
	}

	static CentralEntry WriteFile( Stream output, CompressedFile file )
	{
		var method = file.Compressed is null ? MethodStore : MethodDeflate;
		var compressedLength = file.Compressed?.Length ?? file.Length;

		var entry = new CentralEntry( Encoding.UTF8.GetBytes( file.EntryPath ), method, file.Crc, compressedLength, file.Length,
			output.Position, ToDosTime( file.LastWrite ), 0 );

		WriteLocalHeader( output, entry );

		if ( file.Compressed is not null )
		{
			file.Compressed.Position = 0;
			file.Compressed.CopyTo( output );
			file.Compressed.Dispose();
		}
		else
		{
			using var input = new FileStream( file.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.SequentialScan );
			input.CopyTo( output );
		}

		return entry;
	}

	static CentralEntry WriteFolder( Stream output, string folder )
	{
		var entry = new CentralEntry( Encoding.UTF8.GetBytes( folder ), MethodStore, 0, 0, 0, output.Position, ToDosTime( DateTime.Now ), DirectoryAttribute );
		WriteLocalHeader( output, entry );
		return entry;
	}

	static bool SizesNeedZip64( CentralEntry entry ) => entry.Length >= uint.MaxValue || entry.CompressedLength >= uint.MaxValue;

	static void WriteLocalHeader( Stream output, CentralEntry entry )
	{
		var zip64 = SizesNeedZip64( entry );

		using var header = new MemoryStream();
		Write32( header, LocalHeaderSignature );
		Write16( header, zip64 ? VersionZip64 : VersionDefault );
		Write16( header, Utf8NamesFlag );
		Write16( header, entry.Method );
		Write32( header, entry.DosTime );
		Write32( header, entry.Crc );
		Write32( header, zip64 ? uint.MaxValue : (uint)entry.CompressedLength );
		Write32( header, zip64 ? uint.MaxValue : (uint)entry.Length );
		Write16( header, (ushort)entry.Name.Length );
		Write16( header, (ushort)(zip64 ? 20 : 0) );
		header.Write( entry.Name );

		if ( zip64 )
		{
			Write16( header, Zip64ExtraId );
			Write16( header, 16 );
			Write64( header, entry.Length );
			Write64( header, entry.CompressedLength );
		}

		header.Position = 0;
		header.CopyTo( output );
	}

	static void WriteCentralDirectory( Stream output, List<CentralEntry> entries )
	{
		var start = output.Position;

		foreach ( var entry in entries )
		{
			var sizesZip64 = SizesNeedZip64( entry );
			var offsetZip64 = entry.Offset >= uint.MaxValue;

			// The zip64 extra only holds the fields whose header value overflowed, in this order
			using var extra = new MemoryStream();
			if ( sizesZip64 )
			{
				Write64( extra, entry.Length );
				Write64( extra, entry.CompressedLength );
			}
			if ( offsetZip64 )
			{
				Write64( extra, entry.Offset );
			}

			var version = extra.Length > 0 ? VersionZip64 : VersionDefault;

			using var header = new MemoryStream();
			Write32( header, CentralHeaderSignature );
			Write16( header, version );
			Write16( header, version );
			Write16( header, Utf8NamesFlag );
			Write16( header, entry.Method );
			Write32( header, entry.DosTime );
			Write32( header, entry.Crc );
			Write32( header, sizesZip64 ? uint.MaxValue : (uint)entry.CompressedLength );
			Write32( header, sizesZip64 ? uint.MaxValue : (uint)entry.Length );
			Write16( header, (ushort)entry.Name.Length );
			Write16( header, (ushort)(extra.Length > 0 ? extra.Length + 4 : 0) );
			Write16( header, 0 ); // comment length
			Write16( header, 0 ); // disk number
			Write16( header, 0 ); // internal attributes
			Write32( header, entry.ExternalAttributes );
			Write32( header, offsetZip64 ? uint.MaxValue : (uint)entry.Offset );
			header.Write( entry.Name );

			if ( extra.Length > 0 )
			{
				Write16( header, Zip64ExtraId );
				Write16( header, (ushort)extra.Length );
				extra.Position = 0;
				extra.CopyTo( header );
			}

			header.Position = 0;
			header.CopyTo( output );
		}

		var end = output.Position;
		var size = end - start;
		var needsZip64 = entries.Count >= ushort.MaxValue || start >= uint.MaxValue || size >= uint.MaxValue;

		using var footer = new MemoryStream();

		if ( needsZip64 )
		{
			Write32( footer, Zip64EndOfCentralDirectorySignature );
			Write64( footer, 44 ); // size of the rest of this record
			Write16( footer, VersionZip64 );
			Write16( footer, VersionZip64 );
			Write32( footer, 0 ); // this disk
			Write32( footer, 0 ); // disk with the central directory
			Write64( footer, entries.Count );
			Write64( footer, entries.Count );
			Write64( footer, size );
			Write64( footer, start );

			Write32( footer, Zip64LocatorSignature );
			Write32( footer, 0 ); // disk with the zip64 end record
			Write64( footer, end );
			Write32( footer, 1 ); // total disks
		}

		Write32( footer, EndOfCentralDirectorySignature );
		Write16( footer, 0 ); // this disk
		Write16( footer, 0 ); // disk with the central directory
		Write16( footer, (ushort)Math.Min( entries.Count, ushort.MaxValue ) );
		Write16( footer, (ushort)Math.Min( entries.Count, ushort.MaxValue ) );
		Write32( footer, (uint)Math.Min( size, uint.MaxValue ) );
		Write32( footer, (uint)Math.Min( start, uint.MaxValue ) );
		Write16( footer, 0 ); // comment length

		footer.Position = 0;
		footer.CopyTo( output );
	}

	static uint ToDosTime( DateTime time )
	{
		if ( time.Year < 1980 )
			time = new DateTime( 1980, 1, 1 );

		var date = ((time.Year - 1980) << 9) | (time.Month << 5) | time.Day;
		var clock = (time.Hour << 11) | (time.Minute << 5) | (time.Second / 2);

		return (uint)((date << 16) | clock);
	}

	static void Write16( Stream stream, ushort value )
	{
		Span<byte> bytes = stackalloc byte[2];
		BinaryPrimitives.WriteUInt16LittleEndian( bytes, value );
		stream.Write( bytes );
	}

	static void Write32( Stream stream, uint value )
	{
		Span<byte> bytes = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32LittleEndian( bytes, value );
		stream.Write( bytes );
	}

	static void Write64( Stream stream, long value )
	{
		Span<byte> bytes = stackalloc byte[8];
		BinaryPrimitives.WriteInt64LittleEndian( bytes, value );
		stream.Write( bytes );
	}
}
