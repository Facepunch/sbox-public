using Sandbox.Diagnostics;
using System.Collections.Generic;
using System.IO;
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
	/// Allows logging for this specific asset source
	/// </summary>
	protected Logger Log { get; init; }

	internal MountHost _host;

	public BaseGameMount()
	{
		Log = new Logger( Ident );
	}

	internal void InitializeInternal( MountHost system )
	{
		_host = system;
		Initialize( new InitializeContext( system ) );
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

		await Mount( new MountContext( this ) );

		foreach ( var entry in _entries.Values )
		{
			RootFolder.AddResource( entry );
		}
	}

	/// <summary>
	/// Try to mount. Should set Mounted to true if success.
	/// </summary>
	protected abstract Task Mount( MountContext context );


	internal void ShutdownInternal()
	{
		IsMounted = false;

		Shutdown();

		foreach ( var entry in _entries.Values )
		{
			entry?.ShutdownInternal();
		}

		_entries.Clear();
		_entriesByType.Clear();

		RootFolder = null;
	}

	/// <summary>
	/// Called on destroy, if you have any files open, now is the time to close them.
	/// </summary>
	protected virtual void Shutdown()
	{

	}

	readonly Dictionary<string, ResourceLoader> _entries = new Dictionary<string, ResourceLoader>( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<ResourceType, List<ResourceLoader>> _entriesByType = new();

	/// <summary>
	/// All of the resources in this game
	/// </summary>
	public IReadOnlyCollection<ResourceLoader> Resources => _entries.Values;

	/// <summary>
	/// Retrieves the resource loader associated with the specified path, if it exists.
	/// </summary>
	public ResourceLoader GetByPath( string path )
	{
		if ( path.EndsWith( "_c" ) ) path = path[..^2];
		return _entries.TryGetValue( path, out var entry ) ? entry : default;
	}

	/// <summary>
	/// Retrieves all resource loaders of a type
	/// </summary>
	public IReadOnlyCollection<ResourceLoader> GetAll( ResourceType type )
	{
		return _entriesByType.TryGetValue( type, out var entry ) ? entry : [];
	}

	public ResourceFolder RootFolder { get; internal set; }

	internal void RegisterFileInternal( ResourceLoader entry )
	{
		_entries[entry.Path] = entry;
		_entriesByType.GetOrCreate( entry.Type ).Add( entry );
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
