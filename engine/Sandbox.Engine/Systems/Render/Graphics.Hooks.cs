using Sandbox.Engine;
using Sandbox.Rendering;
using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// Called by the engine during pipeline. This could be rendering the scene from any camera.
	/// That means you can't assume this is the game view. This might be a tools view, or another view
	/// </summary>
	internal static void OnLayer( int stageenum, ManagedRenderSetup_t setup )
	{
		//
		// Special circumstances for the game UI
		//
		if ( stageenum == -1 )
		{
			using ( new Graphics.Scope( in setup ) )
			{
				RenderOverlay();
			}

			return;
		}

		Rendering.Stage renderStage = (Rendering.Stage)stageenum;

		// find our cameralur
		var cameraId = setup.sceneView.m_ManagedCameraId;
		if ( cameraId == 0 )
			return;

		var currentCamera = IManagedCamera.FindById( cameraId );
		if ( currentCamera is null )
			return;

		// Log.Info( $"cameraReference: \"{currentCamera}\" -> {renderStage}" );

		using ( new Graphics.Scope( in setup ) )
		{
			currentCamera.OnRenderStage( renderStage );
		}
	}

	/// <summary>
	/// The platform overlay, over the window's cameras: the menu's UI, the debug overlay, frame capture and the dev tools
	/// dock. Native's overlay view draws it (<c>CCameraRenderer.RenderOverlay</c>), or a Basalt.Gpu game window's last block.
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
