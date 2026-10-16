using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// Independent camera effects in the native pipeline, before the ordinary AfterDepthPrepass hook.
/// The layer dependencies release compute after Hi-Z and join it after the graphics effects.
/// </summary>
internal sealed class EarlyPostProcessLayer : NativeProceduralLayer
{
	readonly PostProcessQueue _queue;

	internal RenderMultisampleType Msaa { get; set; }

	public EarlyPostProcessLayer( PostProcessQueue queue )
		: base( queue == PostProcessQueue.AsyncCompute ? "Early post processing (async compute)" : "Early post processing (graphics)" )
	{
		_queue = queue;

		if ( queue == PostProcessQueue.AsyncCompute )
			Flags |= LayerFlags.AsyncComputeContext;
	}

	protected override void Render( ManagedRenderSetup_t setup )
	{
		// These compute passes have no attachments, but still sample the camera's multisampled normals.
		setup.msaaLevel = Msaa;
		base.Render( setup );
	}

	protected override void OnRender()
	{
		var camera = IManagedCamera.FindById( Graphics.SceneView.m_ManagedCameraId ) as SceneCamera;
		camera?.RenderEarlyPostProcess( Stage.AfterDepthPrepass, _queue );

		if ( _queue == PostProcessQueue.AsyncCompute )
			Graphics.SceneLayer.SetSemaphore( Graphics.Context.SemaphoreSignalAtEnd() );
	}
}
