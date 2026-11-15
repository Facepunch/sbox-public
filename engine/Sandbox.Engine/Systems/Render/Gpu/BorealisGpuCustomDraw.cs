using Borealis;
using Borealis.Backend;
using GpuContext = Borealis.Gpu.GpuContext;

namespace Sandbox;

/// <summary>
/// Custom objects (particles' sprite batches, text, the terrain clipmap) drawn on Borealis.Gpu inside Borealis's frame: each
/// draws through the engine's graphics context into the list Borealis is recording, at its place in the layer, as a camera's
/// stages do (<see cref="BorealisGpuFrame.RunInFrame"/>), with the view's
/// constants and its targets. Borealis records custom objects' runs on the main thread, in segments of their own
/// (<c>MeshRenderFeature.MainThreadRuns</c>), so the graphics context, which is the backend's only one, is never shared with
/// the workers recording the meshes around them.
/// </summary>
internal static class BorealisGpuCustomDraw
{
	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );
	static BorealisGpuFrame frame;
	static BorealisGpuView view;

	/// <summary>Draw custom objects at all; off skips them, for measuring what they cost.</summary>
	internal static bool Enabled { get; set; } = true;

	/// <summary>
	/// Run <paramref name="draw"/> (a custom object's <c>RenderSceneObject</c>) into the list <paramref name="context"/> is
	/// recording, on what it has bound, as <paramref name="layerType"/> from <paramref name="camera"/>. False when there's
	/// nothing to draw into: no Borealis.Gpu backend, no colour target, or no view constants.
	/// </summary>
	public static bool Draw( GpuContext context, SceneLayerType layerType, RenderView camera, Action draw )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !Enabled ) return false;
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend || context is not BackendContext ) return false;

		var target = context.Bound;
		if ( target.Color is null || (target.Attributes as BackendParameters)?.Get( ViewConstantsName ) is not byte[] constants ) return false;

		frame ??= new BorealisGpuFrame( backend );
		view ??= new BorealisGpuView( backend );
		view.LayerType = layerType;
		var viewport = target.Viewport;
		var aspect = viewport.Height > 0 ? viewport.Width / viewport.Height : 1;
		view.SetCamera( camera.Position, camera.Rotation, camera.FieldOfView, camera.ZNear, camera.ZFar, aspect, camera.Orthographic, camera.OrthoSize );
		frame.RunInFrame( target, constants, view, first: false, draw );
		context.RestoreBound();
		return true;
	}
}
