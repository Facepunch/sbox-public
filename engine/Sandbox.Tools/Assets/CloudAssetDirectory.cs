using LiteDB;
using System;
using System.Collections.Concurrent;

namespace Editor;

/// <summary>
/// There are a bunch of loose files in the .sbox/cloud folder. This is a directory
/// of where those files are from, so we can backwards lookup which package(s) they're from.
/// </summary>
internal class CloudAssetDirectory : IDisposable
{
	LiteDatabase db;

	// one row per on-disk file
	ILiteCollection<File> files;

	// one row per (path, package, revision) claim on a file
	ILiteCollection<PackageFile> links;

	// stores a flat record of each package for returning in Asset.Package
	ILiteCollection<CachedPackage> packages;

	// to avoid hitting the database over and over
	Dictionary<string, Package> packageCache = new( StringComparer.OrdinalIgnoreCase );
	Dictionary<string, File> filesByPath = new( StringComparer.OrdinalIgnoreCase );
	Dictionary<string, List<PackageFile>> linksByPath = new( StringComparer.OrdinalIgnoreCase );
	Dictionary<(string Package, long Revision), List<PackageFile>> linksByPackage = new();

	/// <summary>
	/// A physical file that exists on disk in the cloud cache. There's exactly one of these
	/// per path - it records what's actually there, independent of who depends on it.
	/// </summary>
	public class File
	{
		[BsonId]
		public string Path { get; set; }
		public string Crc { get; set; }
		public long Size { get; set; }
		public DateTimeOffset CreationDate { get; set; }
		public DateTimeOffset InstallDate { get; set; }
	}

	/// <summary>
	/// Link between a physical file and a package that depends on it.
	/// There can be many of these per path, and many per package.
	/// </summary>
	public class PackageFile
	{
		[BsonId]
		public int Id { get; set; }
		public string Path { get; set; }
		public string Package { get; set; }
		public long Revision { get; set; }
		public bool IsPrimaryAsset { get; set; }
		public DateTimeOffset RevisionCreated { get; set; }
	}

	/// <summary>
	/// A flat snapshot of the bits of a <see cref="Package"/> the editor reads back off installed cloud
	/// packages. We deliberately don't persist the whole Package graph: it pulls in the latest news
	/// post, which references its own package, and that circular reference overflows LiteDB's
	/// nested-document limit. Storing this flat record sidesteps the problem and keeps rows tiny.
	/// </summary>
	private class CachedPackage
	{
		[BsonId]
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

		// the current revision - VersionId joins to the links table, Meta backs Package.PrimaryAsset
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
	/// <see cref="FindPackage(string)"/> and <see cref="GetPackages()"/> once the directory has been
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

		// "files" used to store one row per (path, package, revision) with content data
		// duplicated across every package that referenced the same path. The new shape
		// splits physical files from package links, so the old collections can't be read
		// back into it - drop them and rebuild from scratch.
		if ( db.CollectionExists( "files" ) )
			db.DropCollection( "files" );

		// the package store used to hold the entire Package object graph, which couldn't serialize
		// packages whose latest news post referenced themselves. we store a flat CachedPackage now,
		// so drop the old collection - its documents can't be read back into the new shape.
		if ( db.CollectionExists( "packages" ) )
			db.DropCollection( "packages" );

		//  1:1 Package:Files -> multiple PackageFile links between those
		if ( db.CollectionExists( "packages_v2" ) )
			db.DropCollection( "packages_v2" );

		files = db.GetCollection<File>( "files_v2" );
		files.Count(); // to stimulate an open

		links = db.GetCollection<PackageFile>( "links_v1" );
		links.EnsureIndex( x => x.Path );
		links.EnsureIndex( x => new { x.Package, x.Revision } );
		links.Count(); // to stimulate an open

		packages = db.GetCollection<CachedPackage>( "packages_v3" );
		packages.Count(); // to stimulate an open

		ValidateFiles();
		RebuildCaches();

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

		PruneOrphanedFiles();
	}

	public void Dispose()
	{
		db?.Dispose();
		db = null;

		files = null;
		links = null;
		packages = null;
	}

	void RebuildCaches()
	{
		filesByPath.Clear();
		linksByPath.Clear();
		linksByPackage.Clear();

		foreach ( var file in files.FindAll() )
		{
			filesByPath[file.Path] = file;
		}

		foreach ( var link in links.FindAll() )
		{
			AddLinkToCache( link );
		}
	}

	void AddLinkToCache( PackageFile link )
	{
		if ( !linksByPath.TryGetValue( link.Path, out var byPath ) )
		{
			byPath = new List<PackageFile>();
			linksByPath[link.Path] = byPath;
		}
		byPath.Add( link );

		var key = (link.Package, link.Revision);
		if ( !linksByPackage.TryGetValue( key, out var byPackage ) )
		{
			byPackage = new List<PackageFile>();
			linksByPackage[key] = byPackage;
		}
		byPackage.Add( link );
	}

	void RemoveLinksFromCache( string package, long revision )
	{
		var key = (package, revision);
		if ( !linksByPackage.TryGetValue( key, out var byPackage ) )
			return;

		foreach ( var link in byPackage )
		{
			if ( linksByPath.TryGetValue( link.Path, out var byPath ) )
			{
				byPath.RemoveAll( l => l.Package == package && l.Revision == revision );
				if ( byPath.Count == 0 )
					linksByPath.Remove( link.Path );
			}
		}
		linksByPackage.Remove( key );
	}

	/// <summary>
	/// Remove any Files that no longer have a link pointing at it. If a file isn't claimed by any package, we don't need to track it anymore.
	/// </summary>
	void PruneOrphanedFiles()
	{
		var orphaned = filesByPath.Keys.Where( p => !linksByPath.ContainsKey( p ) ).ToList();
		if ( orphaned.Count == 0 )
			return;

		foreach ( var path in orphaned )
		{
			files.Delete( path );
			filesByPath.Remove( path );
		}
	}

	IEnumerable<File> GetFilesForPackage( string fullIdent, long revision )
	{
		if ( !linksByPackage.TryGetValue( (fullIdent, revision), out var linkList ) )
			return Enumerable.Empty<File>();

		return linkList
			.Select( l => filesByPath.TryGetValue( l.Path, out var f ) ? f : null )
			.Where( f => f != null );
	}

	/// <summary>
	/// Remember this package, our cloud assets are using it.
	/// </summary>
	public void AddPackage( Package package )
	{
		var ident = package.FullIdent;
		var revision = package.Revision.VersionId;

		db.BeginTrans();
		try
		{
			packages.Upsert( ToCached( package ) );
			links.DeleteMany( x => x.Package == ident && x.Revision != revision );
			db.Commit();
		}
		catch
		{
			db.Rollback();
			throw;
		}

		// tidy stale entries from old revisions of this package
		foreach ( var key in linksByPackage.Keys.Where( k => k.Package == ident && k.Revision != revision ).ToList() )
		{
			RemoveLinksFromCache( key.Package, key.Revision );
		}
		PruneOrphanedFiles();

		packageCache[ident] = package;
	}

	/// <summary>
	/// Remove this package and it's files from our database
	/// </summary>
	public void RemovePackage( Package package )
	{
		var ident = package.FullIdent;
		var revision = package.Revision.VersionId;

		db.BeginTrans();
		try
		{
			packages.Delete( ident );
			links.DeleteMany( x => x.Package == ident && x.Revision == revision );
			db.Commit();
		}
		catch
		{
			db.Rollback();
			throw;
		}

		RemoveLinksFromCache( ident, revision );
		PruneOrphanedFiles();

		packageCache.Remove( ident );
	}

	/// <summary>
	/// Start tracking this path, associate it with this package.
	/// </summary>
	/// <returns>true if the file exists in the db, false if a newer version already exists</returns>
	public bool AddFile( string path, string crc, long size, Package package )
	{
		path = path.NormalizeFilename( true );

		if ( TryGetFile( path, out var existing ) )
		{
			if ( existing.Crc == crc && existing.Size == size )
			{
				// identical content, nothing to conflict over
				AddPackageLink( path, package );
				return true;
			}

			if ( existing.CreationDate > package.Revision.Created )
			{
				// this file is already installed from a newer package, don't overwrite it.
				Log.Warning( $"[{package.FullIdent}] WARNING cloud asset conflict! keeping the newer-published file: {path}" );

				AddPackageLink( path, package );
				return false;
			}
		}

		var file = new File
		{
			Path = path,
			Crc = crc,
			Size = size,
			CreationDate = package.Revision.Created,
			InstallDate = DateTime.UtcNow
		};

		db.BeginTrans();
		try
		{
			files.Upsert( file );
			db.Commit();

			filesByPath[path] = file;
		}
		catch
		{
			db.Rollback();
			throw;
		}

		AddPackageLink( path, package );
		return true;
	}

	/// <summary>
	/// Create an association between an existing file and a package.
	/// </summary>
	public void AddPackageLink( string path, Package package )
	{
		path = path.NormalizeFilename( true );

		if ( linksByPath.TryGetValue( path, out var existingLinks ) )
		{
			// avoid touching the db if there'e already a link between this file and package
			if ( existingLinks.Any( x => x.Package == package.FullIdent && x.Revision == package.Revision.VersionId ) )
				return;
		}

		var link = new PackageFile
		{
			Path = path,
			Package = package.FullIdent,
			Revision = package.Revision.VersionId,
			RevisionCreated = package.Revision.Created,
			IsPrimaryAsset = string.Equals( package.PrimaryAsset?.NormalizeFilename( true ), path, StringComparison.OrdinalIgnoreCase )
		};

		db.BeginTrans();
		try
		{
			links.Insert( link );

			db.Commit();

			if ( linksByPath.TryGetValue( path, out var byPath ) )
				byPath.RemoveAll( l => l.Package == package.FullIdent && l.Revision == package.Revision.VersionId );

			if ( linksByPackage.TryGetValue( (package.FullIdent, package.Revision.VersionId), out var byPackage ) )
				byPackage.RemoveAll( l => string.Equals( l.Path, path, StringComparison.OrdinalIgnoreCase ) );

			AddLinkToCache( link );
		}
		catch
		{
			db.Rollback();
			throw;
		}
	}

	public IEnumerable<string> GetPackageFiles( Package package )
	{
		if ( !linksByPackage.TryGetValue( (package.FullIdent, package.Revision.VersionId), out var linkList ) )
			return Enumerable.Empty<string>();

		return linkList.Select( l => l.Path ).ToList();
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
	/// Validate files on disk match their db entries. If a file is missing or has the wrong size, remove it from the db.
	/// </summary>
	void ValidateFiles()
	{
		Log.Info( "Validating cloud assets" );

		var badFiles = new ConcurrentBag<string>();
		files.FindAll().ToArray().AsParallel().ForAll( file =>
		{
			if ( !FileSystem.Cloud.FileExists( file.Path ) || FileSystem.Cloud.FileSize( file.Path ) != file.Size )
				badFiles.Add( file.Path );
		} );

		if ( badFiles.IsEmpty )
			return;

		var toRemove = new HashSet<string>( badFiles, StringComparer.OrdinalIgnoreCase );
		files.DeleteMany( x => toRemove.Contains( x.Path ) );
		Log.Info( $"..removed {toRemove.Count} entries" );
	}

	/// <summary>
	/// Validate that the files in this package are all present and correct.
	/// </summary>
	internal bool ValidatePackage( Package package )
	{
		if ( !linksByPackage.TryGetValue( (package.FullIdent, package.Revision.VersionId), out var linkList ) )
			return true;

		return linkList.AsParallel().All( link =>
		{
			if ( !filesByPath.TryGetValue( link.Path, out var f ) )
				return false; // link to nonexistent file?

			if ( !FileSystem.Cloud.FileExists( f.Path ) )
				return false;

			if ( FileSystem.Cloud.FileSize( f.Path ) != f.Size )
				return false;

			// crc?

			return true;
		} );
	}

	/// <summary>
	/// Used by the Asset to determine its package.
	/// We pass in abs and relative path because it gives us a shortcut for a fast reject - and the strings are already prepared for us.
	/// If multiple packages claim this path, we'll try to get the one this is the primary asset of first. If you want all of them, use <see cref="FindPackages(string, string)"/>.
	/// </summary>
	internal Package FindPackage( string absolutePath, string relativePath )
	{
		absolutePath = absolutePath.NormalizeFilename( true );
		relativePath = relativePath.NormalizeFilename( true );

		if ( !absolutePath.Contains( ".sbox/cloud/" ) )
			return null;

		if ( !linksByPath.TryGetValue( relativePath, out var linkList ) || linkList.Count == 0 )
		{
			Log.Warning( $"Was unable to determine package for {relativePath}.. Absolute path: {absolutePath}" );

			try
			{
				System.IO.File.Delete( absolutePath );
			}
			catch { } // ignore errors, we'll try again next time

			return null;
		}

		// prefer primary asset links here, that's the one we really want in that case
		foreach ( var link in linkList.OrderByDescending( x => x.IsPrimaryAsset ) )
		{
			var package = FindPackage( link.Package );
			if ( package != null )
				return package;
		}

		return null;
	}

	/// <summary>
	/// Same as <see cref="FindPackage(string, string)"/> but returns every package that currently claims this path.
	/// </summary>
	internal IEnumerable<Package> FindPackages( string absolutePath, string relativePath )
	{
		absolutePath = absolutePath.NormalizeFilename( true );
		relativePath = relativePath.NormalizeFilename( true );

		if ( !absolutePath.Contains( ".sbox/cloud/" ) )
			return Enumerable.Empty<Package>();

		if ( !linksByPath.TryGetValue( relativePath, out var linkList ) )
			return Enumerable.Empty<Package>();

		return linkList
			.Select( l => FindPackage( l.Package ) )
			.Where( p => p != null )
			.ToList();
	}

	internal bool TryGetFile( string path, out File file )
	{
		return filesByPath.TryGetValue( path.NormalizeFilename( true ), out file );
	}

	internal List<Package> GetPackages()
	{
		return packageCache.Values.ToList();
	}
}
