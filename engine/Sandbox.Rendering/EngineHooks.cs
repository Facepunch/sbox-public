using Sandbox.Rendering;
using Sandbox.Rendering.Bridge;
using Sandbox.Rendering.Pipeline;

namespace Sandbox.Rendering;

/// <summary>
/// What this assembly gives the engine, installed by the app system at startup: Basalt's host, the layers of native's
/// rendering pipeline, and the renderer behind <c>r_managed_scene</c>.
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
		ManagedSceneRendering.Renderer ??= new SceneBridge();
	}
}
