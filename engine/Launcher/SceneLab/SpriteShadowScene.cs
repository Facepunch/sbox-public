namespace Sandbox.SceneLab;

/// <summary>
/// The Sprite Shadows preset: two shadowed point lights a little apart, each with a cluster of sprites that cast shadows
/// next to it, and a third cluster far outside both. The sprites all draw through one batch, so its bounds cover the
/// three clusters and every shadow view of either light passes the scene's cull; what it draws into each is what
/// <c>SpriteBatchSceneObject</c> decides itself - nothing for a view that reaches no cluster, and the sprites once, without
/// a sort, for the rest. Still, so a capture is the same whichever way those are done.
/// </summary>
internal static class SpriteShadowScene
{
	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( 0, -520, 300 ), new Vector3( 0, 0, 30 ) );
		using var _ = scene.Push();

		// Only the point lights cast, so what's compared is theirs
		var sun = scene.Directory.FindByName( "Sun" ).First().Components.Get<DirectionalLight>();
		sun.Shadows = false;
		sun.LightColor = new Color( 1.0f, 0.95f, 0.85f ) * 0.1f;

		Vector3[] lights = [new( -170, 0, 120 ), new( 170, 0, 120 )];
		for ( int i = 0; i < lights.Length; i++ )
		{
			var go = new GameObject( true, $"Light {i}" );
			go.WorldPosition = lights[i];

			var light = go.Components.Create<PointLight>();
			light.LightColor = new ColorHsv( i * 120 + 20, 0.25f, 1 ).ToColor() * 8;
			light.Radius = 230;
			light.Shadows = true;
		}

		var sprite = Sprite.FromTexture( Texture.Load( "textures/particles/base_sprite.vtex" ) );

		// Around each light, and one far out of reach of both
		Vector3[] clusters = [new( -170, 0, 0 ), new( 170, 0, 0 ), new( 0, 700, 0 )];
		for ( int c = 0; c < clusters.Length; c++ )
		{
			for ( int i = 0; i < 6; i++ )
			{
				var go = new GameObject( true, $"Sprite {c}.{i}" );
				go.WorldPosition = clusters[c] + new Vector3( MathF.Cos( i * 1.05f ) * 70, MathF.Sin( i * 1.05f ) * 70, 40 + i * 12 );

				var renderer = go.Components.Create<SpriteRenderer>();
				renderer.Sprite = sprite;
				renderer.Size = 46;
				renderer.Color = new ColorHsv( c * 120 + i * 20, 0.6f, 1 ).ToColor();
				renderer.Shadows = true;
			}
		}

		return scene;
	}
}
