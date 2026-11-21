using Borealis;

namespace Sandbox.BorealisLab;

/// <summary>
/// Borealis Lab: Borealis rendering a lab preset's <see cref="RenderWorld"/> on a Borealis.Gpu device of its own, with the
/// engine up beside it only for posing and models made in code. Shows it in a window, or renders presets into a texture, reads
/// them back, saves them and exits (<see cref="PresetRun"/>).
///
/// Arguments:
/// <list type="bullet">
/// <item><c>-preset "Name"</c> - the lab preset (<see cref="LabPresets"/>), Sun Shadows by default.</item>
/// <item><c>-presets "A;B"</c>, <c>-suite core</c> - several presets, captured one after another in this process.</item>
/// <item><c>-capture path.png</c> - render <c>-capture-frame</c> frames (default 10) into a texture, no window, and save the last.</item>
/// <item><c>-out folder</c> - capture each preset to <c>folder/PresetName.png</c> (default <c>screenshots/borealislab</c>).</item>
/// <item><c>-golden-truth folder</c> - check each capture against Borealis's approved images (<see cref="GoldenTruthRun"/>);
/// exit code 1 when any fails. <c>-golden-truth-update</c> writes them.</item>
/// <item><c>-width</c> / <c>-height</c> - the window or texture size (default 640x360, the render tests' size).</item>
/// <item><c>-zoom f</c> - the camera at f times its usual distance.</item>
/// <item><c>-msaa N</c> - capture into a target with N samples (default 1).</item>
/// <item><c>-frames N</c> - close the window after N frames.</item>
/// <item><c>-novsync</c> - don't wait for the display.</item>
/// <item><c>-scene-modules</c> - also start the native modules SceneLab starts (world renderer, animation, physics, sound).
/// Presets with posed skinned models start them anyway.</item>
/// <item><c>-model-parity folder</c> - check s&amp;box models read by Borealis.Source2 against the engine's loading, render each on a Borealis.Gpu device and exit (<see cref="ModelParity"/>).</item>
/// <item><c>-surface-parity folder</c> - draw one material as a .shader, a Slang surface and a raw Slang module on a Borealis.Gpu device, compare and exit (<see cref="SurfaceParity"/>).</item>
/// <item><c>-convert-passes a,b|all</c> - write a pass module beside each named non-material shader and compile it (<see cref="PassConversion"/>).</item>
/// <item><c>-package-shaders folder -package-roots "a;b"</c> - sort package shaders by what the converter makes of them (<see cref="PackageShaders"/>).</item>
/// <item><c>-crowd</c> - SceneLab's Crowd Near, animated, on a Borealis.Gpu device in a window, timed (<see cref="CrowdRun"/>).</item>
/// <item><c>-cook-meshlets out</c> - copy a compiled map package with the meshlets the resource compiler cooks added to its meshes (<see cref="Maps.MeshletCook"/>).</item>
/// <item><c>-map ident</c>, <c>-construct</c> - read a compiled map with Borealis.Source2 and time drawing it on a Borealis.Gpu device, with no Source 2 device (<see cref="Maps.MapRun"/>).</item>
/// </list>
/// Startup and frame times go to the log, and a summary to the console.
/// </summary>
internal static class BorealisLabApp
{
	public static int Run()
	{
		// A map package copied with its meshes' meshlets cooked in: no device
		if ( Maps.MeshletCook.Requested ) return Maps.MeshletCook.Run();

		// A map on a Borealis.Gpu device of its own: nothing of the engine starts
		if ( Maps.MapRun.Requested ) return Maps.MapRun.Run();

		// The front end's cost of many small indirect draws, on a Borealis.Gpu device of its own: nothing of the engine starts
		if ( MdiBench.Requested ) return MdiBench.Run();

		// The Slang material tier against today's shaders, on a Borealis.Gpu device of its own: nothing of the engine starts
		if ( SurfaceParity.Requested ) return SurfaceParity.Run();

		// Pass modules from today's non-material shaders: nothing of the engine starts
		if ( PassConversion.Requested ) return PassConversion.Run();

		// Package shaders through the converter: nothing of the engine starts
		if ( PackageShaders.Requested ) return PackageShaders.Run();

		// Posing skinned presets and models takes the animation system, which only the longer list of native modules starts
		var presets = PresetRun.Selected();
		var posing = presets.Any( x => x.Preset is { Sequences.Length: > 0 } );

		var backend = new Source2Backend();
		backend.Start( sceneModules: Args.Has( "-scene-modules" ) || posing || Args.Has( "-model-parity" ) || CrowdRun.Requested, windows: !PresetRun.Capturing );

		if ( CrowdRun.Requested )
		{
			var code = CrowdRun.Run( backend );
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
