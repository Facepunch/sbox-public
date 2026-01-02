using System;
using System.Text;

namespace ValvePak;

internal sealed class VpkEntry
{
	public string FileName { get; init; }
	public string DirectoryName { get; init; }
	public string TypeName { get; init; }
	public uint CRC32 { get; init; }
	public uint Length { get; init; }
	public uint Offset { get; init; }
	public ushort ArchiveIndex { get; init; }
	public byte[] SmallData { get; init; }

	public uint TotalLength => Length + (uint)(SmallData?.Length ?? 0);

	public string GetFileName() => TypeName == " " ? FileName : $"{FileName}.{TypeName}";

	public string GetFullPath() => DirectoryName == " " ? GetFileName() : $"{DirectoryName}/{GetFileName()}";
}

internal sealed class VpkArchive : IDisposable
{
	private const uint MAGIC = 0x55AA1234;

	private BinaryReader _reader;
	private string _fileName;

	public uint Version { get; private set; }
	public uint TreeSize { get; private set; }
	public Dictionary<string, List<VpkEntry>> Entries { get; private set; }

	public void Read( string filename )
	{
		SetFileName( filename );

		var fs = new FileStream( filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite );
		Read( fs );
	}

	public void Read( Stream input )
	{
		if ( _fileName == null )
			throw new InvalidOperationException( "SetFileName() must be called before Read() with a stream." );

		_reader = new BinaryReader( input );

		if ( _reader.ReadUInt32() != MAGIC )
			throw new InvalidDataException( "Not a VPK file." );

		Version = _reader.ReadUInt32();
		TreeSize = _reader.ReadUInt32();

		if ( Version == 1 )
		{
			// Version 1 has no additional header fields
		}
		else if ( Version == 2 )
		{
			// Skip v2 header fields we don't need
			_reader.ReadUInt32(); // FileDataSectionSize
			_reader.ReadUInt32(); // ArchiveMD5SectionSize
			_reader.ReadUInt32(); // OtherMD5SectionSize
			_reader.ReadUInt32(); // SignatureSectionSize
		}
		else
		{
			throw new InvalidDataException( $"Unsupported VPK version: {Version}" );
		}

		ReadEntries();
	}

	public void SetFileName( string filename )
	{
		if ( filename.EndsWith( ".vpk", StringComparison.OrdinalIgnoreCase ) )
			filename = filename[..^4];

		if ( filename.EndsWith( "_dir", StringComparison.OrdinalIgnoreCase ) )
			filename = filename[..^4];

		_fileName = filename;
	}

	public VpkEntry FindEntry( string filePath )
	{
		filePath = filePath.Replace( '\\', '/' );

		var lastSeparator = filePath.LastIndexOf( '/' );
		var directory = lastSeparator > -1 ? filePath[..lastSeparator] : " ";
		var fileName = filePath[(lastSeparator + 1)..];

		var dot = fileName.LastIndexOf( '.' );
		string extension;

		if ( dot > -1 )
		{
			extension = fileName[(dot + 1)..];
			fileName = fileName[..dot];
		}
		else
		{
			extension = " ";
		}

		if ( Entries == null || !Entries.TryGetValue( extension, out var entries ) )
			return null;

		directory = directory.Trim( '/' );
		if ( directory.Length == 0 )
			directory = " ";

		foreach ( var entry in entries )
		{
			if ( entry.DirectoryName == directory && entry.FileName == fileName )
				return entry;
		}

		return null;
	}

	public void ReadEntry( VpkEntry entry, out byte[] output )
	{
		output = new byte[entry.TotalLength];
		ReadEntry( entry, output );
	}

	public void ReadEntry( VpkEntry entry, byte[] output )
	{
		var totalLength = (int)entry.TotalLength;

		if ( output.Length < totalLength )
			throw new ArgumentException( "Output buffer too small." );

		if ( entry.SmallData?.Length > 0 )
			entry.SmallData.CopyTo( output, 0 );

		if ( entry.Length > 0 )
		{
			using var fs = GetArchiveStream( entry.ArchiveIndex );
			fs.Seek( entry.Offset, SeekOrigin.Begin );

			int offset = entry.SmallData?.Length ?? 0;
			int remaining = (int)entry.Length;
			while ( remaining > 0 )
			{
				int read = fs.Read( output, offset, remaining );
				if ( read == 0 )
					break;
				offset += read;
				remaining -= read;
			}
		}
	}

	private Stream GetArchiveStream( ushort archiveIndex )
	{
		if ( archiveIndex == 0x7FFF )
			return _reader.BaseStream;

		var archivePath = $"{_fileName}_{archiveIndex:D3}.vpk";
		return new FileStream( archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite );
	}

	private void ReadEntries()
	{
		var entries = new Dictionary<string, List<VpkEntry>>( StringComparer.OrdinalIgnoreCase );

		while ( true )
		{
			var typeName = ReadNullTermString();
			if ( string.IsNullOrEmpty( typeName ) )
				break;

			var typeEntries = new List<VpkEntry>();

			while ( true )
			{
				var directoryName = ReadNullTermString();
				if ( string.IsNullOrEmpty( directoryName ) )
					break;

				while ( true )
				{
					var fileName = ReadNullTermString();
					if ( string.IsNullOrEmpty( fileName ) )
						break;

					var crc32 = _reader.ReadUInt32();
					var smallDataSize = _reader.ReadUInt16();
					var archiveIndex = _reader.ReadUInt16();
					var offset = _reader.ReadUInt32();
					var length = _reader.ReadUInt32();
					var terminator = _reader.ReadUInt16();

					if ( terminator != 0xFFFF )
						throw new InvalidDataException( $"Invalid entry terminator: 0x{terminator:X4}" );

					byte[] smallData = null;
					if ( smallDataSize > 0 )
					{
						smallData = _reader.ReadBytes( smallDataSize );
					}

					typeEntries.Add( new VpkEntry
					{
						FileName = fileName,
						DirectoryName = directoryName,
						TypeName = typeName,
						CRC32 = crc32,
						Length = length,
						Offset = offset,
						ArchiveIndex = archiveIndex,
						SmallData = smallData ?? []
					} );
				}
			}

			entries[typeName] = typeEntries;
		}

		Entries = entries;
	}

	private string ReadNullTermString()
	{
		var bytes = new List<byte>();
		byte b;
		while ( (b = _reader.ReadByte()) != 0 )
			bytes.Add( b );
		return Encoding.UTF8.GetString( [.. bytes] );
	}

	public void Dispose()
	{
		_reader?.Dispose();
		_reader = null;
	}
}
