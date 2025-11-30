namespace Sandbox;

// TODO - requires camera component
/// <summary>
/// Applies a cubemap fog effect to the camera
/// </summary>
[Expose]
[Title( "Cubemap Fog" )]
[Category( "Post Processing" )]
[Icon( "foggy" )]
public class CubemapFog : Component
{
	[Property]
	public Material Sky { get; set; } = null;

	[Property, Range( 0, 1 )]
	public float Blur { get; set; } = 0.5f;

	[Property]
	public float StartDistance { get; set; } = 10.0f;

	[Property]
	public float EndDistance { get; set; } = 4096.0f;

	[Property]
	public float FalloffExponent { get; set; } = 1.0f;

	[Property]
	public float HeightWidth { get; set; } = 0.0f;

	[Property]
	public float HeightStart { get; set; } = 2000.0f;

	[Property]
	public float HeightExponent { get; set; } = 2.0f;

	[Property]
	public Color Tint { get; set; } = Color.White;

	internal void SetupCamera( CameraComponent camera, SceneCamera sceneCamera )
	{
		// garry: I don't like making them select a texture, so I try to get the texture
		// from a skybox material.
		var tex = Sky?.GetTexture( "g_tSkyTexture" );
		var tint = Tint;

		// Take it straight from the skybox if it's null
		if ( tex is null )
		{
			SkyBox2D skybox = null;
			foreach ( var s in Scene.GetAllComponents<SkyBox2D>() )
			{
				skybox = s;
				break;
			}

			if ( skybox.IsValid() && !skybox.Tags.HasAny( camera.RenderExcludeTags ) )
			{
				tex = skybox.SkyMaterial?.GetTexture( "g_tSkyTexture" );
				tint *= skybox.Tint;
			}
		}

		sceneCamera.CubemapFog.Enabled = tex is not null;
		sceneCamera.CubemapFog.Texture = tex;
		sceneCamera.CubemapFog.StartDistance = StartDistance;
		sceneCamera.CubemapFog.EndDistance = EndDistance;
		sceneCamera.CubemapFog.FalloffExponent = FalloffExponent;
		sceneCamera.CubemapFog.LodBias = 1 - Blur;
		sceneCamera.CubemapFog.HeightStart = HeightStart;
		sceneCamera.CubemapFog.HeightWidth = HeightWidth;
		sceneCamera.CubemapFog.HeightExponent = HeightExponent;
		sceneCamera.CubemapFog.Transform = WorldTransform.WithRotation( Transform.World.Rotation.Conjugate ).WithScale( 1 );
		sceneCamera.CubemapFog.Tint = tint;
	}

}
