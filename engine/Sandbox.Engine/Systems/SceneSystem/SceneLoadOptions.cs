namespace Sandbox;

public class SceneLoadOptions
{
	SceneFile scene;
	SceneFile runtimeScene;

	internal void FinishRuntimePreparation() => runtimeScene = null;

	internal IDisposable RuntimePreparationScope() => Sandbox.Utility.DisposeAction.Create( FinishRuntimePreparation );

	internal bool PrepareRuntime()
	{
		var prepared = runtimeScene ?? scene;
		if ( runtimeScene is null && prepared is not null && SceneFile.ResolveRuntimeScene is { } resolve )
			prepared = resolve( prepared );

		if ( !prepared.IsValid() )
		{
			FinishRuntimePreparation();
			Log.Error( "No valid runtime Scene was found in SceneLoadOptions." );
			return false;
		}

		if ( !string.IsNullOrEmpty( prepared.CompileError ) )
		{
			FinishRuntimePreparation();
			Log.Error( prepared.CompileError );
			return false;
		}

		runtimeScene = prepared;
		return true;
	}

	/// <summary>
	/// Internal property to mark this scene as being a system scene. It should only be set in
	/// <see cref="Scene.AddSystemScene"/>.
	/// </summary>
	internal bool IsSystemScene { get; set; }

	public bool ShowLoadingScreen { get; set; } = true;
	public bool IsAdditive { get; set; } = false;

	/// <summary>
	/// If true, on load we'll even delete objects that are marked as DontDelete
	/// </summary>
	public bool DeleteEverything { get; set; } = false;
	public Transform Offset { get; set; } = Transform.Zero;

	public SceneFile GetSceneFile() => runtimeScene ?? scene;

	public bool SetScene( SceneFile sceneFile )
	{
		scene = sceneFile;
		FinishRuntimePreparation();
		return true;
	}

	public bool SetScene( string sceneFileName )
	{
		var file = SceneFile.Load( sceneFileName );
		if ( file is null )
		{
			Log.Warning( $"LoadFromFile: Couldn't find {sceneFileName}" );
			return false;
		}

		SetScene( file );
		return true;
	}
}
