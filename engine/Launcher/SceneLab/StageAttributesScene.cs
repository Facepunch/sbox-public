namespace Sandbox.SceneLab;

/// <summary>
/// Attributes a camera command list sets stay in its stage, as native's hook layers each render on a context of their own.
/// After post processing a command list sets <c>UIGammaOutput</c> and <c>UIFrameGrabEncoded</c>, as Sandbox's HUD does
/// before painting its own target; after UI another paints translucent rectangles, whose shader encodes its output when
/// <c>UIGammaOutput</c> reaches it.
/// </summary>
internal static class StageAttributesScene
{
	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var camera );
		using var _ = scene.Push();

		GameScenes.Prop( "Box", "models/dev/box.vmdl", new Vector3( 0, 0, 0 ), new Color( 0.4f, 0.6f, 0.9f ) );

		var setter = new Rendering.CommandList( "Scene Lab stage attributes" );
		setter.Attributes.Set( "UIGammaOutput", true );
		setter.Attributes.Set( "UIFrameGrabEncoded", true );
		camera.AddCommandList( setter, Rendering.Stage.AfterPostProcess );

		var painted = new Rendering.CommandList( "Scene Lab after UI" );
		Paint( painted, 0 );
		camera.AddCommandList( painted, Rendering.Stage.AfterUI );

		return scene;
	}

	/// <summary>
	/// The rectangles moved and recoloured.
	/// </summary>
	public static void Change( Scene scene )
	{
		using var _ = scene.Push();
		var painted = new Rendering.CommandList( "Scene Lab after UI" );
		Paint( painted, 1 );
		scene.Camera.ClearCommandLists( Rendering.Stage.AfterUI );
		scene.Camera.AddCommandList( painted, Rendering.Stage.AfterUI );
	}

	static void Paint( Rendering.CommandList commands, int variant )
	{
		using var painter = Painter.Begin( commands, new Rect( 0, 0, 1280, 720 ) );
		for ( int i = 0; i < 4; i++ )
		{
			painter.Fill = new ColorHsv( i * 70 + variant * 35, 0.6f, 0.5f, 0.6f ).ToColor();
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( 80 + i * 280 + variant * 40, 480, 220, 160 ) );
		}
	}
}
