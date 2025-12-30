using Sandbox.UI;

namespace Sandbox;

public partial class Bitmap
{
	/// <summary>
	/// Draws text onto this bitmap
	/// </summary>
	public void DrawText( TextRendering.Scope scope, Rect rect, TextFlag flags = TextFlag.Center )
	{
		using var block = new TextRendering.TextBlock( scope, new Vector2( 0, 0 ), flags );
		block.Render( _canvas, rect );
	}
}
