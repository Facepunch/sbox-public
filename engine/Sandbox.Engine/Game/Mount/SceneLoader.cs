namespace Sandbox.Mounting;

/// <summary>
/// Base class for a mount loader that produces a <see cref="SceneFile"/> built in code.
/// Override <see cref="BuildScene"/> and create GameObjects - they become part of the
/// mounted scene. Registration and teardown are handled for you.
/// </summary>
public abstract class SceneLoader<T> : ResourceLoader<T> where T : BaseGameMount
{
	SceneFile _scene;

	protected sealed override object Load()
	{
		var scene = new Scene();
		scene.SetDeterministicId( (Path ?? string.Empty).ToGuid() );

		using ( scene.Push() )
		{
			BuildScene();
		}

		_scene = GetOrRegister( Path );
		scene.ToSceneFile( _scene );
		scene.Destroy();

		_scene.PostLoadInternal();

		return _scene;
	}

	/// <summary>
	/// Build the scene contents. Any GameObjects created here become part of the mounted scene.
	/// </summary>
	protected abstract void BuildScene();

	protected sealed override void Shutdown()
	{
		_scene?.DestroyInternal();
		_scene = null;
	}

	static SceneFile GetOrRegister( string path )
	{
		var resourcePath = path ?? string.Empty;
		var fixedPath = Resource.FixPath( resourcePath );

		var scene = Game.Resources.GetByIdLong<SceneFile>( fixedPath.FastHash64() );
		if ( scene is not null )
			return scene;

		scene = new SceneFile();
		scene.Register( resourcePath );
		return scene;
	}
}
