using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Zio;
using WatcherChangeTypes = System.IO.WatcherChangeTypes;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// A physical filesystem whose watchers are FSEvents streams rather than <see cref="System.IO.FileSystemWatcher"/>.
/// .NET's watcher on macOS is FSEvents underneath too, but it hands each stream to a shared run loop
/// thread and waits on it, and that costs 50-100 ms per watcher. The editor starts about fifty at
/// boot, one for every mounted folder - five seconds of the main thread doing nothing. A stream on a
/// dispatch queue starts in well under a millisecond.
/// </summary>
internal class FSEventsPhysicalFileSystem : PhysicalFileSystem
{
	protected override IFileSystemWatcher WatchImpl( UPath path )
	{
		if ( !OperatingSystem.IsMacOS() )
			return base.WatchImpl( path );

		return new FSEventsWatcher( this, path );
	}
}

/// <summary>
/// One FSEvents stream over a folder, delivering every path that changes under it - the raw
/// kernel view, symlinks resolved - with the event's flags. Shared by the Zio watcher and
/// <see cref="FolderWatcher"/>.
/// </summary>
internal sealed unsafe class FSEventStream : IDisposable
{
	const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
	const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
	const string LibSystem = "/usr/lib/libSystem.B.dylib";

	[DllImport( CoreServices )] static extern IntPtr FSEventStreamCreate( IntPtr allocator, IntPtr callback, StreamContext* context, IntPtr pathsToWatch, ulong sinceWhen, double latency, uint flags );
	[DllImport( CoreServices )] static extern void FSEventStreamSetDispatchQueue( IntPtr stream, IntPtr queue );
	[DllImport( CoreServices )] static extern byte FSEventStreamStart( IntPtr stream );
	[DllImport( CoreServices )] static extern void FSEventStreamStop( IntPtr stream );
	[DllImport( CoreServices )] static extern void FSEventStreamInvalidate( IntPtr stream );
	[DllImport( CoreServices )] static extern void FSEventStreamRelease( IntPtr stream );
	[DllImport( CoreFoundation )] static extern IntPtr CFStringCreateWithCString( IntPtr allocator, string cString, uint encoding );
	[DllImport( CoreFoundation )] static extern IntPtr CFArrayCreate( IntPtr allocator, IntPtr* values, nint count, IntPtr callbacks );
	[DllImport( CoreFoundation )] static extern void CFRelease( IntPtr cf );
	[DllImport( LibSystem )] static extern IntPtr dispatch_queue_create( string label, IntPtr attr );
	[DllImport( LibSystem )] static extern IntPtr realpath( string path, byte* resolved );

	[StructLayout( LayoutKind.Sequential )]
	struct StreamContext
	{
		public nint Version;
		public IntPtr Info;
		public IntPtr Retain;
		public IntPtr Release;
		public IntPtr CopyDescription;
	}

	const uint kCFStringEncodingUTF8 = 0x08000100;
	const ulong kFSEventStreamEventIdSinceNow = 0xFFFFFFFFFFFFFFFF;

	const uint kFSEventStreamCreateFlagNoDefer = 0x02;
	const uint kFSEventStreamCreateFlagFileEvents = 0x10;

	public const uint FlagMustScanSubDirs = 0x01;
	public const uint FlagRootChanged = 0x20;
	public const uint FlagItemCreated = 0x100;
	public const uint FlagItemRemoved = 0x200;
	public const uint FlagItemRenamed = 0x800;
	public const uint FlagItemIsDir = 0x20000;

	/// <summary>
	/// Events for every stream arrive here, in order. One queue is plenty - the handlers just
	/// note which paths changed.
	/// </summary>
	static readonly IntPtr queue = dispatch_queue_create( "sbox.fsevents", IntPtr.Zero );

	/// <summary>
	/// The folder as asked for, without a trailing slash.
	/// </summary>
	public string Path { get; }

	// What FSEvents will call it - events come back with symlinks resolved, so a watch on
	// /tmp/x reports /private/tmp/x
	readonly string realPath;
	readonly Action<string, uint> onEvent;

	IntPtr stream;
	GCHandle self;

	/// <summary>
	/// Start watching <paramref name="path"/> and everything under it. Each event is handed
	/// to <paramref name="onEvent"/> as a path spelled under <paramref name="path"/> and the
	/// kernel's flags for it, on a background queue.
	/// </summary>
	public FSEventStream( string path, Action<string, uint> onEvent )
	{
		Path = path.TrimEnd( '/' );
		realPath = ResolveRealPath( Path );
		this.onEvent = onEvent;

		self = GCHandle.Alloc( this );

		var context = new StreamContext { Info = GCHandle.ToIntPtr( self ) };
		var cfPath = CFStringCreateWithCString( IntPtr.Zero, Path, kCFStringEncodingUTF8 );
		var cfPaths = CFArrayCreate( IntPtr.Zero, &cfPath, 1, IntPtr.Zero );

		try
		{
			// A little latency lets a burst of writes to one file arrive as one event
			stream = FSEventStreamCreate( IntPtr.Zero, (IntPtr)(delegate* unmanaged< IntPtr, IntPtr, nuint, IntPtr, uint*, ulong*, void >)&OnEvents,
				&context, cfPaths, kFSEventStreamEventIdSinceNow, 0.05, kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagNoDefer );
		}
		finally
		{
			// The stream copied what it needs
			CFRelease( cfPaths );
			CFRelease( cfPath );
		}

		if ( stream == IntPtr.Zero )
		{
			self.Free();
			throw new IOException( $"FSEventStreamCreate failed for {Path}" );
		}

		FSEventStreamSetDispatchQueue( stream, queue );

		if ( FSEventStreamStart( stream ) == 0 )
		{
			FSEventStreamInvalidate( stream );
			FSEventStreamRelease( stream );
			stream = IntPtr.Zero;
			self.Free();
			throw new IOException( $"FSEventStreamStart failed for {Path}" );
		}
	}

	public void Dispose()
	{
		if ( stream == IntPtr.Zero ) return;

		FSEventStreamStop( stream );
		FSEventStreamInvalidate( stream );
		FSEventStreamRelease( stream );
		stream = IntPtr.Zero;

		// Nothing can call back once the stream is invalidated, so the handle can go
		self.Free();
	}

	[UnmanagedCallersOnly]
	static void OnEvents( IntPtr streamRef, IntPtr info, nuint count, IntPtr paths, uint* flags, ulong* ids )
	{
		if ( GCHandle.FromIntPtr( info ).Target is not FSEventStream stream )
			return;

		var cStrings = (IntPtr*)paths;

		for ( nuint i = 0; i < count; i++ )
		{
			try
			{
				var path = Marshal.PtrToStringUTF8( cStrings[i] );
				if ( path is not null ) stream.Deliver( path, flags[i] );
			}
			catch ( Exception )
			{
				// A handler's problem is not a reason to lose the rest of the batch, and
				// nothing may escape an unmanaged callback
			}
		}
	}

	void Deliver( string path, uint flags )
	{
		// Back from what the kernel calls it to what we were asked to watch
		if ( realPath != Path && path.StartsWith( realPath, StringComparison.Ordinal ) )
		{
			path = string.Concat( Path, path.AsSpan( realPath.Length ) );
		}

		if ( !path.StartsWith( Path, StringComparison.Ordinal ) )
			return;

		onEvent( path, flags );
	}

	/// <summary>
	/// What kind of change this event is. A rename comes as one event per end with the same
	/// flag, so the disk is asked which end this is.
	/// </summary>
	public static WatcherChangeTypes ChangeTypeOf( string path, uint flags )
	{
		// The kernel dropped events for a folder - all anyone can do is look at it again
		if ( (flags & (FlagMustScanSubDirs | FlagRootChanged)) != 0 )
			return WatcherChangeTypes.Changed;

		if ( (flags & FlagItemRemoved) != 0 )
			return WatcherChangeTypes.Deleted;

		if ( (flags & FlagItemCreated) != 0 )
			return WatcherChangeTypes.Created;

		if ( (flags & FlagItemRenamed) != 0 )
			return File.Exists( path ) || Directory.Exists( path ) ? WatcherChangeTypes.Created : WatcherChangeTypes.Deleted;

		return WatcherChangeTypes.Changed;
	}

	static string ResolveRealPath( string path )
	{
		var buffer = stackalloc byte[4096];
		var result = realpath( path, buffer );
		if ( result == IntPtr.Zero ) return path;

		return Marshal.PtrToStringUTF8( result ) ?? path;
	}
}

/// <summary>
/// Zio's watcher over one folder of an <see cref="FSEventsPhysicalFileSystem"/>.
/// </summary>
internal sealed class FSEventsWatcher : Zio.FileSystems.FileSystemWatcher
{
	readonly string nativePath;
	FSEventStream stream;
	bool enabled;

	public FSEventsWatcher( FSEventsPhysicalFileSystem fileSystem, UPath path ) : base( fileSystem, path )
	{
		nativePath = fileSystem.ConvertPathToInternal( path );
	}

	public override bool EnableRaisingEvents
	{
		get => enabled;
		set
		{
			if ( value == enabled ) return;
			enabled = value;

			if ( value )
			{
				stream = new FSEventStream( nativePath, OnEvent );
			}
			else
			{
				stream?.Dispose();
				stream = null;
			}
		}
	}

	protected override void Dispose( bool disposing )
	{
		EnableRaisingEvents = false;

		base.Dispose( disposing );
	}

	void OnEvent( string path, uint flags )
	{
		try
		{
			var changeType = FSEventStream.ChangeTypeOf( path, flags );
			var fullPath = FileSystem.ConvertPathFromInternal( path );

			switch ( changeType )
			{
				case WatcherChangeTypes.Created: RaiseCreated( new FileChangedEventArgs( FileSystem, Zio.WatcherChangeTypes.Created, fullPath ) ); break;
				case WatcherChangeTypes.Deleted: RaiseDeleted( new FileChangedEventArgs( FileSystem, Zio.WatcherChangeTypes.Deleted, fullPath ) ); break;
				default: RaiseChanged( new FileChangedEventArgs( FileSystem, Zio.WatcherChangeTypes.Changed, fullPath ) ); break;
			}
		}
		catch ( Exception e )
		{
			RaiseError( new FileSystemErrorEventArgs( e ) );
		}
	}
}
