namespace Sandbox;

public partial class DebugOverlaySystem
{
	public void Texture( Vector2 pixelPosition, Texture texture, Vector2 size, float duration = 0 )
	{
		var so = new DebugTextureSceneObject( Scene.SceneWorld );
		so.ScreenPos = pixelPosition;
		so.ScreenSize = size;
		so.Flags.CastShadows = false;
		so.RenderLayer = SceneRenderLayer.OverlayWithoutDepth;

		Add( duration, so );
	}
}

file class DebugTextureSceneObject : SceneCustomObject
{
	public Texture Texture;
	public Vector2 ScreenPos;
	public Vector2 ScreenSize;

	Material material;

	public DebugTextureSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		material = Material.FromShader( "shaders/Debug/screen_texture.shader" );
	}

	public override void RenderSceneObject()
	{
		var rect = new Rect( ScreenPos, ScreenSize );

		var attributes = new RenderAttributes();
		attributes.Set( "Texture", Texture );

		Graphics.DrawQuad( rect, material, Color.White, attributes );
	}
}
