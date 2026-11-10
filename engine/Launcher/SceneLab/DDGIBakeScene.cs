using Sandbox.Rendering;

namespace Sandbox.SceneLab;

/// <summary>
/// DDGI Bake: a small indirect light volume baked through the renderer being compared (<c>DDGIProbeUpdaterCubemapper</c>: a
/// cube capture per probe, its depth copied face by face and the probe integrated after the sixth face, two bounces), in a
/// room of coloured walls lit by a sun and a lamp. The baked volume lights the props in the frame, and the isolated capture
/// compares the last probe's colour and depth captures and the baked volumes too.
/// </summary>
internal static class DDGIBakeScene
{
	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _camera, new Vector3( -260, -200, 150 ), new Vector3( 0, 0, 40 ) );
		using var _ = scene.Push();

		// Coloured walls on two sides, so the bounce has colour to carry
		GameScenes.Prop( "Red Wall", "models/dev/box.vmdl", new Vector3( 0, 160, 60 ), new Color( 0.9f, 0.2f, 0.15f ) ).GameObject.WorldScale = new Vector3( 6, 0.2f, 2.5f );
		GameScenes.Prop( "Green Wall", "models/dev/box.vmdl", new Vector3( 160, 0, 60 ), new Color( 0.2f, 0.8f, 0.3f ) ).GameObject.WorldScale = new Vector3( 0.2f, 6, 2.5f );
		GameScenes.Prop( "Box", "models/dev/box.vmdl", new Vector3( 20, 30, 25 ), Color.White );
		GameScenes.Prop( "Sphere", "models/dev/sphere.vmdl", new Vector3( -60, -30, 30 ), Color.White, scale: 0.8f );

		var lamp = new GameObject( true, "Lamp" );
		lamp.WorldPosition = new Vector3( 60, 60, 110 );
		var point = lamp.Components.Create<PointLight>();
		point.LightColor = new Color( 1.0f, 0.85f, 0.6f ) * 2;
		point.Radius = 400;
		point.Shadows = true;

		var volumeObject = new GameObject( true, "Volume" );
		// Every probe in open air, above the props and inside the walls: with no relocation, a probe on the floor's plane or
		// inside a prop sees degenerate depth either renderer resolves its own way
		volumeObject.WorldPosition = new Vector3( 0, 0, 110 );
		var volume = volumeObject.Components.Create<IndirectLightVolume>();
		volume.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 260, 260, 80 ) );
		volume.ProbeDensity = 2;

		return scene;
	}

	static IndirectLightVolume Volume( Scene scene ) => scene.GetAllComponents<IndirectLightVolume>().First();

	// The bake this load made, which owns the volume's textures
	static DDGIProbeUpdaterCubemapper updater;
	static Scene baked;

	/// <summary>
	/// Bake the volume once through <paramref name="renderer"/>, the first frame it's asked for, and light the scene with it.
	/// </summary>
	public static void Capture( Scene scene, ISceneRenderer renderer )
	{
		if ( baked == scene ) return;

		updater?.Dispose();
		baked = scene;

		// No probe relocation: its parallel traces assert in the physics world a scene this new has
		var volume = Volume( scene );
		updater = new DDGIProbeUpdaterCubemapper( volume ) { Renderer = renderer };
		updater.BakeNow();

		volume.IrradianceTexture = updater.GeneratedIrradianceTexture;
		volume.DistanceTexture = updater.GeneratedDistanceTexture;
		volume.BakeVersion = 1;
	}

	static Texture Field( string name ) => (Texture)typeof( DDGIProbeUpdaterCubemapper ).GetField( name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( updater );

	/// <summary>
	/// The last probe's colour capture, from the second bounce.
	/// </summary>
	public static Texture ColorCapture( Scene scene ) => Field( "_captureTexture" );

	/// <summary>
	/// The last probe's depth, copied face by face from each face's depth by the camera's stage hook, and the baked irradiance
	/// and distance volumes, integrated from each probe's captures after its sixth face's transparent pass.
	/// </summary>
	public static Texture[] Readbacks( Scene scene ) => [Field( "_captureDepth" ), Volume( scene ).IrradianceTexture, Volume( scene ).DistanceTexture];
}
