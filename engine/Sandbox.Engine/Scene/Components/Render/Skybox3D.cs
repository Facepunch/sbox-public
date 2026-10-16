namespace Sandbox;

/// <summary>
/// Renders a scene as a 3D skybox, positioned and scaled by this GameObject.
/// </summary>
[Title( "3D Skybox" )]
[Category( "Rendering" )]
[Icon( "visibility" )]
public class Skybox3D : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Scene rendered as the 3D skybox.
	/// </summary>
	[Property, Title( "Scene" )]
	public SceneFile SkyboxScene
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Active )
				RebuildSkybox();
		}
	}

	Scene skyboxScene;
	SceneSkybox3D sceneSkybox;

	protected override void OnEnabled()
	{
		Transform.OnTransformChanged += UpdateTransform;
		RebuildSkybox();
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= UpdateTransform;
		DestroySkybox();
	}

	void RebuildSkybox()
	{
		DestroySkybox();
		if ( !SkyboxScene.IsValid() ) return;
		if ( Scene.SceneWorld.InternalSkyboxWorlds.Any( x => x.IsValid ) )
		{
			Log.Warning( $"{GameObject.Name}: the scene already has an active 3D skybox." );
			return;
		}

		try
		{
			skyboxScene = Scene.IsEditor ? Scene.CreateEditorScene() : new Scene();
			skyboxScene.Source = SkyboxScene;
			using ( skyboxScene.Push() )
			using ( SceneNetworkSystem.SuppressSpawnMessages() )
			// Keep skybox callbacks out of the parent scene's load batch.
			using ( CallbackBatch.Isolated() )
			{
				var options = new SceneLoadOptions { IsAdditive = true, ShowLoadingScreen = false };
				options.SetScene( SkyboxScene );
				if ( !skyboxScene.Load( options ) )
				{
					Log.Error( $"{GameObject.Name}: could not load skybox scene '{SkyboxScene.ResourcePath}'." );
					DestroySkybox();
					return;
				}
				skyboxScene.WantsSystemScene = false;
				if ( skyboxScene.GetComponentsInChildren<Skybox3D>( true ).Any() )
				{
					Log.Error( $"{GameObject.Name}: skybox scene '{SkyboxScene.ResourcePath}' contains a nested Skybox3D component." );
					DestroySkybox();
					return;
				}
			}

			sceneSkybox = new SceneSkybox3D( Scene.SceneWorld, skyboxScene.SceneWorld );
			UpdateTransform();
		}
		finally
		{
			if ( sceneSkybox is null )
				DestroySkybox();
		}
	}

	void DestroySkybox()
	{
		var skybox = sceneSkybox;
		var content = skyboxScene;
		sceneSkybox = null;
		skyboxScene = null;
		if ( skybox?.ParentWorld.IsValid() == true && skybox.ParentWorld.InternalSkyboxWorlds.Contains( skybox ) )
			skybox.Delete();
		content?.Destroy();
	}

	protected override void OnUpdate()
	{
		if ( !skyboxScene.IsValid() ) return;
		using ( skyboxScene.Push() )
		{
			if ( skyboxScene.IsEditor )
				skyboxScene.EditorTick( Time.Now, Time.Delta );
			else
				skyboxScene.GameTick( Time.Delta );
		}
	}

	void UpdateTransform()
	{
		if ( sceneSkybox is null ) return;
		var scale = WorldScale.x;
		scale = float.IsFinite( scale ) && scale > 0 ? scale : 1;
		var position = WorldPosition.IsFinite ? WorldPosition : Vector3.Zero;
		var transform = new Transform( position, Rotation.Identity, scale );
		if ( WorldTransform != transform )
			WorldTransform = transform;
		sceneSkybox.Origin = -position / scale;
		sceneSkybox.Scale = scale;
		sceneSkybox.Update();
	}
}
