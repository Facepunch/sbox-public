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

		var graphicsView = BridgeCustomObject.ViewFor( context, view, layer, null );
		using ( new Graphics.Scope( context.NativeContext(), graphicsView ) )
		{
			if ( after ) Source.ExecuteAfter();
			else Source.ExecuteBefore();
		}
	}
}
