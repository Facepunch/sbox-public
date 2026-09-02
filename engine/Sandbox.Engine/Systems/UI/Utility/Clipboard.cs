
namespace Sandbox.UI;

public static class Clipboard
{
	/// <summary>
	/// Sets the clipboard text
	/// </summary>
	public static void SetText( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return;

		NativeEngine.EngineGlobal.SDL_SetClipboardText( text );
	}
}
