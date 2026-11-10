namespace Sandbox.SceneLab;

/// <summary>
/// A pool of water over a box standing in it: the water takes its refraction capture in a command list its renderer runs
/// before drawing (<c>Renderer.ExecuteBefore</c>), so what's under the surface is seen through it. Compared isolated.
/// Water Planar asks for planar reflections, seen low over the pool, so the camera's view renders the pool's reflection first
/// and the water samples it: a wall at the back and a ball lit by a lamp. Water Planar Shadows turns <c>r.reflections.shadows</c>
/// on and the lamp's shadows, so the reflection shows the ball's shadow on the wall (the sun's shadows off: an oblique
/// reflection's cascades differ from native's by design).
/// </summary>
internal static class WaterScene
{
	public static Scene Build() => Build( planar: false, shadows: false );

	public static Scene Planar() => Build( planar: true, shadows: false );

	public static Scene PlanarShadows() => Build( planar: true, shadows: true );

	static Scene Build( bool planar, bool shadows )
	{
		Rendering.CommandList.ReflectionShadows = shadows;

		// Planar water is seen low, where it reflects the most
		var scene = planar
			? GameScenes.Stage( out var _camera, new Vector3( -330, -230, 70 ), new Vector3( 60, 80, 35 ) )
			: GameScenes.Stage( out _camera, new Vector3( -320, -240, 160 ), new Vector3( 0, 0, 0 ) );
		using var _ = scene.Push();

		GameScenes.Prop( "Submerged", "models/dev/box.vmdl", new Vector3( 20, 10, -30 ), new Color( 0.9f, 0.4f, 0.2f ) );
		GameScenes.Prop( "Pillar", "models/dev/box.vmdl", new Vector3( -60, 80, -20 ), new Color( 0.3f, 0.8f, 0.4f ), scale: 0.6f ).GameObject.WorldScale = new Vector3( 0.6f, 0.6f, 2.5f );

		if ( planar )
		{
			// A wall standing in the water at the back, and a ball in front of it that a lamp shadows it with
			GameScenes.Prop( "Wall", "models/dev/box.vmdl", new Vector3( 60, 190, 50 ), new Color( 0.9f, 0.85f, 0.7f ) ).GameObject.WorldScale = new Vector3( 4, 0.2f, 2 );
			GameScenes.Prop( "Ball", "models/dev/sphere.vmdl", new Vector3( 60, 120, 70 ), new Color( 0.8f, 0.3f, 0.6f ), scale: 0.5f );

			var lamp = new GameObject( true, "Lamp" );
			lamp.WorldPosition = new Vector3( 60, 40, 80 );
			var point = lamp.Components.Create<PointLight>();
			point.LightColor = new Color( 1.0f, 0.8f, 0.6f ) * 3;
			point.Radius = 320;
			point.Shadows = shadows;
		}

		if ( shadows ) scene.GetAllComponents<DirectionalLight>().First().Shadows = false;

		var water = new GameObject( true, "Water" );
		water.WorldPosition = new Vector3( 0, 0, 20 );
		var body = water.Components.Create<WaterBody>();
		body.Size = new Vector2( 400, 400 );
		if ( planar ) body.Reflections = WaterBody.ReflectionType.Planar;

		return scene;
	}
}
