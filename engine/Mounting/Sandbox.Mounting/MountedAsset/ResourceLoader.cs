using Sandbox.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Mounting;

/// <summary>
/// A class responsible for loading a single resource. It will cache the result inside.
/// </summary>
public abstract class ResourceLoader
{
	protected static readonly Logger Log = new Logger( "ResourceLoader" );

	internal BaseGameMount _mount;

	/// <summary>
	/// The type of resource this file can provide.
	/// </summary>
	public ResourceType Type { get; private set; }

	static Dictionary<ResourceType, string> extensions = new()
	{
		{ ResourceType.Model, ".vmdl" },
		{ ResourceType.Scene, ".scene" },
		{ ResourceType.Texture, ".vtex" },
		{ ResourceType.Sound, ".vsnd" },
		{ ResourceType.Material, ".vmat" },
	};

	/// <summary>
	/// The path to the asset
	/// </summary>
	public string Path { get; private set; }

	/// <summary>
	/// The filename of the asset, without extension
	/// </summary>
	public string Name { get; private set; }

	/// <summary>
	/// General tags. Anything you want to do that doesn't fit into the flags.
	/// </summary>
	public HashSet<string> Tags { get; } = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Flags allow customization of how this resource is treated by the engine.
	/// </summary>
	public ResourceFlags Flags { get; set; }

	/// <summary>
	/// The folder in which this resource resides
	/// </summary>
	public ResourceFolder Folder { get; internal set; }

	public ResourceLoader()
	{
	}

	internal void InitializeInternal( ResourceType type, string path, BaseGameMount mount )
	{
		_mount = mount;

		// Standardize the path into a mount path
		path = path.Replace( '\\', '/' ).Trim( '/' );
		path = $"mount://{_mount.Ident}/{path}";

		// Force an engine specific extension, if it isn't already set
		if ( extensions.TryGetValue( type, out var extension ) )
		{
			if ( !path.EndsWith( extension ) )
				path += extension;
		}

		Path = path;
		Type = type;
		Name = System.IO.Path.GetFileNameWithoutExtension( path );

		_mount.RegisterFileInternal( this );
	}

	object _cachedResult;
	Lock _lock = new();
	Task<object> _loadTask;

	/// <summary>
	/// Should be implemented to load a specific type
	/// </summary>
	public async Task<object> GetOrCreate()
	{
		if ( _loadTask is not null && !_loadTask.IsCompleted )
		{
			await _loadTask;
		}

		_lock.Enter();

		if ( _cachedResult is not null )
		{
			_lock.Exit();
			return _cachedResult;
		}

		try
		{
			_loadTask = LoadAsync();

			if ( _loadTask is not null )
			{
				await _loadTask;

				_cachedResult = _loadTask.Result;
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when loading '{Path}'" );
		}
		finally
		{
			_lock.Exit();
		}

		_loadTask = default;

		return _cachedResult;
	}

	/// <summary>
	/// Should be implemented to load a specific type
	/// </summary>
	protected virtual Task<object> LoadAsync()
	{
		return Task.FromResult( Load() );
	}

	/// <summary>
	/// Should be implemented to load a specific type
	/// </summary>
	protected virtual object Load()
	{
		return null;
	}

	internal void ShutdownInternal()
	{
		Shutdown();
	}

	protected virtual void Shutdown()
	{

	}
}


public abstract class ResourceLoader<T> : ResourceLoader where T : BaseGameMount
{
	public T Host => _mount as T;
}
