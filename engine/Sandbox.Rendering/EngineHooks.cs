using Sandbox.Rendering;
using Sandbox.Rendering.Bridge;
using Sandbox.Rendering.Pipeline;

namespace Sandbox.Rendering;

/// <summary>
/// What this assembly gives the engine, installed by the app system at startup: Borealis's host, the layers of native's
/// rendering pipeline, and Borealis behind the scene renderer seam (<c>-borealisgpu</c>).
/// </summary>
internal static class EngineHooks
{
	/// <summary>
	/// Register with the engine. Nothing native is touched, so it can run before the engine starts.
	/// </summary>
	public static void Install()
	{
		RenderHost.Current = new SboxRenderHost();
		RenderPipeline.Current ??= new NativeRenderPipeline();
		SceneRenderer.Borealis ??= new SceneBridge();

		// What the engine reads for a realtime probe's cube is the capture Borealis draws it into, on the same device
		BorealisGpuRenderBackend.RenderedInto ??= ( backend, texture ) => MirrorContent.IsCaptureCube( texture ) && MirrorContent.Gpu == backend.Services ? MirrorContent.CaptureCube( texture ) : null;
	}
}
