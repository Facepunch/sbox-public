using Sandbox.Engine;
using Sandbox.UI;

namespace Sandbox;

internal partial class UISystem
{
	/// <summary>
	/// The size we lay our root panels out at, in pixels. The game's system follows the engine
	/// swap chain, a window surface follows its window.
	/// </summary>
	internal Vector2 Size { get; set; }

	/// <summary>
	/// Desktop dpi scale of whatever we're being displayed on.
	/// </summary>
	internal float DpiScale { get; set; } = 1.0f;
}
