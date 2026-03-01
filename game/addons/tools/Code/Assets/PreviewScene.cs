namespace Editor.Assets;

[AssetPreview( "scene" )]
class PreviewScene : AssetPreview
{
	public override float PreviewWidgetCycleSpeed => 0.2f;

	public override bool UsePixelEvaluatorForThumbs => true;

	public override float VideoLength => 6.0f;

	Rotation baseRotation;

	public PreviewScene( Asset asset ) : base( asset )
	{

	}

	/// <summary>
	/// Only initialize if we're in the live preview, so we don't load a whole scene when showing a thumbnail
	/// </summary>
	public override async Task InitializeAsset()
	{
		if ( !IsRenderingThumbnail )
			return;

		await LoadSceneContent();
	}

	public override Widget CreateWidget( Widget parent )
	{
		return new ThumbnailPreviewWidget( this, LoadSceneContent );
	}

	internal async Task LoadSceneContent()
	{
		using ( EditorUtility.DisableTextureStreaming() )
		{
			var sf = Asset.LoadResource<SceneFile>();
			if ( sf is null ) return;



			using ( Scene.Push() )
			{
				Scene.Load( sf );
			}

			while ( Scene.IsLoading )
			{
				await Task.Delay( 100 );
			}

			using ( Scene.Push() )
			{
				if ( !Scene.Camera.IsValid() )
				{
					var camera = new GameObject( true, "camera" );
					var cc = camera.Components.Create<CameraComponent>();
					cc.FieldOfView = 40;
					cc.BackgroundColor = "#19181a";
					cc.ZFar = 100000;
					cc.ZNear = 1;
				}

				// tonemapping is gonna fuck us up
				foreach ( var x in Scene.GetAllComponents<Tonemapping>() )
				{
					x.Destroy();
				}

				Scene.Camera.FieldOfView = 60;

				if ( !Scene.Camera.Components.Get<Bloom>().IsValid() )
				{
					var bloom = Scene.Camera.Components.Create<Bloom>();
					bloom.Threshold = 0.2f;
					bloom.Strength = 0.2f;
				}

				SceneCenter = Scene.Camera.WorldPosition;
				baseRotation = Scene.Camera.WorldRotation;

				TryAddDefaultLighting( Scene );
			}
		}
	}

	float time = 0;

	public override void UpdateScene( float cycle, float timeStep )
	{
		time += timeStep;

		using ( Scene.Push() )
		{
			if ( IsRenderingVideo )
			{
				Camera.WorldPosition = SceneCenter + baseRotation.Right * (cycle).Remap( 0, 1, -1, 1 ) * 100;
				Camera.WorldRotation = baseRotation;
			}
			else
			{
				Camera.WorldPosition = SceneCenter + baseRotation.Right * MathF.Sin( cycle * 2.0f ) * 100;
				Camera.WorldRotation = baseRotation;
			}
		}

		base.UpdateScene( cycle, timeStep );
	}

	static void TryAddDefaultLighting( Scene scene )
	{
		if ( scene.Components.GetInDescendantsOrSelf<DirectionalLight>( true ).IsValid() ) return;
		if ( scene.Components.GetInDescendantsOrSelf<SpotLight>( true ).IsValid() ) return;
		if ( scene.Components.GetInDescendantsOrSelf<PointLight>( true ).IsValid() ) return;

		var go = scene.CreateObject();
		go.Name = "Directional Light";

		go.WorldRotation = Rotation.From( 90, 0, 0 );
		var light = go.Components.Create<DirectionalLight>();
		light.LightColor = Color.White;

		scene.SceneWorld.AmbientLightColor = "#557685";
	}
}
