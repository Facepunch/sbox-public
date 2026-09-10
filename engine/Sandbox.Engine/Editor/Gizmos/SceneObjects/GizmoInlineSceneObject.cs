using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Draws anything
/// </summary>
internal class GizmoInlineSceneObject : SceneCustomObject
{
	public CommandList CommandList { get; } = new( "GizmoInline" );

	public GizmoInlineSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
	}

	public override void RenderSceneObject()
	{
		CommandList.ExecuteOnRenderThread();
	}
}
