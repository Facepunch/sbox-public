namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Draws mirrored dynamic objects from live native vertices (<c>RenderTools.DrawDynamicSceneObject</c>), or on Borealis.Gpu from
/// the managed copy the object keeps there (<see cref="SceneDynamicObject.ManagedVertices"/>).
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

		// On Borealis.Gpu: in colour layers through the engine's graphics context into Borealis's list, as custom objects draw;
		// the overlay layers after post processing, where native draws them (the camera's output, BorealisGpuCustomDraw)
		if ( MirrorContent.Gpu is not null )
		{
			if ( view.IsShadow || layer.Depth ) return;
			if ( (layer.Runs & (MeshRuns.OverlayWithDepth | MeshRuns.OverlayWithoutDepth)) != 0 )
			{
				BorealisGpuCustomDraw.DeferOverlay( context, view.View, (layer.Runs & MeshRuns.OverlayWithDepth) != 0, Source );
				return;
			}

			var layerType = (SceneLayerType)layer.LayerType;
			if ( layerType is not (SceneLayerType.Opaque or SceneLayerType.Translucent) ) return;
			BorealisGpuCustomDraw.Draw( context, layerType, view.View, draw ??= () => BorealisGpuCustomDraw.DrawDynamic( Source ) );
			return;
		}

		// Inherit frame attributes.
		var native = context.NativeContext();
		native.GetAttributesPtrForModify().SetParent( context.Attributes.Engine().Get() );

		SceneNative.DrawDynamicObject( context, Source, layer.ShaderMode );
	}
}
