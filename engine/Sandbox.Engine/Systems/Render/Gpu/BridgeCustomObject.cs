using Borealis;
using GpuContext = Borealis.Gpu.GpuContext;

namespace Sandbox.Rendering;

/// <summary>
/// Mirrored custom object, drawn through the engine's graphics context into Borealis's list, shadow views included
/// (<c>CManagedRendererDesc::DrawArray</c>).
/// </summary>
internal sealed class BridgeCustomObject : CustomObject
{
	public SceneCustomObject Source { get; }

	public BridgeCustomObject( SceneCustomObject source )
	{
		Source = source;
	}

	// The source's draw as a delegate, made once rather than every draw
	Action render;

	internal override void Render( GpuContext context, ViewPass view, MeshLayer layer )
	{
		if ( !Source.IsValid() ) return;

		// The overlay layers draw after post processing, where native draws them (the camera's output)
		if ( !view.IsShadow && (layer.Runs & (MeshRuns.OverlayWithDepth | MeshRuns.OverlayWithoutDepth)) != 0 )
		{
			BorealisGpuCustomDraw.DeferOverlay( context, view.View, (layer.Runs & MeshRuns.OverlayWithDepth) != 0, Source );
			return;
		}

		// Into depth alone in shadow maps and the depth prepass, with the layer's mode and the shadow view's depth bias
		var layerType = (SceneLayerType)layer.LayerType;
		if ( layer.Depth || view.IsShadow )
		{
			DrawDepthOnGpu( context, view, layer, render ??= Source.RenderInternal );
			return;
		}

		if ( layerType is not (SceneLayerType.Opaque or SceneLayerType.Translucent) ) return;
		BorealisGpuCustomDraw.Draw( context, layerType, view.View, render ??= Source.RenderInternal, cameraAttributes: CameraAttributes( view ), lightingWorld: view.World, subWorlds: view.View.SubWorlds );
	}

	/// <summary>
	/// A run of mirrored custom objects in one layer, drawn in one lent block on Borealis.Gpu (<see cref="BorealisGpuCustomDraw.DrawRun"/>)
	/// rather than a block each: in colour and depth layers alike. Overlay layers keep each for the camera's output.
	/// </summary>
	internal override void RenderRun( GpuContext context, ViewPass view, MeshLayer layer, ReadOnlySpan<CustomDraw> run )
	{
		var started = System.Diagnostics.Stopwatch.GetTimestamp();
		var collections = GC.CollectionCount( 0 );
		try
		{
			RenderRunTimed( context, view, layer, run );
		}
		finally
		{
			var ms = System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			if ( ms > SlowDrawMs ) Log.Warning( SlowDraw( Source, run.Length, view.IsShadow, layer.LayerType, ms, GC.CollectionCount( 0 ) - collections ) );
		}
	}

	/// <summary>A custom object's draw (its run's) longer than this is logged with its type (<see cref="SlowDraw"/>).</summary>
	internal const double SlowDrawMs = 50;

	/// <summary>
	/// What's logged for a custom object's draw over <see cref="SlowDrawMs"/>: the engine's type of the object (a world
	/// panel, a line renderer, a game's own), so a long frame's <c>Borealis Custom Object</c> zone has a name.
	/// </summary>
	internal static string SlowDraw( SceneCustomObject source, int count, bool shadow, MeshLayerType layer, double ms, int collections )
		=> $"Borealis custom object {source?.GetType().FullName ?? "(gone)"}{(count > 1 ? $" (and {count - 1} more in its run)" : "")} took {ms:F1} ms to draw in {(shadow ? "a shadow view's" : "a view's")} {layer} layer, {collections} gen0 collections during it";

	void RenderRunTimed( GpuContext context, ViewPass view, MeshLayer layer, ReadOnlySpan<CustomDraw> run )
	{
		var overlay = !view.IsShadow && (layer.Runs & (MeshRuns.OverlayWithDepth | MeshRuns.OverlayWithoutDepth)) != 0;
		if ( run.Length == 1 || overlay )
		{
			base.RenderRun( context, view, layer, run );
			return;
		}

		var layerType = (SceneLayerType)layer.LayerType;
		if ( layer.Depth || view.IsShadow )
		{
			var shadow = layer.LayerType == MeshLayerType.Shadow;
			BorealisGpuCustomDraw.DrawRun( context, run, layerType, view.View, layer.ShaderMode, shadow ? view.DepthBias : 0, shadow ? view.SlopeScaledDepthBias : 0, CameraAttributes( view ) );
			return;
		}

		if ( layerType is not (SceneLayerType.Opaque or SceneLayerType.Translucent) ) return;
		BorealisGpuCustomDraw.DrawRun( context, run, layerType, view.View, cameraAttributes: CameraAttributes( view ), lightingWorld: view.World, subWorlds: view.View.SubWorlds );
	}

	/// <summary>
	/// The attributes of the camera <paramref name="view"/> draws for (<c>SceneCamera.Attributes</c>, with what
	/// <c>Scene.RenderAttributes</c> set): a shadow view's are its main view's, as the meshes in its shadow maps read them.
	/// </summary>
	internal static RenderAttributes CameraAttributes( ViewPass view ) => (view.Root ?? view.View)?.HostView as RenderAttributes;

	/// <summary>
	/// A custom or dynamic object's <paramref name="draw"/> on Borealis.Gpu in a depth layer (a shadow map, the depth prepass):
	/// into the bound depth alone, in the layer's mode, with a shadow view's depth bias, as the layer's meshes draw, and with
	/// the camera's attributes as its colour draw has them, so a vertex shader animated by them (Mouse Fishing's clutter grass
	/// sways by <c>Scene.RenderAttributes</c>' time) puts the same depth where its colour lands.
	/// </summary>
	internal static void DrawDepthOnGpu( GpuContext context, ViewPass view, MeshLayer layer, Action draw )
	{
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		BorealisGpuCustomDraw.Draw( context, (SceneLayerType)layer.LayerType, view.View, draw, layer.ShaderMode, shadow ? view.DepthBias : 0, shadow ? view.SlopeScaledDepthBias : 0, CameraAttributes( view ) );
	}
}
