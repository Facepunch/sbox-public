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
				RenderUiOverlay();
				DebugOverlay.Render();
				CaptureFrameWithOverlays();
				DrawRecordingBorder();
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

	static void RenderUiOverlay()
	{
		if ( Application.IsStandalone )
			return;

		using var _ = GlobalContext.MenuScope();
		GlobalContext.Current.UISystem.Render();
	}

	/// <summary>
	/// Captures the current frame for screenshots and video recording.
	/// Called after overlays so they appear in captures.
	/// When recording, captures from a temporary copy so the recording border doesn't appear in video.
	/// </summary>
	static void CaptureFrameWithOverlays()
	{
		var colorTarget = Graphics.SceneLayer.GetColorTarget();

		if ( colorTarget.IsNull )
			return;

		try
		{
			if ( !colorTarget.IsStrongHandleValid() )
				return;

			if ( ScreenRecorder.IsRecording() )
			{
				// ReadTextureAsync captures the final frame-end state of whichever texture you pass it.
				// Since the recording border is drawn to colorTarget after this call, we must capture
				// from a separate texture that won't have the border drawn on it.
				var width = (int)Graphics.Viewport.Width;
				var height = (int)Graphics.Viewport.Height;

				using var tempRT = RenderTarget.GetTemporary( width, height, Graphics.IdealColorFormat, ImageFormat.None );
				RenderTools.CopyTexture( Graphics.Context, colorTarget, tempRT.ColorTarget.native, default, 0, 0, 0, 0, 0, 0 );

				ScreenshotService.ProcessFrame( Graphics.Context, tempRT.ColorTarget.native );
				ScreenRecorder.RecordVideoFrame( Graphics.Context, tempRT.ColorTarget.native );
			}
			else
			{
				ScreenshotService.ProcessFrame( Graphics.Context, colorTarget );
			}
		}
		finally
		{
			colorTarget.DestroyStrongHandle();
		}
	}

	static void DrawRecordingBorder()
	{
		if ( !ScreenRecorder.IsRecording() )
			return;

		var width = Graphics.Viewport.Width;
		var height = Graphics.Viewport.Height;

		const float rectSize = 4f;
		var color = new Color( 255, 0, 0, 10 );

		Graphics.DrawRoundedRectangle( new Rect( 0, 0, width, rectSize ), color );
		Graphics.DrawRoundedRectangle( new Rect( 0, height - rectSize, width, rectSize ), color );
		Graphics.DrawRoundedRectangle( new Rect( 0, rectSize, rectSize, height - (rectSize * 2) ), color );
		Graphics.DrawRoundedRectangle( new Rect( width - rectSize, rectSize, rectSize, height - (rectSize * 2) ), color );
	}
}
