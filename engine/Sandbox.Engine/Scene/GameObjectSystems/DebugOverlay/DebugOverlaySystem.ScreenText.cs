namespace Sandbox;

public partial class DebugOverlaySystem
{
	/// <summary>
	/// Draw text on the screen
	/// </summary>
	public void ScreenText( Vector2 pixelPosition, string text, float size = 14, TextFlag flags = TextFlag.Center, Color color = new Color(), float duration = 0 )
	{
		if ( color == default ) color = Color.White;

		var scope = new TextRendering.Scope( text, color, size, weight: 500 );
		scope.Shadow = new TextRendering.Shadow { Enabled = true, Color = Color.Black, Offset = 3, Size = 1 };

		ScreenText( pixelPosition, scope, flags, duration );
	}

	/// <summary>
	/// Draw text on the screen
	/// </summary>
	public void ScreenText( Vector2 pixelPosition, TextRendering.Scope textBlock, TextFlag flags = TextFlag.Center, float duration = 0 )
	{
		var so = new TextSceneObject( Scene.SceneWorld );
		so.ScreenPos = pixelPosition;
		so.ScreenSize = Screen.Size; // probably
		so.Flags.CastShadows = false;
		so.RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
		so.TextBlock = textBlock;
		so.TextFlags = flags;
		so.BuildCommandList();

		Add( duration, so );
	}

	internal void ScreenText( Vector3 position, TextRendering.Scope scope )
	{
		Add( 0.0f, new ScreenTextSceneObject( Scene.SceneWorld )
		{
			TextBlock = scope,
			ScreenPos = position,
		} );
	}
}
file class ScreenTextSceneObject : SceneCustomObject
{
	public Vector3 ScreenPos { get; set; }
	public TextRendering.Scope TextBlock;

	public ScreenTextSceneObject( SceneWorld sceneWorld ) : base( sceneWorld )
	{
		RenderLayer = SceneRenderLayer.OverlayWithoutDepth;
		Flags.CastShadows = false;
		TextBlock = TextRendering.Scope.Default;
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
		if ( ToScreenWithDirection( ScreenPos, out var screen ) )
			return;

		var size = Graphics.Viewport.Size;
		screen -= size * 0.5f;
		var rect = new Rect( screen, size );
		Graphics.DrawText( rect, TextBlock );
	}
}
