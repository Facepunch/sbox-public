using Sandbox.SceneRenderer;

namespace Sandbox.SceneLab;

/// <summary>
/// The same world, built for the native scenesystem - every mesh object becomes a
/// <see cref="SceneModel"/>, every light a native light - and a <see cref="SceneCamera"/> to render it
/// from the managed view's camera. Scene Lab uses it to compare the two renderers' pixels (parity)
/// and their cost (the Renderer menu, and -benchmark).
/// </summary>
internal sealed class NativeScene : IDisposable
{
	readonly SceneWorld sceneWorld = new();
	readonly SceneCamera camera = new( "Scene Lab native" );
	readonly List<(LightObject Managed, SceneLight Native)> lights = new();

	public NativeScene( RenderWorld world, IReadOnlyDictionary<RenderMesh, Model> models, bool shadows )
	{
		var lighting = world.Lighting;
		sceneWorld.AmbientLightColor = lighting.AmbientColor;
		sceneWorld.GradientFog = lighting.GradientFog;

		var sunColor = lighting.SunColor;
		if ( sunColor.r > 0 || sunColor.g > 0 || sunColor.b > 0 )
		{
			// Shadows always on, with no cascades for none: native only fills the sun's color and
			// direction in when its shadows are enabled (ShadowMapper.FindOrCreateDirectionalShadowMaps
			// returns first)
			_ = new SceneDirectionalLight( sceneWorld, Rotation.LookAt( lighting.SunDirection ), sunColor )
			{
				ShadowsEnabled = true,
				ShadowCascadeCount = shadows && lighting.SunShadows ? lighting.SunShadowCascades : 0,
				ShadowCascadeSplitRatio = lighting.SunShadowSplitRatio,
				ShadowHardness = lighting.SunShadowHardness,
				ShadowBias = lighting.SunShadowBias,
			};
		}

		foreach ( var obj in world.Objects )
		{
			switch ( obj )
			{
				case MeshObject mesh when models.TryGetValue( mesh.Mesh, out var model ):
					// The tint's alpha is native's alpha fade too: under 1, opaque models dither and translucent ones blend more
					var sceneModel = new SceneModel( sceneWorld, model, mesh.Transform ) { Flags = { CastShadows = mesh.CastShadows }, ColorTint = mesh.Tint };
					if ( mesh.MaterialOverride is not null ) sceneModel.SetMaterialOverride( mesh.MaterialOverride );

					// A posed skinned model: the same bones, set as its render bones, then its bounds worked out from
					// them - without evaluating an animation, which would overwrite them
					if ( !mesh.Bones.IsEmpty )
					{
						for ( int b = 0; b < mesh.Bones.Length && b < model.BoneCount; b++ )
							sceneModel.SetBoneWorldTransform( b, mesh.Bones[b] );
						sceneModel.animNative.FinishUpdate();
					}
					break;

				case EnvMapObject envMap:
					_ = new SceneCubemap( sceneWorld, envMap.Cubemap, envMap.ProjectionBounds, envMap.Transform, envMap.Tint, envMap.Feathering, (int)SceneCubemap.ProjectionMode.Box )
					{
						Priority = envMap.Priority,
					};
					break;

				case LightObject { Kind: LightObject.LightKind.Point } light:
					lights.Add( (light, new ScenePointLight( sceneWorld, light.Transform.Position, light.Radius, light.Color )
					{
						QuadraticAttenuation = light.Attenuation,
						ShadowsEnabled = shadows && light.CastShadows,
						ShadowHardness = light.ShadowHardness,
					}) );
					break;

				case LightObject { Kind: LightObject.LightKind.Spot } light:
					lights.Add( (light, new SceneSpotLight( sceneWorld, light.Transform.Position, light.Color )
					{
						Rotation = light.Transform.Rotation,
						Radius = light.Radius,
						QuadraticAttenuation = light.Attenuation,
						ConeInner = light.ConeInner,
						ConeOuter = light.ConeOuter,
						ShadowsEnabled = shadows && light.CastShadows,
						ShadowHardness = light.ShadowHardness,
					}) );
					break;
			}
		}

		if ( lighting.SkyMaterial is not null )
		{
			_ = new SceneSkyBox( sceneWorld, lighting.SkyMaterial )
			{
				SkyTint = lighting.SkyTint,
				Transform = new Transform( Vector3.Zero, lighting.SkyRotation ),
				FogParams = lighting.SkyFog,
			};
		}

		camera.World = sceneWorld;

		var fog = lighting.CubemapFog;
		camera.CubemapFog.Enabled = fog.Enabled;
		camera.CubemapFog.Texture = fog.Texture;
		camera.CubemapFog.StartDistance = fog.StartDistance;
		camera.CubemapFog.EndDistance = fog.EndDistance;
		camera.CubemapFog.FalloffExponent = fog.FalloffExponent;
		camera.CubemapFog.LodBias = fog.LodBias;
		camera.CubemapFog.HeightWidth = fog.HeightWidth;
		camera.CubemapFog.HeightStart = fog.HeightStart;
		camera.CubemapFog.HeightExponent = fog.HeightExponent;
		camera.CubemapFog.Tint = fog.Tint;
		camera.CubemapFog.Transform = fog.Transform;
		camera.ClearFlags = ClearFlags.All;
		camera.EnablePostProcessing = false;
	}

	/// <summary>
	/// Point the camera where the managed view is looking, and move the lights to where the managed
	/// ones are now.
	/// </summary>
	void Sync( RenderView view, Vector2 size )
	{
		foreach ( var (managed, native) in lights )
		{
			native.Position = managed.Transform.Position;
			native.Rotation = managed.Transform.Rotation;
		}

		camera.Position = view.Position;
		camera.Rotation = view.Rotation;
		camera.FieldOfView = view.FieldOfView;
		camera.ZNear = view.ZNear;
		camera.ZFar = view.ZFar;
		camera.BackgroundColor = view.ClearColor;

		var viewport = view.Viewport;
		camera.Rect = new Rect( viewport.Left / size.x, viewport.Top / size.y, viewport.Width / size.x, viewport.Height / size.y );
	}

	/// <summary>
	/// Render into a swap chain, the way a native game camera does.
	/// </summary>
	public void Render( SwapChainHandle_t swapChain, RenderView view, Vector2 size )
	{
		Sync( view, size );
		camera.AddToRenderList( swapChain, size );
	}

	/// <summary>
	/// Render into a bitmap, save it to <paramref name="path"/> and return its pixels.
	/// </summary>
	public Color32[] Capture( RenderView view, Vector2 size, string path )
	{
		Sync( view, size );

		using var bitmap = new Bitmap( (int)size.x, (int)size.y );
		camera.RenderToBitmap( bitmap );
		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
		return bitmap.GetPixels32();
	}

	/// <summary>
	/// Start rendering into a linear HDR texture with <paramref name="msaa"/>, the way a native camera renders
	/// to a texture: an RGBA16F scratch target at the camera's <c>msaa</c> attribute, resolved into the texture
	/// by the final copy (<c>CCameraRenderer::RenderToTexture</c>). The render is only queued - it happens with
	/// the frame's other views - so read the texture back a frame or two later.
	/// </summary>
	public Texture RenderToTexture( RenderView view, Vector2 size, NativeEngine.RenderMultisampleType msaa )
	{
		Sync( view, size );

		var texture = Texture.CreateRenderTarget().WithSize( (int)size.x, (int)size.y ).WithFormat( ImageFormat.RGBA16161616F ).Create( "Scene Lab native reference" );
		camera.Attributes.Set( "msaa", (int)msaa );
		camera.RenderToTexture( texture, null, default );
		return texture;
	}

	public void Dispose()
	{
		camera.Dispose();
		sceneWorld.Delete();
	}
}

