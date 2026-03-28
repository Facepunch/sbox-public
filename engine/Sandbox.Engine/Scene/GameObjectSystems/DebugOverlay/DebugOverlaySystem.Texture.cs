namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Draw a texture on the screen
	/// </summary>
	public void Texture( Texture texture, Vector2 position, Color? color = default, float duration = 0 )
	{
		var so = new QuadSceneObject( Scene.SceneWorld );
		so.ColorTint = color ?? Color.White;
		so.ScreenRect = new Rect( position, texture.Size );
		so.Flags.CastShadows = false;
		so.RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
		so.Texture = texture;

		Add( duration, so );
	}

	/// <summary>
	/// Draw a texture on the screen
	/// </summary>
	public void Texture( Texture texture, Rect screenRect, Color? color = default, float duration = 0 )
	{
		var so = new QuadSceneObject( Scene.SceneWorld );
		so.ColorTint = color ?? Color.White;
		so.ScreenRect = screenRect;
		so.Flags.CastShadows = false;
		so.RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
		so.Texture = texture;

		Add( duration, so );
	}

	public void ScreenTexture( Vector3 worldPos, Texture texture, Vector2 size, float duration = 0 )
	{
		var so = new ScreenTextureSceneObject( Scene.SceneWorld );
		so.WorldPos = worldPos;
		so.Texture = texture;
		so.Size = size;

		Add( duration, so );
	}
}

file class ScreenTextureSceneObject : SceneCustomObject
{
	public Vector3 WorldPos { get; set; }
	public Texture Texture { get; set; }
	public Vector2 Size { get; set; }

	public ScreenTextureSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
		managedNative.ExecuteOnMainThread = false;
	}

	static bool ToScreenWithDirection( Vector3 world, out Vector2 screen )
	{
		var frustum = Graphics.SceneView.GetFrustum();
		var behind = frustum.ScreenTransform( world, out var result );
		var x = (result.x + 1f) / 2f;
		var y = ((result.y * -1f) + 1f) / 2f;

		var size = Graphics.Viewport.Size;
		screen = new Vector2( x, y ) * size;

		return behind;
	}

	public override void RenderSceneObject()
	{
		if ( ToScreenWithDirection( WorldPos, out var screen ) )
			return;

		screen -= Size * 0.5f;
		var rect = new Rect( screen, Size );

		Attributes.Set( "Texture", Texture );
		Graphics.DrawQuad( rect, Material.UI.Basic, ColorTint, Attributes );
	}
}
