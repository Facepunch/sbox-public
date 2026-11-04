using Editor.Assets;

namespace Editor.TerrainEditor;

[AssetPreview( "tmat" )]
class PreviewTerrainMaterial : AssetPreview
{
	public override float PreviewWidgetCycleSpeed => 0.1f;

	public PreviewTerrainMaterial( Asset asset ) : base( asset ) { }

	public override async Task InitializeAsset()
	{
		using ( EditorUtility.DisableTextureStreaming() )
		{
			if ( !Asset.TryLoadResource<TerrainMaterial>( out var material ) )
				return;

			UpdateLighting();

			using ( Scene.Push() )
			{
				PrimaryObject = new GameObject();

				var mr = PrimaryObject.AddComponent<ModelRenderer>();
				mr.Model = Model.Sphere;
				mr.MaterialOverride = Material.FromShader( "shaders/terrain_preview.shader" );
				mr.SceneObject.Attributes.Set( "BCR", material.BCRTexture );
				mr.SceneObject.Attributes.Set( "NHO", material.NHOTexture );

				// Same values the terrain feeds its GPU material buffer, so the preview
				// tiles and shades like the material does on a terrain
				mr.SceneObject.Attributes.Set( "UVScale", material.UVScale );
				mr.SceneObject.Attributes.Set( "NormalStrength", 1.0f / material.NormalStrength );
				mr.SceneObject.Attributes.Set( "Metalness", material.Metalness );
			}

			SceneSize = PrimaryObject.GetBounds().Size * 0.6f;
			SceneCenter = PrimaryObject.GetBounds().Center;
		}

		await Task.CompletedTask;
	}

	/// <summary>
	/// The default preview lighting suits shiny props. Terrain materials are dark, fully rough
	/// ground, so light them like open ground instead: a warm key, a cool rim for separation, and
	/// the reflection probe carrying the ambient. Levels are kept so that even a bright albedo
	/// stays off the ceiling - a clipped highlight is the one thing that makes a rough material
	/// read as wet.
	/// </summary>
	void UpdateLighting()
	{
		using var _ = Scene.Push();

		// Key light
		{
			var go = Scene.Directory.FindByName( "sun" )?.FirstOrDefault() ?? new GameObject( true, "sun" );
			var light = go.GetOrAddComponent<DirectionalLight>();
			light.WorldRotation = Rotation.From( 45, -180, 0 );
			light.LightColor = new Color( 1.0f, 0.95f, 0.85f ) * 0.7f;
			light.SkyColor = Color.Gray * 0.35f;
		}

		// Rim light for a bit of separation. Kept gentle - it's blue, and it's the thing that
		// pushes a saturated albedo's blue channel over the top.
		{
			var go = Scene.Directory.FindByName( "rim" )?.FirstOrDefault() ?? new GameObject( true, "rim" );
			var light = go.GetOrAddComponent<PointLight>();
			light.WorldPosition = new Vector3( -100, 40, 80 );
			light.LightColor = new Color( 0.4f, 0.6f, 1.0f ) * 0.6f;
			light.Radius = 800;
			light.Shadows = false;
		}

		// Ambient / reflections. default.vtex rather than the default2 studio cubemap: that one
		// has light sources bright enough that, on a fully rough surface, they still burn through
		// as speckles at any tint low enough to be worth having. This one has no such hotspots,
		// so it can be turned up and actually light the material.
		{
			var go = Scene.Directory.FindByName( "envmap" )?.FirstOrDefault() ?? new GameObject( true, "envmap" );
			var c = go.GetOrAddComponent<EnvmapProbe>();
			c.WorldPosition = Vector3.Zero;
			c.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
			c.Texture = Texture.Load( "textures/cubemaps/default.vtex" );
			c.TintColor = Color.White * 2.0f;
			c.Bounds = BBox.FromPositionAndSize( 0, 100000 );
		}

		// A probe replaces the sky fill rather than adding to it, so top the diffuse back up
		{
			var go = Scene.Directory.FindByName( "ambient" )?.FirstOrDefault() ?? new GameObject( true, "ambient" );
			var light = go.GetOrAddComponent<AmbientLight>();
			light.Color = Color.White * 0.15f;
		}
	}
}
