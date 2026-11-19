using Sandbox.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Mounting;

/// <summary>
/// The base class for all mounts. Your mount should implement the abstract methods from this class.
/// </summary>
public abstract class BaseGameMount
{
	/// <summary>
	/// True if this source is installed on the system and can be mounted.
	/// </summary>
	public bool IsInstalled { get; protected set; }

	/// <summary>
	/// True if this is currently active and mounted
	/// </summary>
	public bool IsMounted { get; protected set; }

	/// <summary>
	/// True if this game is in your Steam library, whether or not it's installed. An installed
	/// game always counts as owned. False for a mount that doesn't name a Steam app and isn't
	/// installed, since there's nothing to ask about it.
	/// </summary>
	public bool IsOwned
	{
		get
		{
			if ( IsInstalled ) return true;
			if ( SteamAppId is not long appId ) return false;

			return _host?.Steam?.IsAppOwned( appId ) ?? false;
		}
	}

	/// <summary>
	/// A short, lowercase string that will be used to uniquely identify this asset source
	/// ie "rust"
	/// </summary>
	public abstract string Ident { get; }

	/// <summary>
	/// The display name of the game this mounts, ie "Rust"
	/// </summary>
	public abstract string Title { get; }

	/// <summary>
	/// The Steam app id this game belongs to, if any. Used to link to the store when it isn't installed.
	/// </summary>
	public virtual long? SteamAppId => null;

	/// <summary>
	/// Wide capsule art for this game, 460x215. Null if this mount doesn't name a Steam app.
	/// </summary>
	public virtual string CapsuleUrl => GetSteamImageUrl( "header.jpg" );

	/// <summary>
	/// Portrait library art for this game, 600x900. Null if this mount doesn't name a Steam app.
	/// </summary>
	public virtual string PortraitUrl => GetSteamImageUrl( "library_600x900.jpg" );

	/// <summary>
	/// Wide hero banner for this game, 1920x620. Null if this mount doesn't name a Steam app.
	/// </summary>
	public virtual string HeroUrl => GetSteamImageUrl( "library_hero.jpg" );

	/// <summary>
	/// This game's logo on transparency. Null if this mount doesn't name a Steam app.
	/// </summary>
	public virtual string LogoUrl => GetSteamImageUrl( "logo.png" );

	/// <summary>
	/// Steam's CDN serves these for any app id, whether or not the game is installed or owned,
	/// so a mount can show its game off before you have it. There's no square icon here - that
	/// one is keyed by a hash from the web API rather than by app id.
	/// </summary>
	string GetSteamImageUrl( string name )
	{
		if ( SteamAppId is not long appId ) return null;

		return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/{name}";
	}

	/// <summary>
	/// Allows logging for this specific asset source
	/// </summary>
	protected Logger Log { get; init; }

	internal MountHost _host;

	/// <summary>
	/// This mount's entry in mounts.json. Set before <see cref="Initialize"/>.
	/// </summary>
	internal MountSettings Config { get; set; } = new();

	/// <summary>
	/// This mount's generated thumbnails, laid out by resource relative path. Null if the mount
	/// didn't come from a project with a Mounts folder.
	/// </summary>
	internal BaseFileSystem ThumbsFileSystem { get; private set; }

	public BaseGameMount()
	{
		Log = new Logger( Ident );
	}

	/// <summary>
	/// Called when the mount is registered
	/// </summary>
	internal void Register( BaseFileSystem projectFileSystem )
	{
		if ( projectFileSystem is null ) return;

		var thumbsFolder = $"Mounts/{Ident}/thumbs/";
		projectFileSystem.CreateDirectory( thumbsFolder );
		ThumbsFileSystem = projectFileSystem.CreateSubSystem( thumbsFolder );
	}

	/// <summary>
	/// Called when the mount is removed for good. Undoes <see cref="Register"/>.
	/// </summary>
	internal void Unregister()
	{
		ThumbsFileSystem?.Dispose();
		ThumbsFileSystem = null;
	}

	internal void InitializeInternal( MountHost system )
	{
		_host = system;
		Initialize( new InitializeContext( system, this ) );
	}

	/// <summary>
	/// Called on startup, in parallel with other sources. Use this to check for the presence of the game on disk and
	/// set the IsInstalled property if it is.
	/// </summary>
	protected abstract void Initialize( InitializeContext context );


	internal async Task MountInternal()
	{
		RootFolder = ResourceFolder.CreateRoot();

		if ( !IsInstalled ) return;

		// Files are registered during Mount, before it sets IsMounted, until the mount shuts down
		lock ( _entriesLock )
		{
			_acceptingEntries = true;
		}

		await Mount( new MountContext( this ) );

		foreach ( var entry in Resources )
		{
			// Derived resources aren't files, they have no place in the folders
			if ( entry.Parent is null )
				RootFolder.AddResource( entry );
		}
	}

	/// <summary>
	/// Try to mount. Should set Mounted to true if success.
	/// </summary>
	protected abstract Task Mount( MountContext context );


	internal void ShutdownInternal()
	{
		// Under the lock, so a child resolved on another thread can't be published after this
		lock ( _entriesLock )
		{
			IsMounted = false;
			_acceptingEntries = false;
		}

		Shutdown();

		ResourceLoader[] entries;
		lock ( _entriesLock )
		{
			entries = _entries.Values.ToArray();
			_entries.Clear();
			_entriesByType.Clear();
		}

		foreach ( var entry in entries )
		{
			entry?.ShutdownInternal();
		}

		RootFolder = null;
	}

	/// <summary>
	/// Called on destroy, if you have any files open, now is the time to close them.
	/// </summary>
	protected virtual void Shutdown()
	{

	}

	// Guards the entries: registration, shutdown and publishing resolved children all take it
	readonly Lock _entriesLock = new();
	bool _acceptingEntries;
	readonly Dictionary<string, ResourceLoader> _entries = new Dictionary<string, ResourceLoader>( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<ResourceType, List<ResourceLoader>> _entriesByType = new();

	/// <summary>
	/// Paths longer than this are never looked up.
	/// </summary>
	const int MaxPathLength = 1024;

	/// <summary>
	/// All of the resources in this game
	/// </summary>
	public IReadOnlyCollection<ResourceLoader> Resources
	{
		get
		{
			lock ( _entriesLock ) return _entries.Values.ToArray();
		}
	}

	/// <summary>
	/// Retrieves the resource loader associated with the specified path, if it exists.
	/// </summary>
	public ResourceLoader GetByPath( string path )
	{
		if ( string.IsNullOrEmpty( path ) || path.Length > MaxPathLength )
			return default;

		var key = path.AsSpan();
		if ( key.EndsWith( "_c" ) ) key = key[..^2];

		var split = key.Length;

		while ( true )
		{
			ResourceLoader parent = null;

			lock ( _entriesLock )
			{
				var lookup = _entries.GetAlternateLookup<ReadOnlySpan<char>>();

				if ( split == key.Length && lookup.TryGetValue( key, out var entry ) )
					return entry;

				if ( !IsMounted )
					return default;

				// Nothing is registered here, but a resource further up the path might derive one
				for ( split = key[..split].LastIndexOf( '/' ); split > 0; split = key[..split].LastIndexOf( '/' ) )
				{
					if ( lookup.TryGetValue( key[..split], out parent ) )
						break;
				}
			}

			if ( parent is null )
				return default;

			var name = key[(split + 1)..];
			if ( !IsValidChildName( name ) )
				return default;

			// Outside the lock, a resolver may load or look up other resources of this mount
			var child = parent.ResolveChildInternal( name.ToString() );
			if ( child is not null )
				return PublishChild( child );
		}
	}

	/// <summary>
	/// A derived resource's name below its parent: no empty, "." or ".." segments and no backslashes,
	/// so it's registered at exactly the path it was asked for.
	/// </summary>
	static bool IsValidChildName( ReadOnlySpan<char> name )
	{
		if ( name.IsEmpty || name.Contains( '\\' ) )
			return false;

		foreach ( var range in name.Split( '/' ) )
		{
			var segment = name[range];
			if ( segment.IsEmpty || segment is "." || segment is ".." )
				return false;
		}

		return true;
	}

	/// <summary>
	/// Register a resolved child, unless another thread got there first or the mount has gone away since.
	/// </summary>
	ResourceLoader PublishChild( ResourceLoader child )
	{
		ResourceLoader published;

		lock ( _entriesLock )
		{
			if ( _entries.TryGetValue( child.Path, out published ) )
			{
				// Someone else published it first, ours is discarded below
			}
			else if ( IsMounted && _entries.TryGetValue( child.Parent.Path, out var parent ) && ReferenceEquals( parent, child.Parent ) )
			{
				AddEntry( child );
				return child;
			}
		}

		child.ShutdownInternal();
		return published;
	}

	/// <summary>
	/// Retrieves all resource loaders of a type
	/// </summary>
	public IReadOnlyCollection<ResourceLoader> GetAll( ResourceType type )
	{
		lock ( _entriesLock )
		{
			return _entriesByType.TryGetValue( type, out var entry ) ? entry.ToArray() : [];
		}
	}

	public ResourceFolder RootFolder { get; internal set; }

	internal void RegisterFileInternal( ResourceLoader entry )
	{
		var removed = new List<ResourceLoader>();

		lock ( _entriesLock )
		{
			if ( !_acceptingEntries )
			{
				// Mounting is over, this loader would outlive the mount
				removed.Add( entry );
			}
			else if ( _entries.TryGetValue( entry.Path, out var replaced ) )
			{
				if ( ReferenceEquals( replaced, entry ) )
					return;

				RemoveEntryAndChildren( replaced, removed );
				AddEntry( entry );
			}
			else
			{
				AddEntry( entry );
			}
		}

		foreach ( var loader in removed )
		{
			loader.ShutdownInternal();
		}
	}

	void AddEntry( ResourceLoader entry )
	{
		_entries[entry.Path] = entry;
		_entriesByType.GetOrCreate( entry.Type ).Add( entry );
	}

	/// <summary>
	/// Unregister a loader and every loader resolved from it, adding them to <paramref name="removed"/> to be shut down.
	/// </summary>
	void RemoveEntryAndChildren( ResourceLoader entry, List<ResourceLoader> removed )
	{
		_entries.Remove( entry.Path );
		_entriesByType.GetValueOrDefault( entry.Type )?.Remove( entry );
		removed.Add( entry );

		foreach ( var child in _entries.Values.Where( x => x.Parent == entry ).ToArray() )
		{
			RemoveEntryAndChildren( child, removed );
		}
	}

	/// <summary>
	/// Unmount and re-mount the source. Used during development to update the files.
	/// </summary>
	public async Task RefreshInternal()
	{
		// If we're not mounted, just initialize again, mount might be installed now.
		if ( IsMounted == false )
		{
			InitializeInternal( _host );
			return;
		}

		ShutdownInternal();
		IsInstalled = false;

		InitializeInternal( _host );
		await MountInternal();
	}
}
