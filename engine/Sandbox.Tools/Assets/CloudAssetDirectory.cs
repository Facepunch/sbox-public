using LiteDB;
using System;

namespace Editor;

/// <summary>
/// There are a bunch of loose files in the source2/cloud folder. This is a directory
/// of where those files are from, so we can backwards lookup which asset they're from.
/// </summary>
internal class CloudAssetDirectory : IDisposable
{
	LiteDatabase db;

	// stores a list of files for looking up which package they use
	ILiteCollection<File> files;

	// stores a list of packages for returning in Asset.Package 
	ILiteCollection<Package> packages;

	// to avoid hitting the database over and over
	Dictionary<string, Package> packageCache = new( StringComparer.OrdinalIgnoreCase );
	Dictionary<string, File> fileCache = new( StringComparer.OrdinalIgnoreCase );

	public class File
	{
		public int Id { get; set; }
		public string Path { get; set; }
		public string Crc { get; set; }
		public long Size { get; set; }
		public string Package { get; set; }
		public long Revision { get; set; }
		public DateTimeOffset InstallDate { get; set; }
	}

	public CloudAssetDirectory( string filename )
	{
		ConnectionString cs = new();
		cs.Connection = ConnectionType.Shared;
		cs.Filename = filename;
		cs.Upgrade = true;

		db = new LiteDatabase( cs );

		files = db.GetCollection<File>( "files" );
		files.EnsureIndex( x => x.Path );
		files.Count(); // to stimulate an open

		packages = db.GetCollection<Package>( "packages" );
		packages.EnsureIndex( x => x.FullIdent );
		packages.Count(); // to stimulate an open

		foreach ( var file in files.FindAll().ToArray() )
		{
			fileCache[file.Path] = file;
		}

		Log.Info( "Validating cloud packages" );
		foreach ( var package in packages.FindAll().ToArray() )
		{
			var ident = package.FullIdent;

			if ( !ValidatePackage( package ) )
			{
				Log.Info( $"'{package.FullIdent}' failed to validate, removing" );
				RemovePackage( package );
				continue;
			}

			packageCache[ident] = package;
		}

		fileCache.Clear();
		foreach ( var file in files.FindAll().ToArray() )
		{
			fileCache[file.Path] = file;
		}
	}

	public void Dispose()
	{
		db?.Dispose();
		db = null;

		files = null;
		packages = null;
	}

	/// <summary>
	/// Remember this package, our cloud assets are using it.
	/// </summary>
	public void AddPackage( Package package )
	{
		packages.DeleteMany( x => x.FullIdent == package.FullIdent );
		files.DeleteMany( x => x.Package == package.FullIdent && x.Revision != package.Revision.VersionId );

		// tidy stale entries to keep it in sync with the database
		var staleKeys = fileCache.Where( x => x.Value.Package == package.FullIdent && x.Value.Revision != package.Revision.VersionId )
			.Select( x => x.Key )
			.ToList();
		foreach ( var key in staleKeys )
		{
			fileCache.Remove( key );
		}

		packages.Insert( package );

		packageCache[package.FullIdent] = package;
	}

	/// <summary>
	/// Remove this package and it's files from our database
	/// </summary>
	public void RemovePackage( Package package )
	{
		packages.DeleteMany( x => x.FullIdent == package.FullIdent );
		files.DeleteMany( x => x.Package == package.FullIdent && x.Revision == package.Revision.VersionId );

		if ( packageCache.ContainsKey( package.FullIdent ) )
		{
			packageCache.Remove( package.FullIdent );
		}
	}

	/// <summary>
	/// Start tracking this path, associate it with this package.
	/// </summary>
	public void AddFile( string path, string crc, long size, string package, long revision )
	{
		path = path.NormalizeFilename( true );

		files.DeleteMany( x => x.Path == path && x.Revision == revision && x.Package == package );

		var f = new File
		{
			Path = path,
			Package = package,
			Crc = crc,
			Size = size,
			Revision = revision,
			InstallDate = DateTime.UtcNow
		};

		files.Insert( f );
		fileCache[path] = f;
	}

	public IEnumerable<string> GetPackageFiles( Package package )
	{
		return files.Find( x => x.Package == package.FullIdent && x.Revision == package.Revision.VersionId ).Select( x => x.Path );
	}

	/// <summary>
	/// Given an ident, find the saved package. This doesn't access the internet, it looks it up
	/// in the database of packages that we've previously downloaded.
	/// </summary>
	internal Package FindPackage( string ident )
	{
		if ( string.IsNullOrEmpty( ident ) )
			return default;

		if ( !Package.TryParseIdent( ident, out var p ) )
			return default;

		if ( packageCache.TryGetValue( Package.FormatIdent( p.org, p.package, local: p.local ), out var package ) )
			return package;

		return default;
	}

	/// <summary>
	/// Validate that the files in this package are all present and correct. This is
	/// done once when retriving the package.
	/// </summary>
	internal bool ValidatePackage( Package package )
	{
		return fileCache.Values
			.Where( x => x.Package == package.FullIdent && x.Revision == package.Revision.VersionId )
			.AsParallel()
			.All( x => FileSystem.Cloud.FileExists( x.Path ) );
	}

	/// <summary>
	/// Used by the Asset to determine its package. We pass in abs and relative path because
	/// it gives us a shortcut for a fast reject - and the strings are already prepared for us.
	/// </summary>
	internal Package FindPackage( string absolutePath, string relativePath )
	{
		// this isn't great but it does the fucking job
		if ( !absolutePath.Contains( ".sbox/cloud/" ) )
			return null;

		relativePath = relativePath.NormalizeFilename( true );

		if ( !fileCache.TryGetValue( relativePath, out var file ) )
		{
			Log.Warning( $"Was unable to determine package for {relativePath}.. Absolute path: {absolutePath}" );

			try
			{
				System.IO.File.Delete( absolutePath );
			}
			catch { } // ignore errors, we'll try again next time

			return null;
		}

		return FindPackage( file.Package );
	}

	internal List<Package> GetPackages()
	{
		return packageCache.Values.ToList();
	}
}

