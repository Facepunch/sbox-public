using NativeEngine;

namespace Sandbox.Rendering;


/// <summary>
/// Captures frames for screenshots and video recording.
/// </summary>
internal class MediaRecorderLayer : ProceduralRenderLayer
{
	public MediaRecorderLayer()
	{
		Name = "Record Movie Frame";
		Flags |= LayerFlags.NeverRemove;
		Flags |= LayerFlags.DoesntModifyColorBuffers;
		Flags |= LayerFlags.DoesntModifyDepthStencilBuffer;
	}

	internal override void OnRender()
	{
		var colorTarget = Graphics.SceneLayer.GetColorTarget();

		if ( colorTarget.IsNull )
			return;

		try
		{
			if ( !colorTarget.IsStrongHandleValid() )
			{
				return;
			}

			ScreenshotService.ProcessFrame( Graphics.Context, colorTarget );

			if ( ScreenRecorder.IsRecording() )
			{
				ScreenRecorder.RecordVideoFrame( Graphics.Context, colorTarget );
			}
		}
		finally
		{
			colorTarget.DestroyStrongHandle();
		}
	}
}

/// <summary>
/// Draws a red rectangle border around the screen when recording.
/// </summary>
internal class MediaRecorderOverlayLayer : ProceduralRenderLayer
{
	public MediaRecorderOverlayLayer()
	{
		Name = "Post Record Movie Frame";
		Flags |= LayerFlags.NeverRemove;
	}

	internal override void OnRender()
	{
		if ( !ScreenRecorder.IsRecording() ) return;

		var width = Graphics.Viewport.Width;
		var height = Graphics.Viewport.Height;

		const float rectSize = 8f;
		var color = new Color( 255, 0, 0, 60 );

		// Draw top border
		Graphics.DrawRoundedRectangle( new Rect( 0, 0, width, rectSize ), color );

		// Draw bottom border
		Graphics.DrawRoundedRectangle( new Rect( 0, height - rectSize, width, rectSize ), color );

		// Draw left border
		Graphics.DrawRoundedRectangle( new Rect( 0, rectSize, rectSize, height - (rectSize * 2) ), color );

		// Draw right border
		Graphics.DrawRoundedRectangle( new Rect( width - rectSize, rectSize, rectSize, height - (rectSize * 2) ), color );
	}
}
