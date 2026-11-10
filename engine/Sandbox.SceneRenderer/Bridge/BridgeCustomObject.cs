namespace Sandbox.SceneRenderer.Bridge;

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
	/// Thread-local view prevents concurrent target/camera state changes.
	/// </summary>
	[ThreadStatic] static Graphics.ManagedView threadView;

	internal override void Render( RenderContext context, ViewPass view, MeshLayer layer )
	{
		if ( !Source.IsValid() ) return;

		// Shadow views inherit camera settings from the root view.
		var frameView = view.View.GraphicsView ?? view.Root?.GraphicsView;
		var graphicsView = threadView ??= new Graphics.ManagedView();
		graphicsView.PostProcessEnabled = frameView?.PostProcessEnabled ?? true;
		graphicsView.ToolsVisMode = frameView?.ToolsVisMode ?? 0;
		graphicsView.RenderSceneObject = frameView?.RenderSceneObject;

		var bound = context.Bound;
		var native = context.Native;

		// Inherit frame attributes.
		native.GetAttributesPtrForModify().SetParent( context.Attributes.Get() );

		graphicsView.Color = bound.Color;
		graphicsView.Depth = bound.Depth;
		graphicsView.DepthSlice = bound.DepthSlice;
		graphicsView.SrgbWrite = bound.SrgbWrite;
		graphicsView.Viewport = bound.Viewport;
		graphicsView.MinZ = bound.MinZ;
		graphicsView.MaxZ = bound.MaxZ;
		graphicsView.ColorFormat = bound.ColorFormat;
		graphicsView.Msaa = bound.Samples switch
		{
			2 => MultisampleAmount.Multisample2x,
			4 => MultisampleAmount.Multisample4x,
			8 => MultisampleAmount.Multisample8x,
			_ => MultisampleAmount.MultisampleNone,
		};
		// Shadow billboards face the light and use depth mode.
		graphicsView.LayerType = layer.LayerType;
		graphicsView.ShaderMode = layer.ShaderMode;
		graphicsView.SetupLighting = view.IsShadow ? null : setupLighting ??= SetupLighting;

		var camera = view.View;
		var viewport = bound.Viewport;
		var aspect = viewport.Height > 0 ? viewport.Width / viewport.Height : 1;
		graphicsView.SetCamera( camera.Position, camera.Rotation, camera.FieldOfView, camera.ZNear, camera.ZFar, aspect, camera.Orthographic, camera.OrthoSize.x, camera.OrthoSize.y );

		using ( new Graphics.Scope( native, graphicsView ) )
		{
			Source.RenderInternal();
		}
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

		var choice = LightProbeVolume.Choose( volumes, center, center, extents, lightGroup );
		if ( choice >= 0 ) volumes[choice].Attributes.MergeTo( attributes );
	}

	static readonly StringToken LightGroupName = new( "LightGroup" );
}
