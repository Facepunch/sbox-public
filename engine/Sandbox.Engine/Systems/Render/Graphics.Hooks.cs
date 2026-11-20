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
		if ( Application.IsStandalone )
			return;

		using var _ = GlobalContext.MenuScope();
		GlobalContext.Current.UISystem.Render();
	}

}
