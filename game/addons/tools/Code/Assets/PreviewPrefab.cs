namespace Editor.Assets;

[AssetPreview( "prefab" )]
class PreviewPrefab : AssetPreview
{
	public override float PreviewWidgetCycleSpeed => 0.2f;

	public override bool UsePixelEvaluatorForThumbs => true;

	public PreviewPrefab( Asset asset ) : base( asset )
	{

	}

	/// <summary>
	/// Only initialize if we're in the live preview, so we don't load a whole scene when showing a thumbnail
	/// </summary>
	public override Task InitializeAsset()
	{
		if ( !IsRenderingThumbnail )
			return Task.CompletedTask;

		return LoadPrefabContent();
	}

	public override Widget CreateWidget( Widget parent )
	{
		return new ThumbnailPreviewWidget( this, LoadPrefabContent );
	}

	internal Task LoadPrefabContent()
	{
		using ( EditorUtility.DisableTextureStreaming() )
		{
			var pf = Asset.LoadResource<PrefabFile>();
			if ( pf is null ) return Task.CompletedTask;

			using ( Scene.Push() )
			{
				PrimaryObject = GameObject.Clone( pf );
				PrimaryObject.WorldPosition = 0;
				SceneCenter = PrimaryObject.GetBounds().Center;
				SceneSize = PrimaryObject.GetBounds().Size;

				TryAddDefaultLighting( Scene );
			}
		}

		return Task.CompletedTask;
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
