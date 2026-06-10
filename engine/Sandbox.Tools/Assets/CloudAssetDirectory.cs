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

	// stores a flat record of each package for returning in Asset.Package
	ILiteCollection<CachedPackage> packages;

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

	/// <summary>
	/// A flat snapshot of the bits of a <see cref="Package"/> the editor reads back off installed cloud
	/// packages. We deliberately don't persist the whole Package graph: it pulls in the latest news
	/// post, which references its own package, and that circular reference overflows LiteDB's
	/// nested-document limit. Storing this flat record sidesteps the problem and keeps rows tiny.
	/// </summary>
	private class CachedPackage
	{
		public int Id { get; set; }

		public string FullIdent { get; set; }
		public string OrgIdent { get; set; }
		public string OrgTitle { get; set; }
		public string OrgThumb { get; set; }
		public string Ident { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public string Thumb { get; set; }
		public string TypeName { get; set; }
		public string[] Tags { get; set; }
		public DateTimeOffset Created { get; set; }

		// the current revision - VersionId joins to the files table, Meta backs Package.PrimaryAsset
		public long VersionId { get; set; }
		public long FileCount { get; set; }
		public long TotalSize { get; set; }
		public DateTimeOffset RevisionCreated { get; set; }
		public int EngineVersion { get; set; }
		public string ManifestUrl { get; set; }
		public string Changes { get; set; }
		public string Meta { get; set; }
	}

	/// <summary>
	/// Flatten a package into the record we persist. Reads the concrete <see cref="PackageRevision"/>
	/// so we keep the raw metadata JSON and manifest URL, which the public revision interface doesn't
	/// expose.
	/// </summary>
	static CachedPackage ToCached( Package package )
	{
		var cached = new CachedPackage
		{
			FullIdent = package.FullIdent,
			OrgIdent = package.Org?.Ident,
			OrgTitle = package.Org?.Title,
			OrgThumb = package.Org?.Thumb,
			Ident = package.Ident,
			Title = package.Title,
			Summary = package.Summary,
			Thumb = package.Thumb,
			TypeName = package.TypeName,
			Tags = package.Tags,
			Created = package.Created,
		};

		if ( package.Revision is PackageRevision revision )
		{
			cached.VersionId = revision.AssetVersionId;
			cached.FileCount = revision.FileCount;
			cached.TotalSize = revision.TotalSize;
			cached.RevisionCreated = revision.Created;
			cached.EngineVersion = revision.EngineVersion;
			cached.ManifestUrl = revision.ManifestUrl;
			cached.Changes = revision.Changes;
			cached.Meta = revision.Meta;
		}

		return cached;
	}

	/// <summary>
	/// Rebuild a usable package from its stored record. This is what consumers get from
	/// <see cref="FindPackage(string)"/> and <see cref="GetPackages"/> once the directory has been
	/// re-opened, so it carries everything they read: identity, display fields, and a revision whose
	/// metadata still resolves <see cref="Package.PrimaryAsset"/>.
	/// </summary>
	static Package FromCached( CachedPackage cached )
	{
		return new RemotePackage
		{
			Org = new Package.Organization
			{
				Ident = cached.OrgIdent,
				Title = cached.OrgTitle,
				Thumb = cached.OrgThumb,
			},
			Ident = cached.Ident,
			Title = cached.Title,
			Summary = cached.Summary,
			Thumb = cached.Thumb,
			TypeName = cached.TypeName,
			Tags = cached.Tags,
			Created = cached.Created,
			Version = new PackageRevision
			{
				AssetVersionId = cached.VersionId,
				FileCount = cached.FileCount,
				TotalSize = cached.TotalSize,
				Created = cached.RevisionCreated,
				EngineVersion = cached.EngineVersion,
				ManifestUrl = cached.ManifestUrl,
				Changes = cached.Changes,
				Meta = cached.Meta,
			},
		};
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

		packages = db.GetCollection<CachedPackage>( "packages_v2" );
		packages.EnsureIndex( x => x.FullIdent );
		packages.Count(); // to stimulate an open

		// the package store used to hold the entire Package object graph, which couldn't serialize
		// packages whose latest news post referenced themselves. we store a flat CachedPackage now,
		// so drop the old collection - its documents can't be read back into the new shape.
		if ( db.CollectionExists( "packages" ) )
			db.DropCollection( "packages" );

		foreach ( var file in files.FindAll().ToArray() )
		{
			fileCache[file.Path] = file;
		}

		Log.Info( "Validating cloud packages" );
		foreach ( var cached in packages.FindAll().ToArray() )
		{
			var package = FromCached( cached );
			var ident = package.FullIdent;

			if ( !ValidatePackage( package ) )
			{
				Log.Info( $"'{ident}' failed to validate, removing" );
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

		packages.Insert( ToCached( package ) );

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

