namespace Sandbox.SceneLab;

/// <summary>
/// Scene Lab - GameObject scenes, maps and scripted scenarios rendered by native's scene system, to measure and capture it.
/// Boots the minimal panel app and opens a <see cref="SceneLabWindow"/>: native draws the scene's camera into the window's
/// swap chain and the panel UI draws over it.
///
/// Arguments:
/// <list type="bullet">
/// <item><c>-scene "Name"</c> - start with one of the Scene menu's presets.</item>
/// <item><c>-capture path.png</c> - save the camera's frame picked by <c>-capture-frame</c> (default 30) beside it as
/// <c>path.native.png</c>, then exit.</item>
/// <item><c>-width</c> / <c>-height</c> - window size.</item>
/// <item><c>-novsync</c> - don't wait for the display, so the frame rate shows what the app can actually do.</item>
/// <item><c>+name value</c> - set a console variable, native ones too, before the first scene loads.</item>
/// </list>
/// Left drag orbits, the wheel zooms.
/// </summary>
internal sealed class SceneLabAppSystem : PanelAppSystem
{
	SceneLabWindow window;

	/// <summary>
	/// The benchmark scenes want more than the 65,536 transforms a UI app gets.
	/// </summary>
	protected override bool DrawsScenes => true;

	protected override void OnInitialized()
	{
		// The engine's managed console variables, which a panel app doesn't register (Bootstrap.InitEngineConVars): without them
		// they'd be unknown commands, and quietly do nothing. Saved ones under SceneLab's own cookies, not the game's, so a
		// player's settings don't change what it renders.
		ConVarSystem.AddAssembly( typeof( Sandbox.Rendering.ManagedSceneRendering ).Assembly, "scenelab" );

		// Console variables from the command line, as a game takes them: +name value
		var args = Environment.GetCommandLineArgs();
		for ( int i = 1; i + 1 < args.Length; i++ )
		{
			if ( args[i].Length > 1 && args[i][0] == '+' ) ConVarSystem.Run( $"{args[i][1..]} {args[i + 1]}" );
		}

		var named = Args.String( "-scene" ) is { } name ? SceneLabScene.Presets.FirstOrDefault( x => x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) : null;
		window = new SceneLabWindow( named ?? SceneLabScene.Presets[0] );
	}

	protected override bool RunFrame()
	{
		var running = base.RunFrame();

		// A command line capture is done - closing the last window ends the app
		if ( window is { Finished: true } )
		{
			window.Dispose();
			window = null;
		}

		return running;
	}
}
