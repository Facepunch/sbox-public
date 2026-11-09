using Sandbox.Rendering;
using Sandbox.UI;

namespace Sandbox.SceneLab;

/// <summary>
/// The managed renderer's debug views' key and labels over the viewport (<c>+r_managed_debug_lod 1</c>,
/// <c>+r_managed_debug_cull 1</c>, <c>+r_managed_debug_shadows 1</c>, or the View menu): what the dev tools' overlay draws
/// in a game, drawn by a panel here, as a panel app has no engine overlay. The renderer draws the tints and thumbnails into
/// the frame; a capture with a view on saves the frame with these over it as <c>name.debug.png</c>.
/// </summary>
internal sealed class DebugViewsOverlay : Panel
{
	public DebugViewsOverlay()
	{
		AddClass( "scenelab-plan" );
	}

	public override void Tick()
	{
		base.Tick();
		SetClass( "hidden", !DebugOverlay.ManagedViews.Showing );
	}

	public override void OnDraw( Painter painter )
	{
		base.OnDraw( painter );
		if ( !DebugOverlay.ManagedViews.Showing ) return;

		DrawViews( painter, Box.Rect );
	}

	/// <summary>
	/// The occlusion boxes when the cull reasons view is on, then the key at the top right, and the labels over the view in
	/// <paramref name="area"/>.
	/// </summary>
	static void DrawViews( Painter painter, Rect area )
	{
		var pos = new Vector2( area.Right - 272, area.Top + 12 );
		if ( ManagedDebugViews.CullReasons && OcclusionDebug.Current is not null )
		{
			DebugOverlay.OcclusionCulling.Draw( painter, ref pos, area );
			pos.y += 12;
		}

		DebugOverlay.ManagedViews.Draw( painter, ref pos, ManagedDebugViews.Latest, area );
	}

	/// <summary>
	/// <paramref name="pixels"/>, a captured frame, with the key and labels over it, for a capture to save.
	/// </summary>
	public static Color32[] Render( Color32[] pixels, int width, int height )
	{
		using var bitmap = new Bitmap( width, height );
		bitmap.SetPixels( pixels.Select( x => x.ToColor() ).ToArray() );
		using var frame = bitmap.ToTexture( mips: false );

		using var texture = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).Create( "Scene Lab debug views" );
		using ( var painter = Painter.Begin( texture ) )
		{
			var area = new Rect( 0, 0, width, height );
			painter.Texture( frame, area, Color.White, FilterMode.Point );
			DrawViews( painter, area );
		}

		return texture.GetPixels();
	}
}
