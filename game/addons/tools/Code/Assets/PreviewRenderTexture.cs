using Sandbox;

namespace Editor.Assets;

[AssetPreview( "rtex" )]
class PreviewRenderTexture : AssetPreview
{
	private RenderTextureAsset renderTexture;

	public PreviewRenderTexture( Asset asset ) : base( asset )
	{
		if ( asset.TryLoadResource<RenderTextureAsset>( out var resource ) )
		{
			renderTexture = resource;
		}
	}

	public override Task InitializeAsset()
	{
		if ( renderTexture?.Texture is null )
		{
			return Task.CompletedTask;
		}

		using ( Scene.Push() )
		{
			PrimaryObject = new GameObject();
			PrimaryObject.WorldTransform = Transform.Zero;

			var sprite = PrimaryObject.AddComponent<SpriteRenderer>();
			sprite.Sprite = new Sprite()
			{
				Animations =
				[
					new Sprite.Animation
					{
						Name = "Default",
						Frames = [ new Sprite.Frame { Texture = renderTexture.Texture } ]
					}
				]
			};
			sprite.Size = new Vector2( 16, 16 );

			Camera.Orthographic = true;
			Camera.OrthographicHeight = 16;
		}

		return Task.CompletedTask;
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		base.UpdateScene( cycle, timeStep );

		Camera.Orthographic = true;
		Camera.OrthographicHeight = 16;
		Camera.WorldPosition = Vector3.Forward * -200;
		Camera.WorldRotation = Rotation.LookAt( Vector3.Forward );
	}
}
