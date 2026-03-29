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

		var root = _scene.Children.FirstOrDefault();
		if ( root is null )
		{
			Log.Warning( $"PrefabBuilder: no root GameObject found for '{_name}'" );
			_scene.Destroy();
			_scene = null;
			return null;
		}

		JsonObject rootJson;

		using ( _scene.Push() )
		{
			rootJson = root.Serialize( new GameObject.SerializeOptions() );
		}

		_scene.Destroy();
		_scene = null;

		if ( rootJson is null )
		{
			Log.Warning( $"PrefabBuilder: failed to serialize '{_name}'" );
			return null;
		}

		SceneUtility.MakeIdGuidsUnique( rootJson );

		// Reuse an existing entry if the path was previously registered (like a re-mount)
		var resourcePath = _name ?? string.Empty;
		var fixedPath = Resource.FixPath( resourcePath );
		var prefab = Game.Resources.GetByIdLong<PrefabFile>( fixedPath.FastHash64() );

		if ( prefab is null )
		{
			prefab = new PrefabFile();
			prefab.Register( resourcePath );
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

	internal PrefabBuildScope( Scene scene )
	{
		_push = scene.Push();
	}

	public void Dispose() => _push?.Dispose();
}
