namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Mirrored custom object drawn in a Graphics scope, including shadow views (<c>CManagedRendererDesc::DrawArray</c>).
/// </summary>
internal sealed class BridgeCustomObject : CustomObject
{
	public SceneCustomObject Source { get; }

	public BridgeCustomObject( SceneCustomObject source )
	{
		Source = source;
	}

	/// <summary>
	/// Thread-local views prevent concurrent target/camera state changes, one for each child view deep
	/// (<see cref="SceneBridge.ChildDepth"/>): a child view rendered from an object's command list draws its own objects meanwhile.
	/// </summary>
	[ThreadStatic] static Graphics.ManagedView[] threadViews;

	internal override void Render( GpuContext context, ViewPass view, MeshLayer layer )
	{
		if ( !Source.IsValid() ) return;

		// On Borealis.Gpu: through the engine's graphics context into Borealis's list, in colour layers, and into depth alone
		// in shadow maps and the depth prepass, with the layer's mode and the shadow view's depth bias
		if ( MirrorContent.Gpu is not null )
		{
			// The overlay layers draw after post processing, where native draws them (the camera's output)
			if ( !view.IsShadow && (layer.Runs & (MeshRuns.OverlayWithDepth | MeshRuns.OverlayWithoutDepth)) != 0 )
			{
				BorealisGpuCustomDraw.DeferOverlay( context, view.View, (layer.Runs & MeshRuns.OverlayWithDepth) != 0, Source );
				return;
			}

			var layerType = (SceneLayerType)layer.LayerType;
			if ( layer.Depth || view.IsShadow )
			{
				DrawDepthOnGpu( context, view, layer, Source.RenderInternal );
				return;
			}

			if ( layerType is not (SceneLayerType.Opaque or SceneLayerType.Translucent) ) return;
			BorealisGpuCustomDraw.Draw( context, layerType, view.View, Source.RenderInternal );
			return;
		}

		var graphicsView = ViewFor( context, view, layer, view.IsShadow ? null : setupLighting ??= SetupLighting );
		using ( new Graphics.Scope( context.NativeContext(), graphicsView ) )
		{
			Source.RenderInternal();
		}
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

	/// <summary>
	/// This thread's managed view, pointed at what's bound for a layer of a view, with the frame's attributes inherited:
	/// what a native layer's <c>Graphics.Scope</c> gives drawing code.
	/// </summary>
	internal static Graphics.ManagedView ViewFor( GpuContext context, ViewPass view, MeshLayer layer, Action<SceneObject, RenderAttributes> setupLighting )
	{
		// Shadow views inherit camera settings from the root view.
		var frameView = (view.View.HostView ?? view.Root?.HostView) as Graphics.ManagedView;
		threadViews ??= new Graphics.ManagedView[SceneBridge.MaxChildDepth + 1];
		var graphicsView = threadViews[SceneBridge.ChildDepth] ??= new Graphics.ManagedView();
		graphicsView.PostProcessEnabled = frameView?.PostProcessEnabled ?? true;
		graphicsView.ToolsVisMode = frameView?.ToolsVisMode ?? 0;
		graphicsView.RenderSceneObject = frameView?.RenderSceneObject;
		graphicsView.Renderer = frameView?.Renderer;
		graphicsView.IsChild = frameView?.IsChild ?? false;

		var bound = context.Bound;
		var native = context.NativeContext();

		// Inherit frame attributes.
		native.GetAttributesPtrForModify().SetParent( context.Attributes.Engine().Get() );

		graphicsView.Color = bound.Color.Native();
		graphicsView.Depth = bound.Depth.Native();
		graphicsView.DepthSlice = bound.DepthSlice;
		graphicsView.SrgbWrite = bound.SrgbWrite;
		graphicsView.Viewport = bound.Viewport;
		graphicsView.MinZ = bound.MinZ;
		graphicsView.MaxZ = bound.MaxZ;
		graphicsView.ColorFormat = Source2Formats.ToEngineOrDefault( bound.ColorFormat );
		graphicsView.Msaa = bound.Samples switch
		{
			2 => MultisampleAmount.Multisample2x,
			4 => MultisampleAmount.Multisample4x,
			8 => MultisampleAmount.Multisample8x,
			_ => MultisampleAmount.MultisampleNone,
		};
		// Shadow billboards face the light and use depth mode.
		graphicsView.LayerType = (SceneLayerType)layer.LayerType;
		graphicsView.ShaderMode = layer.ShaderMode;
		graphicsView.SetupLighting = setupLighting;

		var camera = view.View;
		var viewport = bound.Viewport;
		var aspect = viewport.Height > 0 ? viewport.Width / viewport.Height : 1;
		graphicsView.SetCamera( camera.Position, camera.Rotation, camera.FieldOfView, camera.ZNear, camera.ZFar, aspect, camera.Orthographic, camera.OrthoSize.x, camera.OrthoSize.y );
		return graphicsView;
	}

	Action<SceneObject, RenderAttributes> setupLighting;

	/// <summary>
	/// Select probe lighting by bounds and light group (<c>SetupPerObjectLighting</c>).
	/// </summary>
	void SetupLighting( SceneObject sceneObject, RenderAttributes attributes )
	{
		var volumes = World?.LightProbeVolumes;
		if ( volumes is null || volumes.Count == 0 || !sceneObject.IsValid() ) return;

		var bounds = sceneObject.Bounds;
		var center = bounds.Center;
		var extents = bounds.Size * 0.5f;
		var lightGroup = (uint)sceneObject.native.GetIntValue( LightGroupName, 0 );

		var choice = Borealis.LightProbeVolume.Choose( volumes, center, center, extents, lightGroup );
		if ( choice >= 0 ) volumes[choice].Attributes.Engine().MergeTo( attributes );
	}

	static readonly StringToken LightGroupName = new( "LightGroup" );
}
