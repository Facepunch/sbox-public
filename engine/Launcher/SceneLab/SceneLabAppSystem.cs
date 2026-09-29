namespace Sandbox.SceneLab;

/// <summary>
/// Scene Lab - the proving ground for the managed scene renderer (<c>Sandbox.SceneRenderer</c>), the way
/// PanelGallery is for panels. Boots the minimal panel app and opens a <see cref="SceneLabWindow"/>:
/// the renderer draws the world into the window's swap chain and the panel UI draws over it.
///
/// Arguments:
/// <list type="bullet">
/// <item><c>-model path.vmdl</c>, <c>-grid N</c>, <c>-lights N</c>, <c>-lightradius f</c>, <c>-spot</c>,
/// <c>-sun scale</c> - describe the starting scene instead of picking one from the Scene menu.</item>
/// <item><c>-scene "Name"</c> - start with one of the Scene menu's presets.</item>
/// <item><c>-debugshader</c> - start with the debug shader (faces tinted by axis) instead of the models' materials.</item>
/// <item><c>-capture path.png</c> - save the frame picked by <c>-capture-frame</c> (default 30), then exit.</item>
/// <item><c>-native</c> - with <c>-capture</c>, also render the same world with the native scenesystem to
/// path.native.png and compare the two. Exits with code 2 if they differ.</item>
/// <item><c>-width</c> / <c>-height</c> - window size.</item>
/// <item><c>-zoom f</c> - start the camera at f times its usual distance.</item>
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
		// +r_managed_async_compute and the like were unknown commands, and quietly did nothing. Saved ones under SceneLab's own
		// cookies, not the game's, so a player's settings don't change what it renders.
		ConVarSystem.AddAssembly( typeof( Sandbox.Rendering.ManagedSceneRendering ).Assembly, "scenelab" );

		// Console variables from the command line, as a game takes them: +name value
		var args = Environment.GetCommandLineArgs();
		for ( int i = 1; i + 1 < args.Length; i++ )
		{
			if ( args[i].Length > 1 && args[i][0] == '+' ) ConVarSystem.Run( $"{args[i][1..]} {args[i + 1]}" );
		}

		var named = Args.String( "-scene" ) is { } name ? SceneLabScene.Presets.FirstOrDefault( x => x.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) : null;
		window = new SceneLabWindow( named ?? SceneLabScene.FromCommandLine() ?? SceneLabScene.Presets[0] );
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
