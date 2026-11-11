using Sandbox.Rendering;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A camera rendering into a game window on Basalt.Gpu (<c>-basaltgpu</c>, docs/managed/basalt-gpu-path.md stage 5).
/// </summary>
internal sealed partial class SceneBridge
{
	static readonly StringToken UIGammaOutput = new( "UIGammaOutput" );

	// What a camera's stages on Basalt.Gpu ask of their frame; stages run one camera at a time, so one does
	BasaltGpuView gpuView;

	/// <summary>
	/// Render a camera into the frame <paramref name="window"/> is drawing: its viewport cleared to its background, then the
	/// stages after post processing (the HUD's command lists, the UI), in a block on the window's target.
	/// </summary>
	void RenderInto( BasaltGpuWindow window, SceneCamera camera, Vector2? size, in ViewSetup setup )
	{
		var renderSize = size ?? window.Size;
		if ( renderSize.x < 1 || renderSize.y < 1 ) return;

		camera.OnPreRender( renderSize );
		var rect = camera.Rect;
		var viewport = new Rect( rect.Left * renderSize.x, rect.Top * renderSize.y, rect.Width * renderSize.x, rect.Height * renderSize.y );

		gpuView ??= new BasaltGpuView( RenderBackend.Default );
		gpuView.Target = RenderTarget.From( window.Target );
		gpuView.Viewport = viewport;
		gpuView.PostProcessEnabled = camera.EnablePostProcessing;
		gpuView.SetCamera( camera, viewport.Size );

		window.Draw( gpuView, () =>
		{
			if ( (camera.ClearFlags & ClearFlags.Color) != 0 ) Graphics.Clear( camera.BackgroundColor, true, false, false );

			// The window's target is 8-bit, bound through a linear view: UI writes gamma-encoded output into it, as
			// native's UI hook layers do over an sRGB swap chain (ui_gamma_blend)
			Graphics.Attributes.Set( UIGammaOutput, true );
			if ( camera.RenderUI && camera.Attributes.GetBool( "enableEarlyUI" ) ) RunStage( camera, Stage.EarlyUI );

			Graphics.Attributes.Set( UIGammaOutput, false );
			RunStage( camera, Stage.AfterPostProcess );

			Graphics.Attributes.Set( UIGammaOutput, true );
			if ( camera.RenderUI ) RunStage( camera, Stage.UI );
			RunStage( camera, Stage.AfterUI );
		} );
	}

	readonly HashSet<(int, Stage)> stageFailures = new();

	/// <summary>A camera's stage, in the block being recorded. One that throws is logged once, and the frame goes on.</summary>
	void RunStage( SceneCamera camera, Stage stage )
	{
		try
		{
			((IManagedCamera)camera).OnRenderStage( stage );
		}
		catch ( Exception e )
		{
			if ( stageFailures.Add( (camera._cameraId, stage) ) ) Log.Warning( e, $"Basalt.Gpu: {camera.Name}'s {stage} stage: {e.Message}" );
		}
	}
}
