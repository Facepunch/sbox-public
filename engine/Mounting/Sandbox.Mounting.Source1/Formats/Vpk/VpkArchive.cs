using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VpkArchive : IDisposable
{
	private const ushort EmbeddedArchiveIndex = 0x7fff;
	private readonly string _directoryFileName;
	private readonly string _archiveFileNameFormat;
	private long _embeddedDataOffset;

	private readonly Dictionary<string, DirectoryEntry> _files = new( StringComparer.OrdinalIgnoreCase );
	private readonly object _archiveLock = new();
	private readonly Dictionary<ushort, ArchiveFile> _archives = [];
	private volatile bool _disposed;

	private readonly record struct DirectoryEntry( ushort ArchiveIndex, uint Offset, uint Length, ReadOnlyMemory<byte> Preload );

	public VpkArchive( string dirFilePath )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( dirFilePath );
		if ( !dirFilePath.EndsWith( ".vpk", StringComparison.OrdinalIgnoreCase ) )
			throw new ArgumentException( "Expected a VPK file.", nameof( dirFilePath ) );

		_directoryFileName = dirFilePath;
		var suffixLength = dirFilePath.EndsWith( "_dir.vpk", StringComparison.OrdinalIgnoreCase ) ? 8 : 4;
		_archiveFileNameFormat = dirFilePath[..^suffixLength] + "_{0:000}.vpk";
		using var stream = File.OpenRead( dirFilePath );
		ReadDirectory( stream );
	}

	private static string NormalizePath( string path ) => path.Replace( '\\', '/' );

	public IEnumerable<string> GetFilePaths() => _files.Keys;

	public Stream OpenFile( string filePath )
	{
		ThrowIfDisposed();
		if ( !_files.TryGetValue( NormalizePath( filePath ), out var entry ) )
			throw new FileNotFoundException( $"Could not find file '{filePath}' in VPK archive.", filePath );

		if ( entry.Length == 0 )
			return ArchiveFile.OpenPreload( entry.Preload );

		lock ( _archiveLock )
		{
			ThrowIfDisposed();
			if ( !_archives.TryGetValue( entry.ArchiveIndex, out var archive ) )
			{
				var path = entry.ArchiveIndex == EmbeddedArchiveIndex
					? _directoryFileName : string.Format( _archiveFileNameFormat, entry.ArchiveIndex );
				archive = new ArchiveFile( path );
				_archives.Add( entry.ArchiveIndex, archive );
			}
			var offset = entry.Offset + (entry.ArchiveIndex == EmbeddedArchiveIndex ? _embeddedDataOffset : 0);
			return archive.OpenRead( offset, entry.Length, entry.Preload );
		}
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf( _disposed, this );

	public void Dispose()
	{
		lock ( _archiveLock )
		{
			if ( _disposed )
				return;
			_disposed = true;
			foreach ( var archive in _archives.Values )
				archive.Release();
			_archives.Clear();
		}
		GC.SuppressFinalize( this );
	}
}
