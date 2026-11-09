namespace Sandbox.SceneLab;

/// <summary>
/// A renderer's own command list (<c>Renderer.ExecuteBefore</c>), which both renderers run before its object's draws, as
/// Sandbox's tool guns paint their screens: it paints a texture the object's material samples. Compared isolated, so
/// one renderer's painting can't show in the other's frame.
/// </summary>
internal static class ObjectCommandListScene
{
	public static Scene Build()
	{
		var scene = GameScenes.Stage( out var _camera );
		using var _ = scene.Push();

		var screen = Texture.CreateRenderTarget().WithSize( 256, 256 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Black ).Create();
		var material = Material.Load( "materials/dev/primary_white.vmat" ).CreateCopy();
		material.Set( "g_tColor", screen );

		var renderer = GameScenes.Prop( "Screen", "models/dev/box.vmdl", new Vector3( 0, 0, 0 ) );
		renderer.MaterialOverride = material;

		var commands = new Rendering.CommandList( "Scene Lab screen" );
		Paint( commands, screen, 0 );
		renderer.ExecuteBefore = commands;

		GameScenes.Prop( "Plain", "models/dev/box.vmdl", new Vector3( 80, 90, 0 ), new Color( 0.5f, 0.7f, 0.4f ), scale: 0.6f );
		return scene;
	}

	/// <summary>
	/// The screen painted another way.
	/// </summary>
	public static void Change( Scene scene )
	{
		using var _ = scene.Push();
		var renderer = scene.Directory.FindByName( "Screen" ).First().Components.Get<ModelRenderer>();
		var screen = renderer.MaterialOverride.GetTexture( "g_tColor" );
		var commands = new Rendering.CommandList( "Scene Lab screen" );
		Paint( commands, screen, 1 );
		renderer.ExecuteBefore = commands;
	}

	static void Paint( Rendering.CommandList commands, Texture screen, int variant )
	{
		commands.SetRenderTarget( RenderTarget.From( screen ) );
		using ( var painter = Painter.Begin( commands, new Rect( 0, 0, 256, 256 ) ) )
		{
			painter.Clear( new ColorHsv( 200 + variant * 120, 0.7f, 0.6f ).ToColor() );
			painter.Stroke = Stroke.None;
			for ( int i = 0; i < 4; i++ )
			{
				painter.Fill = new ColorHsv( i * 90 + variant * 45, 0.8f, 1 ).ToColor();
				painter.Rect( new Rect( 24 + i * 56, 40 + (i + variant) % 2 * 90, 40, 120 ) );
			}
		}

		commands.ClearRenderTarget();
	}
}
