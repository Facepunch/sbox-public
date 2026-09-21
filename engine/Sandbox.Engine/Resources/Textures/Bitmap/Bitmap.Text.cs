using Sandbox.UI;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Draws text onto this bitmap. The text is rendered on the GPU and read back.
	/// </summary>
	public void DrawText( TextRendering.Scope scope, Rect rect, TextFlag flags = TextFlag.Center )
	{
		var block = TextRendering.GetOrCreateTextBlock( scope, flags, rect.Size );
		if ( block is null || block.IsEmpty ) return;

		block.MakeReady();
		if ( block.Texture is null ) return;

		// Preserve HDR colours when reading back a floating point text texture.
		using var text = block.Texture.GetBitmap( 0 );
		if ( text is null ) return;

		// Align the layout, then account for its origin inside the padded texture.
		var size = new Vector2( block.Layout.MeasuredWidth, block.Layout.MeasuredHeight );
		var position = rect.Align( size, flags ).Position - block.BlockOrigin;

		// Text uses the scope's colours and opacity, independently of the current pen.
		_canvas.DrawBitmap( text._bitmap, position.x, position.y );
	}
}
