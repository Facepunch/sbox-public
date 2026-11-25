using Sandbox.Engine;
using Sandbox.Rendering;
using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// The platform overlay, over the window's cameras: the menu's UI, the debug overlay, frame capture and the dev tools
	/// dock, drawn in a Borealis.Gpu game window's last block.
	/// </summary>
	internal static void RenderOverlay()
	{
		RenderUiOverlay();
		DebugOverlay.Render();
		ScreenCaptureUtility.CaptureFrame();
		ScreenCaptureUtility.DrawRecordingBorder();

		// Over everything, and after the capture so recordings don't have it in
		Sandbox.Diagnostics.DevTools.RenderGameDock();
	}

	static void RenderUiOverlay()
	{
		// An exported game has no menu, just its loading screen
		if ( Application.IsStandalone )
		{
			StandaloneLoadingScreen.Render();
			return;
		}

		// Wanted again after a pause: simulate it next frame, rather than draw what it built before
		EngineLoop.MenuUIOverlayWanted = EngineLoop.LoopFrames;
		if ( !EngineLoop.MenuUISimulated )
			return;

		using var _ = GlobalContext.MenuScope();
		GlobalContext.Current.UISystem.Render();
	}

}
