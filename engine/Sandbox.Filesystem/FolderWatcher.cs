using System.IO;

namespace Sandbox;

/// <summary>
/// Watches a folder on disk for changes - the shape of <see cref="FileSystemWatcher"/>, with the
/// same events, but cheap to start on macOS. There .NET's watcher hands every stream to a shared
/// run loop thread and waits on it, 50-100 ms each; this one is an FSEvents stream on a dispatch
/// queue and starts in well under a millisecond. Elsewhere it's <see cref="FileSystemWatcher"/>.
/// <para>
/// FSEvents doesn't pair the two ends of a rename, so on macOS a rename arrives as
/// <see cref="Deleted"/> for the old path and <see cref="Created"/> for the new one, never
/// <see cref="Renamed"/>.
/// </para>
/// </summary>
public sealed class FolderWatcher : IDisposable
{
	public event FileSystemEventHandler Changed;
	public event FileSystemEventHandler Created;
	public event FileSystemEventHandler Deleted;
	public event RenamedEventHandler Renamed;

	string path;
	bool includeSubdirectories;
	bool enabled;

	FileSystemWatcher stock;
	FSEventStream stream;

	public FolderWatcher()
	{
	}

	public FolderWatcher( string path )
	{
		Path = path;
	}

	/// <summary>
	/// The folder to watch. Changing it while enabled moves the watch.
	/// </summary>
	public string Path
	{
		get => path;
		set
		{
			if ( path == value ) return;
			path = value;

			if ( stock is not null ) stock.Path = value;
			if ( stream is not null ) Restart();
		}
	}

	/// <summary>
	/// Whether changes anywhere under the folder count, or only its direct contents.
	/// </summary>
	public bool IncludeSubdirectories
	{
		get => includeSubdirectories;
		set
		{
			includeSubdirectories = value;
			if ( stock is not null ) stock.IncludeSubdirectories = value;
		}
	}

	public bool EnableRaisingEvents
	{
		get => enabled;
		set
		{
			if ( enabled == value ) return;
			enabled = value;

			if ( value ) Start();
			else Stop();
		}
	}

	void Start()
	{
		if ( string.IsNullOrEmpty( path ) )
			throw new InvalidOperationException( "FolderWatcher needs a Path before it can be enabled" );

		if ( OperatingSystem.IsMacOS() )
		{
			stream = new FSEventStream( path, OnEvent );
			return;
		}

		stock = new FileSystemWatcher( path ) { IncludeSubdirectories = includeSubdirectories };
		stock.Changed += ( s, e ) => Changed?.Invoke( this, e );
		stock.Created += ( s, e ) => Created?.Invoke( this, e );
		stock.Deleted += ( s, e ) => Deleted?.Invoke( this, e );
		stock.Renamed += ( s, e ) => Renamed?.Invoke( this, e );
		stock.EnableRaisingEvents = true;
	}

	void Stop()
	{
		stream?.Dispose();
		stream = null;

		stock?.Dispose();
		stock = null;
	}

	void Restart()
	{
		Stop();
		Start();
	}

	void OnEvent( string fullPath, uint flags )
	{
		// The stream is always recursive - drop what's deeper than asked for
		if ( !includeSubdirectories && !string.Equals( System.IO.Path.GetDirectoryName( fullPath ), stream.Path, StringComparison.Ordinal ) )
			return;

		var changeType = FSEventStream.ChangeTypeOf( fullPath, flags );
		var args = new FileSystemEventArgs( changeType, System.IO.Path.GetDirectoryName( fullPath ) ?? stream.Path, System.IO.Path.GetFileName( fullPath ) );

		switch ( changeType )
		{
			case WatcherChangeTypes.Created: Created?.Invoke( this, args ); break;
			case WatcherChangeTypes.Deleted: Deleted?.Invoke( this, args ); break;
			default: Changed?.Invoke( this, args ); break;
		}
	}

	public void Dispose()
	{
		enabled = false;
		Stop();
	}
}
