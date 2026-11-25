
namespace Editor.Assets;

[AssetPreview( "fbx" )]
[AssetPreview( "obj" )]
[AssetPreview( "dmx" )]
class PreviewMesh : AssetPreview
{
	public override float PreviewWidgetCycleSpeed => 0.2f;

	public PreviewMesh( Asset asset ) : base( asset )
	{
	}

	public override async Task InitializeAsset()
	{
		await Task.Yield();

		using ( Scene.Push() )
		using ( EditorUtility.DisableTextureStreaming() )
		{
			// TODO - make async
			var model = Asset.GetPreviewModel();
			if ( model is null )
				return;

			if ( model.MeshCount == 0 )
				return;

			SceneCenter = model.RenderBounds.Center;
			SceneSize = Vector3.Zero;

			PrimaryObject = new GameObject( true, "preview mesh" );
			PrimaryObject.WorldTransform = Transform.Zero;

			var modelRenderer = PrimaryObject.AddComponent<ModelRenderer>();
			modelRenderer.Model = model;

			SceneSize = model.RenderBounds.Size;
			SceneCenter = modelRenderer.WorldRotation * SceneCenter;
		}
	}
}
