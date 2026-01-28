using System.IO;
using System.Text.Json;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// A filesystem. Could be on disk, or in memory, or in the cloud. Could be writable or read only.
/// Or it could be an aggregation of all those things, merged together and read only.
/// </summary>
public class BaseFileSystem
{
	internal static JsonSerializerOptions JsonSerializerOptions { get; set; }

	protected Zio.IFileSystem system;
	protected Zio.IFileSystemWatcher watcher;

	internal List<FileWatch> watchers { get; } = new List<FileWatch>();
	internal HashSet<string> changedFiles { get; } = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

	internal BaseFileSystem( Zio.IFileSystem system )
	{
		this.system = system;
	}

	internal BaseFileSystem()
	{
	}

	/// <inheritdoc cref="IValid.IsValid"/>
	public bool IsValid => system != null;

	/// <summary>
	/// Returns true if this filesystem is read only
	/// </summary>
	public bool IsReadOnly => system is ReadOnlyFileSystem;

	internal bool WatchEnabled = true;
	internal bool PendingDispose = false;
	internal bool TraceChanges = false;

	internal virtual void Dispose()
	{
		lock ( FileWatch.WithChanges )
		{
			system?.Dispose();
			system = null;

			watcher?.Dispose();
			watcher = null;

			foreach ( var watcherSbox in watchers.ToArray() ) watcherSbox.Dispose();
			watchers.Clear();

			changedFiles.Clear();
		}
	}

	/// <summary>
	/// Get a list of directories
	/// </summary>
	public IEnumerable<string> FindDirectory( string folder, string pattern = "*", bool recursive = false )
	{
		folder = FixPath( folder );

		foreach ( var path in system.EnumeratePaths( folder, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly, Zio.SearchTarget.Directory ) )
		{
			yield return path.FullName.Substring( folder.Length ).Trim( '/' );
		}
	}

	/// <summary>
	/// Unoptimal, for debugging purposes - don't expose
	/// </summary>
	internal int FileCount => FindFile( "/", recursive: true ).ToArray().Length;

	/// <summary>
	/// Get a list of files
	/// </summary>
	public IEnumerable<string> FindFile( string folder, string pattern = "*", bool recursive = false )
	{
		folder = FixPath( folder );

		List<string> foundFiles = new();

		try
		{
			foreach ( var path in system.EnumeratePaths( folder, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly, Zio.SearchTarget.File ) )
			{
				var found = path.FullName.Substring( folder.Length ).Trim( '/' );
				foundFiles.Add( found );
			}
		}
		catch ( System.IO.DirectoryNotFoundException ) { }// If directory not found, doesn't matter

		return foundFiles;
	}

	/// <summary>
	/// Delete a folder and optionally all of its contents
	/// </summary>
	public void DeleteDirectory( string folder, bool recursive = false )
	{
		system.DeleteDirectory( FixPath( folder ), recursive );
	}


	/// <summary>
	/// Delete a file
	/// </summary>
	public void DeleteFile( string path )
	{
		system.DeleteFile( FixPath( path ) );
	}

	/// <summary>
	/// Create a directory - or a tree of directories.
	/// Returns silently if the directory already exists.
	/// </summary>
	/// <param name="folder"></param>
	public void CreateDirectory( string folder )
	{
		if ( string.IsNullOrWhiteSpace( folder ) )
			return;

		system.CreateDirectory( FixPath( folder ) );
	}

	/// <summary>
	/// Returns true if the file exists on this filesystem
	/// </summary>
	public bool FileExists( string path )
	{
		ArgumentNullException.ThrowIfNullOrEmpty( path, "path" );
		Assert.NotNull( system );

		if ( path.Contains( ":" ) ) return false;

		return system.FileExists( FixPath( path ) );
	}

	/// <summary>
	///  Returns true if the directory exists on this filesystem
	/// </summary>
	public bool DirectoryExists( string path ) => system.DirectoryExists( FixPath( path ) );

	/// <summary>
	/// Returns the full physical path to a file or folder on disk,
	/// or null if it isn't on disk.
	/// </summary>
	public string GetFullPath( string path )
	{
		if ( path.Contains( ":" ) )
			return path;

		if ( system is CasefoldSubFileSystem sfs )
		{
			return sfs.ConvertPathToInternal( FixPath( path ) );
		}

		if ( system is Zio.FileSystems.AggregateFileSystem afs )
		{
			// This probably isn't optimal
			var entry = afs.FindFirstFileSystemEntry( FixPath( path ) );
			if ( entry == null ) return null;

			return entry?.FileSystem.ConvertPathToInternal( entry.Path );
		}

		return null;
	}

	static string GetRelativePath( Zio.IFileSystem system, string path )
	{
		if ( system is CasefoldSubFileSystem sfs )
		{
			try
			{
				return sfs.ConvertPathFromInternal( path ).FullName;
			}
			catch ( System.ArgumentException ) // System.ArgumentException: Path `C:/git/sbox-boomer/code/UI/Info.razor` must be absolute (Parameter 'path')
			{
				return null;
			}
		}

		if ( system is Zio.FileSystems.AggregateFileSystem afs )
		{
			foreach ( var e in afs.GetFileSystems() )
			{
				try
				{
					var a = GetRelativePath( e, path );
					if ( a is not null )
						return a;
				}
				catch ( System.InvalidOperationException )
				{
					continue;
				}
				catch ( System.ArgumentException ) // System.ArgumentException: Path must be absolute (Parameter 'path')
				{
					continue;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Returns the relative path
	/// </summary>
	internal string GetRelativePath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;
		return GetRelativePath( system, path.ToLowerInvariant() );
	}

	/// <summary>
	/// Write the contents to the path. The file will be over-written if the file exists
	/// </summary>
	public void WriteAllText( string path, string contents )
	{
		using ( var stream = OpenWrite( path ) )
		using ( var writer = new System.IO.StreamWriter( stream ) )
		{
			writer.Write( contents );
		}
	}

	/// <summary>
	/// Write the contents to the path. The file will be over-written if the file exists
	/// </summary>
	public void WriteAllBytes( string path, byte[] contents )
	{
		using ( var stream = OpenWrite( path ) )
		{
			stream.Write( contents, 0, contents.Length );
		}
	}

	/// <summary>
	/// Given a filename, create a path to it
	/// </summary>
	void CreatePathForFile( string filePath )
	{
		var directory = Path.GetDirectoryName( filePath );
		if ( string.IsNullOrEmpty( directory ) ) return;
		if ( DirectoryExists( directory ) ) return;

		CreateDirectory( directory );
	}

	/// <summary>
	/// Read the contents of path and return it as a string.
	/// Returns null if file not found.
	/// </summary>
	public string ReadAllText( string path )
	{
		if ( !FileExists( path ) )
			return null;

		using ( var f = OpenRead( path, FileMode.Open ) )
		using ( var r = new StreamReader( f, Encoding.UTF8, true ) )
		{
			return r.ReadToEnd();
		}
	}

	/// <summary>
	/// Read the contents of path and return it as a string
	/// </summary>
	public Span<byte> ReadAllBytes( string path )
	{
		using ( var f = OpenRead( path ) )
		{
			var bytes = new byte[f.Length];
			f.ReadExactly( bytes, 0, bytes.Length );
			return bytes;
		}
	}

	/// <summary>
	/// Read the contents of path and return it as a string
	/// </summary>
	public async Task<byte[]> ReadAllBytesAsync( string path )
	{
		using ( var f = OpenRead( path ) )
		{
			var bytes = new byte[f.Length];
			await f.ReadExactlyAsync( bytes, 0, bytes.Length );
			return bytes;
		}
	}

	/// <summary>
	/// Read the contents of path and return it as a string
	/// </summary>
	public async Task<string> ReadAllTextAsync( string path )
	{
		using ( var f = OpenRead( path ) )
		using ( var r = new StreamReader( f, Encoding.UTF8, true ) )
		{
			return await r.ReadToEndAsync();
		}
	}

	/// <summary>
	/// Create a sub-filesystem at the specified path
	/// </summary>
	public BaseFileSystem CreateSubSystem( string path )
	{
		// Log.Trace( $"CreateFileSystem( {path} ) [{GetFullPath(path)}]" );

		var sub = new CasefoldSubFileSystem( system, FixPath( path ), false );
		return new BaseFileSystem( sub );
	}



	/// <summary>
	/// Open a file for write. If the file exists we'll overwrite it (by default)
	/// </summary>
	public System.IO.Stream OpenWrite( string path, FileMode mode = FileMode.Create )
	{
		CreatePathForFile( path );

		return system.OpenFile( FixPath( path ), mode, FileAccess.Write );
	}

	/// <summary>
	/// Open a file for read. Will throw an exception if it doesn't exist.
	/// </summary>
	public System.IO.Stream OpenRead( string path, FileMode mode = FileMode.Open )
	{
		return system.OpenFile( FixPath( path ), mode, FileAccess.Read, FileShare.Read );
	}

	/// <summary>
	/// Read Json from a file using System.Text.Json.JsonSerializer. This will throw exceptions
	/// if not valid json.
	/// </summary>
	public T ReadJson<T>( string filename, T defaultValue = default )
	{
		var text = ReadAllText( filename );
		if ( string.IsNullOrWhiteSpace( text ) )
			return defaultValue;

		return System.Text.Json.JsonSerializer.Deserialize<T>( text, JsonSerializerOptions );
	}

	/// <summary>
	/// The same as ReadJson except will return a default value on missing/error.
	/// </summary>
	public T ReadJsonOrDefault<T>( string filename, T returnOnError = default )
	{
		try
		{
			return ReadJson<T>( filename, returnOnError );
		}
		catch ( System.Exception )
		{
			return returnOnError;
		}
	}

	/// <summary>
	/// Convert object to json and write it to the specified file
	/// </summary>
	public void WriteJson<T>( string filename, T data )
	{
		var text = System.Text.Json.JsonSerializer.Serialize( data, JsonSerializerOptions );
		WriteAllText( filename, text );
	}

	/// <summary>
	/// Gets the size in bytes of all the files in a directory
	/// </summary>
	public int DirectorySize( string path, bool recursive = false )
	{
		return (int)FindFile( path, recursive: recursive ).Sum( x => system.GetFileLength( Path.Combine( path, x ) ) );
	}

	internal FileWatch Watch( string pathglob = null )
	{
		watcher?.Dispose();
		watcher = null;

		if ( watcher == null )
		{
			watcher = system.Watch( "/" );
			watcher.NotifyFilter = Zio.NotifyFilters.Attributes | Zio.NotifyFilters.Size | Zio.NotifyFilters.CreationTime | Zio.NotifyFilters.LastWrite | Zio.NotifyFilters.FileName | Zio.NotifyFilters.DirectoryName | Zio.NotifyFilters.Security;
			watcher.IncludeSubdirectories = true;

			watcher.Changed += OnDirectoryContentsChanged;
			watcher.Deleted += OnDirectoryContentsChanged;
			watcher.Created += OnDirectoryContentsChanged;
			watcher.Renamed += OnDirectoryContentsRenamed;
			watcher.Error += OnDirectoryContentsError;

			watcher.EnableRaisingEvents = true;
		}

		FileWatch w = (pathglob != null) ? new FileWatch( this, pathglob ) : new FileWatch( this );

		watchers.Add( w );

		return w;
	}

	internal void RemoveWatcher( FileWatch watcher )
	{
		watchers?.Remove( watcher );
	}

	internal void AddChangedFile( string path )
	{
		if ( !WatchEnabled )
			return;

		path = path.ToLower();

		// Ignore common visual studio spam
		if ( path.EndsWith( ".tmp" ) || path.EndsWith( "~" ) )
			return;

		if ( path == "/accesslist.txt" )
			return;

		lock ( FileWatch.WithChanges )
		{
			if ( changedFiles.Contains( path ) )
				return;

			// Log.Info( $"File Changed [{path}]" );

			changedFiles.Add( path );

			if ( !FileWatch.WithChanges.Contains( this ) )
				FileWatch.WithChanges.Add( this );

			FileWatch.TimeSinceLastChange = 0;
		}
	}

	void OnDirectoryContentsChanged( object sender, Zio.FileChangedEventArgs e )
	{
		string path = e.FullPath.FullName;

		if ( TraceChanges )
		{
			Log.Trace( $"File [{e.ChangeType}] - {path} / {e.FullPath} / {e.Name}" );
		}

		AddChangedFile( path );
	}


	private static Stopwatch timeSinceActivity = Stopwatch.StartNew();

	void OnDirectoryContentsRenamed( object sender, Zio.FileRenamedEventArgs e )
	{
		string path = e.FullPath.FullName;
		string oldpath = e.OldFullPath.FullName;

		if ( TraceChanges )
		{
			Log.Trace( $"File [{e.ChangeType}] - {oldpath} -> {path}" );
		}

		AddChangedFile( path );
		AddChangedFile( oldpath );
	}

	private void OnDirectoryContentsError( object sender, Zio.FileSystemErrorEventArgs e )
	{
		if ( TraceChanges )
		{
			Log.Warning( $"File [Error] - {e.Exception}" );
		}
	}

	/// <summary>
	/// Returns CRC64 of the file contents.
	/// </summary>
	/// <param name="filepath">File path to the file to get CRC of.</param>
	/// <returns>The CRC64, or 0 if file is not found.</returns>
	public async Task<ulong> GetCrcAsync( string filepath )
	{
		try
		{
			using ( var s = OpenRead( filepath, FileMode.Open ) )
			{
				return await Sandbox.Utility.Crc64.FromStreamAsync( s );
			}
		}
		catch ( System.IO.FileNotFoundException )
		{
			return 0;
		}
	}

	/// <summary>
	/// Returns CRC64 of the file contents.
	/// </summary>
	/// <param name="filepath">File path to the file to get CRC of.</param>
	/// <returns>The CRC64, or 0 if file is not found.</returns>
	public ulong GetCrc( string filepath )
	{
		try
		{
			using ( var s = OpenRead( filepath, FileMode.Open ) )
			{
				return Sandbox.Utility.Crc64.FromStream( s );
			}
		}
		catch ( System.IO.FileNotFoundException )
		{
			return 0;
		}
	}

	/// <summary>
	/// Returns file size of given file.
	/// </summary>
	/// <param name="filepath">File path to the file to look up size of.</param>
	/// <returns>File size, in bytes.</returns>
	public long FileSize( string filepath )
	{
		return system.GetFileLength( FixPath( filepath ) );
	}

	/// <summary>
	/// Mount this path on the filesystem
	/// </summary>
	/// <param name="filesystem"></param>
	internal void Mount( BaseFileSystem filesystem )
	{
		if ( filesystem == null ) return;
		if ( filesystem.system == null ) return;

		if ( system is Zio.FileSystems.AggregateFileSystem fs )
		{
			if ( fs.GetFileSystems().Contains( filesystem.system ) )
				return;

			fs.AddFileSystem( filesystem.system );
		}
	}

	internal void UnMount( BaseFileSystem filesystem )
	{
		if ( filesystem == null ) return;
		if ( filesystem.system == null ) return;

		(system as Zio.FileSystems.AggregateFileSystem).RemoveFileSystem( filesystem.system );
	}

	/// <summary>
	/// Mount this path on the filesystem, so it's accessible in Mount
	/// </summary>
	internal BaseFileSystem CreateAndMount( BaseFileSystem system, string path )
	{
		var sub = system.CreateSubSystem( path );
		Mount( sub );
		return sub;
	}

	/// <summary>
	/// Mount this path on the filesystem, so it's accessible in Mount
	/// </summary>
	internal BaseFileSystem CreateAndMount( string path )
	{
		var sub = new LocalFileSystem( path );
		Mount( sub );
		return sub;
	}

	/// <summary>
	/// Zio wants good paths to start with '/' - so we add it here if it isn't already on
	/// </summary>
	internal static string FixPath( string path )
	{
		// Do not allow 0-32 ASCII stuff
		if ( path.Any( c => char.IsControl( c ) ) ) throw new ArgumentException( "Path cannot contain control characters!", "path" );

		if ( path.Length < 1 )
			return "/";

		if ( path[0] == '/' ) return path;
		return string.Concat( "/", path );
	}

	/// <summary>
	/// Lowercase the filename and replace \ with /
	/// </summary>
	internal static string NormalizeFilename( string filepath )
	{
		return filepath.ToLower().Replace( "\\", "/" ); // we can probably do more here
	}
}
