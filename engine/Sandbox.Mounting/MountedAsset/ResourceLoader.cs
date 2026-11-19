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

	internal BaseGameMount Mount { get; private set; }

	/// <summary>
	/// The type of resource this file can provide.
	/// </summary>
	public ResourceType Type { get; private set; }

	internal bool IsShutdown { get; private set; }
	internal event Action ShutdownActions;

	static Dictionary<ResourceType, string> extensions = new()
	{
		{ ResourceType.Model, ".vmdl" },
		{ ResourceType.Scene, ".scene" },
		{ ResourceType.Texture, ".vtex" },
		{ ResourceType.Sound, ".vsnd" },
		{ ResourceType.Material, ".vmat" },
		{ ResourceType.PrefabFile, ".prefab" },
		{ ResourceType.AnimationGraph, ".ag" },
		{ ResourceType.AnimationSkeleton, ".skel" },
		{ ResourceType.AnimationClip, ".anim" },
	};

	/// <summary>
	/// The path to the asset
	/// </summary>
	public string Path { get; private set; }

	public string RelativePath { get; private set; }

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

	/// <summary>
	/// The loader this one was resolved from, when its resource is derived from another
	/// resource instead of having a file of its own.
	/// </summary>
	public ResourceLoader Parent { get; private set; }

	public ResourceLoader()
	{
	}

	/// <summary>
	/// Called for a path below this loader's that nothing is registered at, so a resource can offer
	/// others derived from it, such as the clips inside a model, without listing them while mounting.
	/// The returned loader is registered at that path, with this loader as its <see cref="Parent"/>.
	/// It can be called from any thread, and more than once for a path when threads race: only the first
	/// result is kept, the others are shut down. Return a new loader each time.
	/// </summary>
	/// <param name="name">The path below this loader's, including its extension.</param>
	/// <param name="type">The type of resource the returned loader creates.</param>
	protected virtual ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		return null;
	}

	/// <summary>
	/// Resolve a child of this loader, set up but not registered. Its path is exactly this loader's
	/// plus <paramref name="name"/>, the path it was asked for, so the next lookup finds it.
	/// </summary>
	internal ResourceLoader ResolveChildInternal( string name )
	{
		var child = ResolveChild( name, out var type );
		if ( child is null || child.Mount is not null )
			return null;

		child.Parent = this;
		child.Setup( type, $"{RelativePath}/{name}", Mount );
		return child;
	}

	internal void InitializeInternal( ResourceType type, string path, BaseGameMount mount )
	{
		// Standardize the path into a mount path
		path = path.Replace( '\\', '/' ).Trim( '/' );

		// Force an engine specific extension, if it isn't already set
		if ( extensions.TryGetValue( type, out var extension ) && !path.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) )
			path += extension;

		Setup( type, path, mount );
		Mount.RegisterFileInternal( this );
	}

	void Setup( ResourceType type, string path, BaseGameMount mount )
	{
		Mount = mount;
		IsShutdown = false;
		RelativePath = path;
		Path = $"mount://{Mount.Ident}/{RelativePath}";
		Type = type;
		Name = System.IO.Path.GetFileNameWithoutExtension( path );
	}

	object _cachedResult;
	Lock _lock = new();
	Task<object> _loadTask;

	internal void InvalidateCachedResult( object result )
	{
		lock ( _lock )
		{
			if ( ReferenceEquals( _cachedResult, result ) )
				_cachedResult = null;
		}
	}

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

		object result = _cachedResult;
		if ( _cachedResult is not null && (_cachedResult is not IValid v || v.IsValid) )
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

				result = _loadTask.Result;
				if ( Type != ResourceType.GameResource )
					_cachedResult = result;
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

		return result;
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
		IsShutdown = true;
		try
		{
			Shutdown();
		}
		finally
		{
			var actions = ShutdownActions;
			ShutdownActions = null;
			actions?.Invoke();
		}
	}

	protected virtual void Shutdown()
	{

	}
}


public abstract class ResourceLoader<T> : ResourceLoader where T : BaseGameMount
{
	public T Host => Mount as T;
}
