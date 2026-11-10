using Sandbox.Rendering;

namespace Sandbox.SceneLab;

/// <summary>
/// Mirror Reflection: the camera's own command list draws the scene reflected in a mirror standing behind the props
/// (<c>CommandList.DrawReflection</c>) after the transparent pass and shows it in a picture over the frame, so the reflection
/// has to be rendered before the command after it, in the same frame. A box, a sphere, a pillar and a shadowed point light
/// stand on the floor in the sun. Compared isolated.
/// Mirror Reflection Shadows turns <c>r.reflections.shadows</c> on, so the reflection draws shadows of its own, and gives the
/// reflection an ordinary projection: an oblique one's sun cascades differ from native's by design.
/// </summary>
internal static class MirrorScene
{
	public static Scene Build() => Build( shadows: false );

	public static Scene BuildWithShadows() => Build( shadows: true );

	static Scene Build( bool shadows )
	{
		CommandList.ReflectionShadows = shadows;

		var scene = GameScenes.Stage( out var camera, new Vector3( -300, -170, 140 ), new Vector3( 0, 20, 30 ) );
		using var _ = scene.Push();

		GameScenes.Prop( "Box", "models/dev/box.vmdl", new Vector3( 30, -40, 25 ), new Color( 0.9f, 0.4f, 0.2f ) );
		GameScenes.Prop( "Sphere", "models/dev/sphere.vmdl", new Vector3( -50, 40, 30 ), new Color( 0.3f, 0.6f, 0.9f ), scale: 0.8f );
		GameScenes.Prop( "Pillar", "models/dev/box.vmdl", new Vector3( 90, 60, 60 ), new Color( 0.4f, 0.8f, 0.4f ), scale: 0.5f ).GameObject.WorldScale = new Vector3( 0.5f, 0.5f, 2.4f );

		var light = new GameObject( true, "Lamp" );
		light.WorldPosition = new Vector3( 10, 10, 90 );
		var point = light.Components.Create<PointLight>();
		point.LightColor = new Color( 1.0f, 0.7f, 0.4f ) * 2;
		point.Radius = 260;
		point.Shadows = true;

		// The reflection in a mirror at y = 160, facing the camera, in a picture at the frame's top left
		var commands = new CommandList( "Scene Lab mirror" );
		var target = commands.GetRenderTarget( "Scene Lab mirror reflection", 2, ImageFormat.RGBA16161616F, ImageFormat.Default );
		commands.DrawReflection( camera, new Plane( new Vector3( 0, 160, 0 ), Vector3.Right ), target, new ReflectionSetup
		{
			ViewSetup = new ViewSetup
			{
				ProjectionMatrix = shadows ? Matrix.CreateProjection( camera.ZNear, camera.ZFar, camera.FieldOfView, 1280.0f / 760.0f, new Vector4( -1, -1, 1, 1 ) ) : null,
				EnablePostprocessing = false,
				EnableUI = false,
				EnableDebugOverlays = false,
				ViewHash = HashCode.Combine( "Scene Lab mirror" ),
			}
		} );
		commands.Attributes.Set( "Texture", target.ColorTexture );
		commands.DrawQuad( new Rect( 40, 40, 560, 330 ), Material.UI.Basic, Color.White );
		camera.AddCommandList( commands, Stage.AfterTransparent );

		return scene;
	}
}
