namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Draws mirrored dynamic objects from the managed copy of their vertices the object keeps on Borealis.Gpu
/// (<see cref="SceneDynamicObject.ManagedVertices"/>).
/// </summary>
internal sealed class BridgeDynamicObject : CustomObject
{
	public SceneDynamicObject Source { get; }

	Action draw;

	public BridgeDynamicObject( SceneDynamicObject source )
	{
		Source = source;
	}

	internal override void Render( GpuContext context, ViewPass view, MeshLayer layer )
	{
		if ( !Source.IsValid() ) return;

		// In colour layers through the engine's graphics context into Borealis's list, as custom objects draw, and into depth
		// alone in shadow maps and the depth prepass; the overlay layers after post processing, where native draws them (the
		// camera's output, BorealisGpuCustomDraw)
		if ( view.IsShadow || layer.Depth )
		{
			BridgeCustomObject.DrawDepthOnGpu( context, view, layer, draw ??= () => BorealisGpuCustomDraw.DrawDynamic( Source ) );
			return;
		}

		if ( (layer.Runs & (MeshRuns.OverlayWithDepth | MeshRuns.OverlayWithoutDepth)) != 0 )
		{
			BorealisGpuCustomDraw.DeferOverlay( context, view.View, (layer.Runs & MeshRuns.OverlayWithDepth) != 0, Source );
			return;
		}

		var layerType = (SceneLayerType)layer.LayerType;
		if ( layerType is not (SceneLayerType.Opaque or SceneLayerType.Translucent) ) return;
		BorealisGpuCustomDraw.Draw( context, layerType, view.View, draw ??= () => BorealisGpuCustomDraw.DrawDynamic( Source ) );
	}
}
