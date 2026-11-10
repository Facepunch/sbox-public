using Sandbox.Rendering;

namespace Sandbox.SceneLab;

/// <summary>
/// Envmap Capture: glossy spheres in the middle of a ring of coloured props, lit by an envmap probe whose cube is captured
/// every frame, as a realtime probe's is (<c>CubemapRendering.Render</c>: six faces and the GGX filter), through the renderer
/// being compared. The isolated capture compares the cube's faces too.
/// </summary>
internal static class EnvmapCaptureScene
{
	static readonly Vector3 Center = new( 0, 0, 40 );
	const int Size = 128;

	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _camera, new Vector3( -230, -150, 120 ), new Vector3( 0, 0, 30 ) );
		using var _ = scene.Push();

		GameScenes.Prop( "Mirror Ball", "models/dev/sphere.vmdl", Center, Color.White, "materials/dev/dev_metal_rough00.vmat", 0.9f );
		GameScenes.Prop( "Satin Ball", "models/dev/sphere.vmdl", Center + new Vector3( -40, 70, -10 ), Color.White, "materials/dev/dev_metal_rough40.vmat", 0.5f );

		for ( int i = 0; i < 8; i++ )
		{
			var angle = i * MathF.PI / 4;
			var position = Center + new Vector3( MathF.Cos( angle ) * 150, MathF.Sin( angle ) * 150, i % 2 == 0 ? 0 : 30 );
			GameScenes.Prop( $"Ring {i}", "models/dev/box.vmdl", position, new ColorHsv( i * 45, 0.8f, 1 ).ToColor(), scale: i % 2 == 0 ? 0.8f : 0.5f );
		}

		var light = new GameObject( true, "Lamp" );
		light.WorldPosition = Center + new Vector3( 60, -80, 80 );
		var point = light.Components.Create<PointLight>();
		point.LightColor = new Color( 1.0f, 0.8f, 0.6f ) * 2;
		point.Radius = 300;
		point.Shadows = true;

		var cube = Texture.CreateCube( Size, Size ).WithUAVBinding().WithMips( 7 ).WithFormat( ImageFormat.RGBA16161616F ).Finish();
		var probeObject = new GameObject( true, "Probe" );
		probeObject.WorldPosition = Center;
		var probe = probeObject.Components.Create<EnvmapProbe>();
		probe.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
		probe.Texture = cube;
		probe.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 600, 600, 400 ) );

		return scene;
	}

	/// <summary>
	/// The probe's cube.
	/// </summary>
	public static Texture Cube( Scene scene ) => scene.GetAllComponents<EnvmapProbe>().First().Texture;

	/// <summary>
	/// Capture the probe's cube from its centre, as a realtime probe does, through <paramref name="renderer"/>.
	/// </summary>
	public static void Capture( Scene scene, ISceneRenderer renderer )
	{
		CubemapRendering.Render( scene.SceneWorld, Cube( scene ), new Transform( Center ), 1, 4096, CubemapRendering.GGXFilterType.Fast, renderer: renderer );
	}
}
