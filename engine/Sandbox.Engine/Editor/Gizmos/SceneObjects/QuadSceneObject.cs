namespace Sandbox;

/// <summary>
/// Draws text in screenspace
/// </summary>
internal class QuadSceneObject : SceneCustomObject
{
	public Rect ScreenRect { get; set; }
	public Texture Texture { get; set; }

	public QuadSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
	}

	public override void RenderSceneObject()
	{
		Attributes.Set( "Texture", Texture );
		Graphics.DrawQuad( ScreenRect, Material.UI.Basic, ColorTint, Attributes );
	}
}
