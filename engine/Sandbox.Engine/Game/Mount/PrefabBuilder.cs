using System.Text.Json.Nodes;

namespace Sandbox.Mounting;

/// <summary>
/// A scoped builder for creating prefabs within a Mount.
/// Typically used inside a <see cref="Mounting.ResourceLoader.Load"/> implementation.
/// </summary>
public sealed class PrefabBuilder
{
	string _name;
	Scene _scene;
	Dictionary<string, string> _metadata;

	/// <summary>
	/// Set the name/resource path of the resulting prefab.
	/// The root <see cref="GameObject"/> name is derived from the filename portion if left unchanged.
	/// </summary>
	public PrefabBuilder WithName( string name )
	{
		_name = name;
		return this;
	}

	/// <summary>
	/// Set a metadata value on the resulting prefab, such as its "Title", "Description" or "Group", read back with
	/// <see cref="PrefabFile.GetMetadata"/>. It wins over the same key from a metadata component in the prefab,
	/// such as <see cref="SceneInformation"/>. A null value takes back one set earlier.
	/// </summary>
	public PrefabBuilder WithMetadata( string key, string value )
	{
		if ( string.IsNullOrEmpty( key ) )
			return this;

		_metadata ??= new();

		if ( value is null )
			_metadata.Remove( key );
		else
			_metadata[key] = value;

		return this;
	}

	/// <summary>
	/// Enter a temporary scene scope. GameObjects created inside will become part of this prefab.
	/// </summary>
	public PrefabBuildScope Scope()
	{
		_scene = new Scene();
		return new PrefabBuildScope( _scene );
	}

	/// <summary>
	/// Serialize the scene content into a registered <see cref="PrefabFile"/>.
	/// Call after you've created any objects and the <see cref="Scope"/> has been disposed.
	/// </summary>
	public PrefabFile Create()
	{
		if ( _scene is null )
			throw new InvalidOperationException( "Call Scope() before Create()" );

		// A prefab can be built from inside another object's deserialize, when it is loaded on demand. Nothing of
		// the build may wait for that operation's callbacks: the prefab is handed back whole.
		var running = CallbackBatch.Detach();

		try
		{
			return CreateDetached();
		}
		finally
		{
			CallbackBatch.Reattach( running );
		}
	}

	PrefabFile CreateDetached()
	{
		var root = _scene.Children.FirstOrDefault();
		if ( root is null )
		{
			Log.Warning( $"PrefabBuilder: no root GameObject found for '{_name}'" );
			_scene.Destroy();
			_scene = null;
			return null;
		}

		JsonObject rootJson;
		JsonObject properties;

		using ( _scene.Push() )
		{
			rootJson = root.Serialize( new GameObject.SerializeOptions() );
			properties = _scene.SerializeProperties();
		}

		_scene.Destroy();
		_scene = null;

		if ( rootJson is null )
		{
			Log.Warning( $"PrefabBuilder: failed to serialize '{_name}'" );
			return null;
		}

		SceneUtility.MakeIdGuidsUnique( rootJson );

		// What a saved prefab carries beside its root, where its metadata is read from. Those never hold system overrides.
		properties.Remove( "GameObjectSystems" );

		if ( _metadata is { Count: > 0 } )
		{
			if ( properties["Metadata"] is not JsonObject metadata )
				properties["Metadata"] = metadata = new JsonObject();

			foreach ( var (key, value) in _metadata )
				metadata[key] = value;
		}

		rootJson["__properties"] = properties;

		// Reuse an existing entry if the path was previously registered (like a re-mount)
		var resourcePath = _name ?? string.Empty;
		var fixedPath = Resource.FixPath( resourcePath );
		var prefab = Game.Resources.GetByIdLong<PrefabFile>( fixedPath.FastHash64() );

		if ( prefab is null )
		{
			prefab = new PrefabFile();
			prefab.Register( resourcePath );
		}
		else
		{
			prefab.CachedScene?.DestroyInternal();
			prefab.CachedScene = null;
		}

		prefab.RootObject = rootJson;
		prefab.PostLoadInternal();

		return prefab;
	}

	/// <summary>
	/// Unregister and destroy a <see cref="PrefabFile"/> created by <see cref="Create"/>.
	/// Call from <see cref="Mounting.ResourceLoader.Shutdown"/> when a mount is disabled.
	/// </summary>
	public static void Destroy( PrefabFile prefab )
	{
		prefab?.DestroyInternal();
	}
}

/// <summary>
/// Disposable scope that manages a temporary scene for <see cref="PrefabBuilder"/>.
/// </summary>
public readonly struct PrefabBuildScope : IDisposable
{
	readonly IDisposable _push;
	readonly CallbackBatch _running;

	internal PrefabBuildScope( Scene scene )
	{
		// The build runs as it does at the top level, whatever it was triggered from: each step's callbacks,
		// the properties of what it reads from text among them, have run by the time the step returns.
		_running = CallbackBatch.Detach();
		_push = scene.Push();
	}

	public void Dispose()
	{
		if ( _push is null )
			return;

		_push.Dispose();
		CallbackBatch.Reattach( _running );
	}
}
