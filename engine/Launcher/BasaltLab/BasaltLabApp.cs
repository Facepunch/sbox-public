using Basalt;

namespace Sandbox.BasaltLab;

/// <summary>
/// Basalt Lab: Basalt rendering a lab preset's <see cref="RenderWorld"/> with nothing else of the engine running. Shows it in
/// a window, or renders presets into a texture, reads them back, saves them and exits (<see cref="PresetRun"/>).
///
/// Arguments:
/// <list type="bullet">
/// <item><c>-preset "Name"</c> - the lab preset (<see cref="LabPresets"/>), Sun Shadows by default.</item>
/// <item><c>-presets "A;B"</c>, <c>-suite core</c> - several presets, captured one after another in this process.</item>
/// <item><c>-capture path.png</c> - render <c>-capture-frame</c> frames (default 10) into a texture, no window, and save the last.</item>
/// <item><c>-out folder</c> - capture each preset to <c>folder/PresetName.png</c> (default <c>screenshots/basaltlab</c>).</item>
/// <item><c>-golden-truth folder</c> - check each capture against Basalt's approved images (<see cref="GoldenTruthRun"/>);
/// exit code 1 when any fails. <c>-golden-truth-update</c> writes them.</item>
/// <item><c>-width</c> / <c>-height</c> - the window or texture size (default 640x360, the render tests' size).</item>
/// <item><c>-zoom f</c> - the camera at f times its usual distance.</item>
/// <item><c>-msaa N</c> - capture into a target with N samples (default 1).</item>
/// <item><c>-frames N</c> - close the window after N frames.</item>
/// <item><c>-novsync</c> - don't wait for the display.</item>
/// <item><c>-scene-modules</c> - also start the native modules SceneLab starts (world renderer, animation, physics, sound).
/// Presets with posed skinned models start them anyway.</item>
/// <item><c>-ui-parity folder</c> - draw a card of UI through Source 2 and through a Basalt.Gpu device, compare and exit (<see cref="UiParity"/>).</item>
/// <item><c>-model-parity folder</c> - render s&amp;box models with Basalt on Source 2's device and on a Basalt.Gpu device, compare and exit (<see cref="ModelParity"/>).</item>
/// </list>
/// Startup and frame times go to the log, and a summary to the console.
/// </summary>
internal static class BasaltLabApp
{
	public static int Run()
	{
		// Posing skinned presets takes the animation system, which only the longer list of native modules starts
		var presets = PresetRun.Selected();
		var posing = presets.Any( x => x.Preset is { Sequences.Length: > 0 } );

		var backend = new Source2Backend();
		backend.Start( sceneModules: Args.Has( "-scene-modules" ) || posing, windows: !PresetRun.Capturing );

		if ( Args.String( "-ui-parity" ) is { } parity )
		{
			var code = UiParity.Run( backend, parity );
			return backend.Exit( code );
		}

		if ( Args.String( "-model-parity" ) is { } models )
		{
			var code = ModelParity.Run( backend, models );
			return backend.Exit( code );
		}

		var run = PresetRun.Run( backend, presets );
		return backend.Exit( run );
	}
}
