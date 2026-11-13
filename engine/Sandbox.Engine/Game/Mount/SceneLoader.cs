using Sandbox.Engine;

namespace Sandbox.Mounting;

/// <summary>
/// Base class for a mount loader that produces a <see cref="SceneFile"/> built in code.
/// Override <see cref="BuildScene"/> and create GameObjects - they become part of the
/// mounted scene. Registration and teardown are handled for you.
/// </summary>
public abstract class SceneLoader<T> : ResourceLoader<T>, IThumbnailProvider where T : BaseGameMount
{
	public Texture Thumbnail
	{
		get
		{
			if ( !_thumbnail.IsValid() )
			{
				_thumbnail = GetThumbnail()
					?? Texture.LoadFromFileSystem( $"/thumbs/{Host.Ident}/{RelativePath.WithExtension( ".png" )}", GlobalContext.Menu.FileMount, false )
					?? Texture.Invalid;
			}

			if ( ReferenceEquals( _thumbnail, Texture.Invalid ) )
				return null;

			return _thumbnail;
		}
	}

	SceneFile _scene;
	Texture _thumbnail;

	protected sealed override object Load()
	{
		var scene = new Scene();
		scene.SetDeterministicId( (Path ?? string.Empty).ToGuid() );

		SceneFile file = null;
		try
		{
			using ( scene.Push() )
			// Finish build callbacks before destroying the temporary scene.
			using ( CallbackBatch.Isolated() )
			{
				BuildScene();
			}

			file = GetOrRegister( Path );
			file.OnDestroyed = OnSceneUnloaded;
			_scene = file;
			scene.ToSceneFile( file );
			scene.Destroy();

			file.PostLoadInternal();

			return file;
		}
		catch
		{
			if ( scene.IsValid() )
				scene.Destroy();

			// Destroying the registration calls OnSceneUnloaded
			if ( file is null )
				OnSceneUnloaded();
			else
				file.DestroyInternal();
			throw;
		}
	}

	/// <summary>
	/// Build the scene contents. Any GameObjects created here become part of the mounted scene.
	/// </summary>
	protected abstract void BuildScene();

	/// <summary>
	/// Load or create the thumbnail image for this scene, the result is cached.
	/// </summary>
	protected virtual Texture GetThumbnail() { return null; }

	/// <summary>
	/// Called when the built scene is destroyed or its build failed, the next load builds it again.
	/// Release anything kept alive for it here.
	/// </summary>
	protected virtual void OnSceneUnloaded() { }

	/// <summary>
	/// Every live scene, editor scenes included, to check whether something is still in use before releasing it.
	/// </summary>
	protected static IEnumerable<Scene> LiveScenes => Scene.AllLive;

	protected sealed override void Shutdown()
	{
		_scene?.DestroyInternal();
		_scene = null;

		if ( !ReferenceEquals( _thumbnail, Texture.Invalid ) )
			_thumbnail?.Destroy();
		_thumbnail = null;
	}

	static SceneFile GetOrRegister( string path )
	{
		var resourcePath = path ?? string.Empty;
		var fixedPath = Resource.FixPath( resourcePath );

		var scene = Game.Resources.GetByIdLong<SceneFile>( fixedPath.FastHash64() );
		if ( scene is not null && scene.IsValid )
			return scene;

		scene = new SceneFile();
		scene.Register( resourcePath );
		return scene;
	}
}
