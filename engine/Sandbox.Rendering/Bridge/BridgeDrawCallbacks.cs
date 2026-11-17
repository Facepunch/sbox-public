namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A mirrored object's own command lists (<c>Renderer.ExecuteBefore</c> and <c>ExecuteAfter</c>), run around its draws
/// in a <c>Graphics</c> scope of the layer, as native's batch lists call <c>SceneSystem.OnBeforeRender</c> and
/// <c>OnAfterRender</c>.
/// </summary>
internal sealed class BridgeDrawCallbacks : DrawCallbacks
{
	public SceneObject Source { get; }

	public BridgeDrawCallbacks( SceneObject source )
	{
		Source = source;
	}

	internal override void Render( GpuContext context, ViewPass view, MeshLayer layer, bool after )
	{
		if ( !Source.IsValid() ) return;

		// Through the engine's graphics context into Borealis's list, as custom objects draw
		var layerType = (SceneLayerType)layer.LayerType;
		if ( view.IsShadow || layer.Depth || layerType is not (SceneLayerType.Opaque or SceneLayerType.Translucent) ) return;
		RenderOnGpu( context, layerType, view.View, after );
	}

	// The context a command list ran for; callbacks run on the main thread
	static GpuContext running;
	Action runBefore, runAfter;

	/// <summary>
	/// The command list in a block on the list <paramref name="context"/> records, then what it set on the block's attributes
	/// set on the context's own, for the object's draws after it, as native's command lists write the layer context's
	/// attributes, which those draws read (a water body's refraction texture and viewport).
	/// </summary>
	void RenderOnGpu( GpuContext context, SceneLayerType layerType, RenderView camera, bool after )
	{
		var run = after ? runAfter ??= () => Run( Source.ExecuteAfter ) : runBefore ??= () => Run( Source.ExecuteBefore );
		var previous = running;
		running = context;
		try
		{
			BorealisGpuCustomDraw.Draw( context, layerType, camera, run );
		}
		finally
		{
			running = previous;
		}
	}

	static void Run( Action commands )
	{
		var attributes = Graphics.Attributes.Managed;
		if ( attributes is null )
		{
			commands();
			return;
		}

		var before = attributes.Version;
		commands();
		if ( running is { } context ) MirrorContent.SetChanged( attributes, before, context.Attributes );
	}
}
