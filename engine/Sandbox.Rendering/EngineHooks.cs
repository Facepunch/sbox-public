using Sandbox.Rendering;
using Sandbox.Rendering.Bridge;

namespace Sandbox.Rendering;

/// <summary>
/// What this assembly gives the engine, installed by the app system at startup: Borealis's host, and Borealis behind the
/// scene renderer seam (<c>-borealisgpu</c>).
/// </summary>
internal static class EngineHooks
{
	/// <summary>
	/// Register with the engine. Nothing native is touched, so it can run before the engine starts.
	/// </summary>
	public static void Install()
	{
		RenderHost.Current = new SboxRenderHost();
		SceneRenderer.Borealis ??= new SceneBridge();

		// What the engine reads for a realtime probe's cube is the capture Borealis draws it into, on the same device, and for a
		// render target a child view drew last (a mirror's DrawReflection), what that view drew
		BorealisGpuRenderBackend.RenderedInto ??= MirrorContent.RenderedInto;

		// And that capture goes when the engine texture is disposed, not only once it's collected: a map's probes' cubes are
		// disposed as their components go but stayed referenced, so each map loaded left its cubes behind
		BorealisGpuRenderBackend.Released ??= ( backend, texture ) =>
		{
			if ( MirrorContent.Gpu == backend.Services ) MirrorContent.ReleaseCapture( texture );
		};

		// The engine's own drawing of a material whose shader is a surface alone draws the material the scene draws it with
		BorealisGpuRenderBackend.SceneMaterial ??= ( backend, material ) => MirrorContent.Gpu == backend.Services ? MirrorContent.Material( material ) : null;
	}
}
