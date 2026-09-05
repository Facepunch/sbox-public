using System.IO;

namespace Sandbox;

/// <summary>
/// Keeps a record of every package we've downloaded into the asset cache, so we
/// can attribute cached files to packages later (storage usage, cleanup etc).
/// Records live alongside the cache in download/.packages/.
/// </summary>
internal static class DownloadedPackages
{
	const string Folder = ".packages";

	internal class Record
	{
		public string Ident { get; set; }
		public string Title { get; set; }
		public string Type { get; set; }
		public string Thumb { get; set; }
		public long Version { get; set; }
		public DateTimeOffset Downloaded { get; set; }
		public DateTimeOffset LastUsed { get; set; }
		public List<Entry> Files { get; set; }

		public struct Entry
		{
			public string Path { get; set; }
			public string Crc { get; set; }
			public long Size { get; set; }
		}
	}

	static string RecordPath( string ident ) => $"{Folder}/{ident.ToLowerInvariant()}.json";

	/// <summary>
	/// Write or refresh the record for this package. Called whenever a package
	/// is downloaded or mounted from cache.
	/// </summary>
	public static void Update( Package package, ManifestSchema.File[] files )
	{
		var fs = EngineFileSystem.DownloadedFiles;
		if ( fs is null ) return;

		var ident = Package.FormatIdent( package.Org.Ident, package.Ident );
		var record = fs.ReadJsonOrDefault<Record>( RecordPath( ident ) ) ?? new Record { Downloaded = DateTimeOffset.UtcNow };

		record.Ident = ident;
		record.Title = package.Title;
		record.Type = package.TypeName;
		record.Thumb = package.Thumb;
		record.Version = package.Revision?.VersionId ?? 0;
		record.LastUsed = DateTimeOffset.UtcNow;
		record.Files = files.Select( x => new Record.Entry { Path = x.Path, Crc = x.Crc, Size = x.Size } ).ToList();

		try
		{
			fs.CreateDirectory( Folder );
			fs.WriteJson( RecordPath( ident ), record );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Couldn't write download record for {ident}" );
		}
	}

	/// <summary>
	/// All the packages we have download records for
	/// </summary>
	public static IEnumerable<Record> All()
	{
		var fs = EngineFileSystem.DownloadedFiles;
		if ( fs is null ) yield break;

		foreach ( var file in fs.FindFile( Folder, "*.json" ) )
		{
			var record = fs.ReadJsonOrDefault<Record>( $"{Folder}/{file}" );
			if ( record?.Files is null ) continue;

			yield return record;
		}
	}

	public static Record Find( string ident )
	{
		if ( !Package.TryParseIdent( ident, out var parsed ) )
			return null;

		ident = Package.FormatIdent( parsed.org, parsed.package );
		return EngineFileSystem.DownloadedFiles?.ReadJsonOrDefault<Record>( RecordPath( ident ) );
	}

	/// <summary>
	/// The absolute path this file would be cached at, or null if it isn't on disk right now.
	/// </summary>
	public static string GetCachedFilePath( Record.Entry entry )
	{
		if ( !ulong.TryParse( entry.Crc, System.Globalization.NumberStyles.HexNumber, null, out var crc ) )
			return null;

		var path = AssetDownloadCache.GetAbsolutePath( entry.Path, crc );
		return File.Exists( path ) ? path : null;
	}

	/// <summary>
	/// Delete a package's cached files and its record. Files that another recorded
	/// package also references are left alone - everything re-downloads on demand anyway.
	/// </summary>
	public static void Delete( Record record )
	{
		var shared = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var other in All() )
		{
			if ( string.Equals( other.Ident, record.Ident, StringComparison.OrdinalIgnoreCase ) )
				continue;

			foreach ( var entry in other.Files )
			{
				shared.Add( $"{entry.Path}:{entry.Crc}" );
			}
		}

		foreach ( var entry in record.Files )
		{
			if ( shared.Contains( $"{entry.Path}:{entry.Crc}" ) )
				continue;

			var path = GetCachedFilePath( entry );
			if ( path is null ) continue;

			try
			{
				File.Delete( path );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Failed to delete cached file {path}" );
			}
		}

		EngineFileSystem.DownloadedFiles?.DeleteFile( RecordPath( record.Ident ) );
	}
}
