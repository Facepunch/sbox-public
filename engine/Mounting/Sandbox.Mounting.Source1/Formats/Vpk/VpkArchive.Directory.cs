using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VpkArchive
{
	private static void ValidatePath( string path )
	{
		foreach ( var range in path.AsSpan().Split( '/' ) )
		{
			var part = path.AsSpan()[range];
			if ( part.IsEmpty || part.SequenceEqual( "." ) || part.SequenceEqual( ".." ) )
				throw new InvalidDataException( $"Invalid VPK entry path '{path}'." );
		}
	}

	private ref struct DirectoryReader( ReadOnlySpan<byte> data )
	{
		private readonly ReadOnlySpan<byte> data = data;
		public int Position { get; private set; }
		public bool IsComplete => Position == data.Length;

		public ReadOnlySpan<byte> ReadBytes( int length )
		{
			if ( length > data.Length - Position )
				throw new InvalidDataException( "Truncated VPK directory entry or preload." );
			var result = data.Slice( Position, length );
			Position += length;
			return result;
		}

		public ReadOnlySpan<byte> ReadStringBytes()
		{
			var length = data[Position..].IndexOf( (byte)0 );
			if ( length < 0 )
				throw new InvalidDataException( "Unterminated string in VPK directory tree." );
			var result = data.Slice( Position, length );
			Position += length + 1;
			return result;
		}

		public string ReadString() => Encoding.UTF8.GetString( ReadStringBytes() );
	}

	private static string CreatePath( string prefix, ReadOnlyMemory<byte> name, string suffix )
	{
		return string.Create( prefix.Length + Encoding.UTF8.GetCharCount( name.Span ) + suffix.Length,
			(prefix, name, suffix), static ( text, parts ) =>
			{
				parts.prefix.AsSpan().CopyTo( text );
				var written = Encoding.UTF8.GetChars( parts.name.Span, text[parts.prefix.Length..] );
				parts.suffix.AsSpan().CopyTo( text[(parts.prefix.Length + written)..] );
			} );
	}

	private void ReadDirectory( Stream stream )
	{
		using var reader = new BinaryReader( stream, Encoding.UTF8, leaveOpen: true );
		if ( stream.Length < 12 || reader.ReadUInt32() != 0x55aa1234 )
			throw new InvalidDataException( "Invalid VPK header signature." );

		var version = reader.ReadUInt32();
		if ( version is not (1 or 2) )
			throw new InvalidDataException( $"Unsupported VPK version {version}." );

		var treeSize = reader.ReadUInt32();
		long embeddedSize;
		if ( version == 2 )
		{
			if ( stream.Length < 28 )
				throw new InvalidDataException( "Truncated VPK v2 header." );
			embeddedSize = reader.ReadUInt32();
			long trailingSize = reader.ReadUInt32();
			trailingSize += reader.ReadUInt32();
			trailingSize += reader.ReadUInt32();
			if ( 28L + treeSize + embeddedSize + trailingSize > stream.Length )
				throw new InvalidDataException( "VPK sections extend beyond the file." );
		}
		else
		{
			embeddedSize = stream.Length - 12 - treeSize;
		}

		var treeEnd = stream.Position + treeSize;
		if ( treeSize == 0 || treeSize > Array.MaxLength || treeEnd > stream.Length )
			throw new InvalidDataException( "Invalid VPK directory tree size." );
		_embeddedDataOffset = treeEnd;
		var tree = GC.AllocateUninitializedArray<byte>( (int)treeSize );
		stream.ReadExactly( tree );
		var directoryReader = new DirectoryReader( tree );

		while ( true )
		{
			var extension = directoryReader.ReadString();
			if ( extension.Length == 0 )
				break;
			var suffix = extension == " " ? "" : "." + extension;
			var suffixHasSeparators = suffix.AsSpan().IndexOfAny( '/', '\\' ) >= 0;
			while ( true )
			{
				var directory = directoryReader.ReadString();
				if ( directory.Length == 0 )
					break;
				// A directory/extension group shares its prefix; allocate only the final path per name.
				var prefix = "";
				if ( directory != " " )
				{
					directory = NormalizePath( directory );
					ValidatePath( directory );
					prefix = directory + "/";
				}
				while ( true )
				{
					var nameOffset = directoryReader.Position;
					var name = directoryReader.ReadStringBytes();
					if ( name.Length == 0 )
						break;

					var entry = directoryReader.ReadBytes( 18 );
					// The first four bytes are the CRC32 of the complete file, including preload.
					var preloadSize = BinaryPrimitives.ReadUInt16LittleEndian( entry[4..] );
					var archiveIndex = BinaryPrimitives.ReadUInt16LittleEndian( entry[6..] );
					var offset = BinaryPrimitives.ReadUInt32LittleEndian( entry[8..] );
					var length = BinaryPrimitives.ReadUInt32LittleEndian( entry[12..] );
					if ( BinaryPrimitives.ReadUInt16LittleEndian( entry[16..] ) != 0xffff )
						throw new InvalidDataException( "Invalid VPK directory entry terminator." );
					if ( archiveIndex > EmbeddedArchiveIndex )
						throw new InvalidDataException( "Invalid VPK archive index." );
					if ( archiveIndex == EmbeddedArchiveIndex && length > 0 && (long)offset + length > embeddedSize )
						throw new InvalidDataException( "VPK entry extends beyond the embedded data section." );

					var preloadOffset = directoryReader.Position;
					directoryReader.ReadBytes( preloadSize );
					var preload = preloadSize == 0 ? default : tree.AsMemory( preloadOffset, preloadSize );
					var path = CreatePath( prefix, tree.AsMemory( nameOffset, name.Length ), suffix );
					if ( suffixHasSeparators || name.IndexOfAny( (byte)'/', (byte)'\\' ) >= 0 )
					{
						path = NormalizePath( path );
						ValidatePath( path );
					}
					else if ( suffix.Length == 0 && (name.SequenceEqual( "."u8 ) || name.SequenceEqual( ".."u8 )) )
					{
						throw new InvalidDataException( $"Invalid VPK entry path '{path}'." );
					}
					_files[path] = new( archiveIndex, offset, length, preload );
				}
			}
		}

		if ( !directoryReader.IsComplete )
			throw new InvalidDataException( "VPK directory tree size does not match its contents." );
	}
}
