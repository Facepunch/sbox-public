using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Sandbox.Mounting.Source1;

internal sealed class BspPakArchive : IContentArchive
{
	private readonly ZipArchive _archive;
	private readonly PakStream _pak = new();
	private readonly Func<Stream> _open;
	private readonly long _pakOffset;
	private readonly long _pakLength;
	private readonly record struct PakEntry( ZipArchiveEntry Zip, long LzmaHeaderOffset );
	private readonly Dictionary<string, PakEntry> _files = new( StringComparer.OrdinalIgnoreCase );
	private readonly object _lock = new();
	private bool _disposed;
	public BspPakArchive( Stream stream, Func<Stream> open )
	{
		_open = open;
		try
		{
			var header = new BspHeader( stream );
			var start = header.Start;
			var lump = header.Lumps[40];
			var offset = lump.offset;
			var length = lump.length;
			if ( lump.version != 0 || lump.uncompressedSize != 0 )
				throw new NotSupportedException( "Versioned or LZMA-compressed BSP pak lumps are not supported." );
			if ( length == 0 )
				return;

			_pakOffset = start + offset;
			_pakLength = length;
			using var pak = ArchiveFile.OpenSlice( stream, _pakOffset, _pakLength );
			_pak.Source = pak;
			_archive = new ZipArchive( _pak, ZipArchiveMode.Read, leaveOpen: true );
			var entries = _archive.Entries;
			_pak.Position = GetDirectoryOffset( _pak );
			Span<byte> directoryEntry = stackalloc byte[46];
			foreach ( var entry in entries )
			{
				_pak.ReadExactly( directoryEntry );
				if ( BitConverter.ToUInt32( directoryEntry[..4] ) != 0x02014B50 )
					throw new InvalidDataException( "Invalid BSP pak ZIP directory entry." );
				int method = BitConverter.ToUInt16( directoryEntry.Slice( 10, 2 ) );
				uint headerOffset = BitConverter.ToUInt32( directoryEntry.Slice( 42, 4 ) );
				int variableLength = BitConverter.ToUInt16( directoryEntry.Slice( 28, 2 ) ) + BitConverter.ToUInt16( directoryEntry.Slice( 30, 2 ) ) + BitConverter.ToUInt16( directoryEntry.Slice( 32, 2 ) );
				if ( variableLength > _pak.Length - _pak.Position )
					throw new InvalidDataException( "BSP pak ZIP directory entry is truncated." );
				_pak.Position += variableLength;
				if ( method == 14 && (headerOffset == uint.MaxValue || headerOffset > _pak.Length - 30) )
					throw new InvalidDataException( "BSP pak LZMA entry has an invalid local header offset." );
				var path = entry.FullName.Replace( '\\', '/' ).TrimStart( '/' );
				if ( path.EndsWith( '/' ) )
					continue;
				if ( !_files.TryAdd( path, new PakEntry( entry, method == 14 ? headerOffset : -1 ) ) )
					throw new InvalidDataException( $"Duplicate BSP pak entry '{path}'." );
			}
		}
		catch
		{
			_archive?.Dispose();
			throw;
		}
		finally
		{
			_pak.Source = null;
			stream.Dispose();
		}
	}

	public IEnumerable<string> GetFilePaths() => _files.Keys;
	public Stream OpenFile( string path ) => OpenFile( path, null );
	public Stream OpenFileHeader( string path, int length )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( length );
		return OpenFile( path, length );
	}

	private Stream OpenFile( string path, int? prefixLength )
	{
		lock ( _lock )
		{
			ObjectDisposedException.ThrowIf( _disposed, this );
			if ( !_files.TryGetValue( path.Replace( '\\', '/' ), out var entry ) )
				throw new FileNotFoundException( $"BSP pak entry '{path}' was not found.", path );
			if ( entry.Zip.Length > Array.MaxLength )
				throw new InvalidDataException( $"BSP pak entry '{path}' is too large to load into memory." );
			using var source = _open();
			using var pak = ArchiveFile.OpenSlice( source, _pakOffset, _pakLength );
			_pak.Source = pak;
			try
			{
				if ( entry.LzmaHeaderOffset >= 0 )
					return new MemoryStream( ReadLzmaEntry( entry, prefixLength ), false );
				var data = new byte[(int)Math.Min( entry.Zip.Length, prefixLength ?? entry.Zip.Length )];
				using var stream = entry.Zip.Open();
				stream.ReadExactly( data );
				return new MemoryStream( data, false );
			}
			finally
			{
				_pak.Source = null;
			}
		}
	}

	private static long GetDirectoryOffset( Stream stream )
	{
		Span<byte> end = stackalloc byte[22];
		if ( stream.Length < end.Length )
			throw new InvalidDataException( "BSP pak ZIP end record is truncated." );
		stream.Position = stream.Length - end.Length;
		stream.ReadExactly( end );
		if ( BitConverter.ToUInt32( end[..4] ) != 0x06054B50 || BitConverter.ToUInt16( end[20..] ) != 0 )
		{
			var tail = new byte[(int)Math.Min( stream.Length, ushort.MaxValue + end.Length )];
			stream.Position = stream.Length - tail.Length;
			stream.ReadExactly( tail );
			int position = tail.Length - end.Length;
			while ( position >= 0 && (BitConverter.ToUInt32( tail, position ) != 0x06054B50 || BitConverter.ToUInt16( tail, position + 20 ) != tail.Length - position - end.Length) )
				position--;
			if ( position < 0 )
				throw new InvalidDataException( "BSP pak ZIP end record was not found." );
			tail.AsSpan( position, end.Length ).CopyTo( end );
		}

		uint offset = BitConverter.ToUInt32( end.Slice( 16, 4 ) );
		if ( offset == uint.MaxValue )
			throw new NotSupportedException( "ZIP64 BSP paks are not supported." );
		if ( offset > stream.Length )
			throw new InvalidDataException( "BSP pak ZIP directory is outside its data range." );
		return offset;
	}

	private byte[] ReadLzmaEntry( PakEntry entry, int? prefixLength )
	{
		Span<byte> header = stackalloc byte[30];
		_pak.Position = entry.LzmaHeaderOffset;
		_pak.ReadExactly( header );
		if ( BitConverter.ToUInt32( header[..4] ) != 0x04034B50 || BitConverter.ToUInt16( header.Slice( 8, 2 ) ) != 14 )
			throw new InvalidDataException( $"BSP pak entry '{entry.Zip.FullName}' has an invalid LZMA ZIP header." );
		if ( (BitConverter.ToUInt16( header.Slice( 6, 2 ) ) & 1) != 0 )
			throw new NotSupportedException( "Encrypted BSP pak entries are not supported." );
		long offset = _pak.Position + BitConverter.ToUInt16( header.Slice( 26, 2 ) ) + BitConverter.ToUInt16( header.Slice( 28, 2 ) );
		long length = entry.Zip.CompressedLength;
		if ( length < 14 || length > Array.MaxLength || offset > _pak.Length || length > _pak.Length - offset )
			throw new InvalidDataException( $"BSP pak entry '{entry.Zip.FullName}' has an invalid compressed data range." );
		_pak.Position = offset;
		Span<byte> data = stackalloc byte[9];
		_pak.ReadExactly( data );
		if ( BitConverter.ToUInt16( data[2..] ) != 5 )
			throw new InvalidDataException( $"BSP pak entry '{entry.Zip.FullName}' has invalid LZMA properties." );
		if ( prefixLength is not null && prefixLength < entry.Zip.Length )
			return LzmaDecoder.Decode( _pak, length - data.Length, (int)entry.Zip.Length, data[4..], prefixLength );
		var compressed = new byte[(int)length - data.Length];
		_pak.ReadExactly( compressed );
		return LzmaDecoder.Decode( compressed, 0, (int)entry.Zip.Length, data[4..] );
	}

	public void Dispose()
	{
		lock ( _lock )
		{
			if ( _disposed )
				return;
			_disposed = true;
			_archive?.Dispose();
			_pak?.Dispose();
		}
	}

	private sealed class PakStream : Stream
	{
		public Stream Source { private get; set; }
		private Stream Current => Source ?? throw new InvalidOperationException( "BSP pak is not open for reading." );
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => Current.Length;
		public override long Position { get => Current.Position; set => Current.Position = value; }
		public override int Read( byte[] buffer, int offset, int count ) => Current.Read( buffer, offset, count );
		public override int Read( Span<byte> buffer ) => Current.Read( buffer );
		public override int ReadByte() => Current.ReadByte();
		public override long Seek( long offset, SeekOrigin origin ) => Current.Seek( offset, origin );
		public override void Flush() => Current.Flush();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	}
}
