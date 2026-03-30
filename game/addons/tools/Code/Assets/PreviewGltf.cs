
namespace Editor.Assets;

[AssetPreview( "gltf" )]
[AssetPreview( "glb" )]
class PreviewGltf : AssetPreview
{
	public override float PreviewWidgetCycleSpeed => 0.2f;

	public PreviewGltf( Asset asset ) : base( asset )
	{
	}

	public override async Task InitializeAsset()
	{
		await Task.Yield();

		var absolutePath = Asset.AbsolutePath;
		if ( string.IsNullOrWhiteSpace( absolutePath ) )
			return;

		using ( Scene.Push() )
		using ( EditorUtility.DisableTextureStreaming() )
		{
			var model = GltfImporter.ImportToRuntimeModel( absolutePath );
			if ( model is null )
				return;

			if ( model.MeshCount == 0 )
				return;

			SceneCenter = model.RenderBounds.Center;
			SceneSize = Vector3.Zero;

			PrimaryObject = new GameObject( true, "preview gltf" );
			PrimaryObject.WorldTransform = Transform.Zero;

			var modelRenderer = PrimaryObject.AddComponent<ModelRenderer>();
			modelRenderer.Model = model;

			SceneSize = model.RenderBounds.Size;
			SceneCenter = modelRenderer.WorldRotation * SceneCenter;
		}
	}
}
