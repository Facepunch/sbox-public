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
		BorealisGpuCustomDraw.Draw( context, layerType, view.View, render ??= Source.RenderInternal, cameraAttributes: view.View.HostView as RenderAttributes );
	}

	/// <summary>
	/// A custom or dynamic object's <paramref name="draw"/> on Borealis.Gpu in a depth layer (a shadow map, the depth prepass):
	/// into the bound depth alone, in the layer's mode, with a shadow view's depth bias, as the layer's meshes draw.
	/// </summary>
	internal static void DrawDepthOnGpu( GpuContext context, ViewPass view, MeshLayer layer, Action draw )
	{
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		BorealisGpuCustomDraw.Draw( context, (SceneLayerType)layer.LayerType, view.View, draw, layer.ShaderMode, shadow ? view.DepthBias : 0, shadow ? view.SlopeScaledDepthBias : 0 );
	}
}
