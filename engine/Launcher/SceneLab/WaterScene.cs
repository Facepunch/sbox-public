namespace Sandbox.SceneLab;

/// <summary>
/// A pool of water over a box standing in it: the water takes its refraction capture in a command list its renderer runs
/// before drawing (<c>Renderer.ExecuteBefore</c>), so what's under the surface is seen through it. Compared isolated.
/// </summary>
internal static class WaterScene
{
	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _camera, new Vector3( -320, -240, 160 ), new Vector3( 0, 0, 0 ) );
		using var _ = scene.Push();

		GameScenes.Prop( "Submerged", "models/dev/box.vmdl", new Vector3( 20, 10, -30 ), new Color( 0.9f, 0.4f, 0.2f ) );
		GameScenes.Prop( "Pillar", "models/dev/box.vmdl", new Vector3( -60, 80, -20 ), new Color( 0.3f, 0.8f, 0.4f ), scale: 0.6f ).GameObject.WorldScale = new Vector3( 0.6f, 0.6f, 2.5f );

		var water = new GameObject( true, "Water" );
		water.WorldPosition = new Vector3( 0, 0, 20 );
		var body = water.Components.Create<WaterBody>();
		body.Size = new Vector2( 400, 400 );

		return scene;
	}
}
