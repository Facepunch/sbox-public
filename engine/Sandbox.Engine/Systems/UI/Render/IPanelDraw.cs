using Sandbox.Rendering;

namespace Sandbox.UI;

/// <summary>
/// Implement on a <see cref="Panel"/> to issue custom GPU commands into the UI rendering pipeline.
/// </summary>
public interface IPanelDraw
{
	void Draw( CommandList cl );
}
