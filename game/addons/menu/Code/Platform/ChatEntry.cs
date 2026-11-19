using Sandbox;
using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// The Multiplayer shelf's chat box - what you're saying wraps onto as many lines as it takes, the box growing with it.
/// Enter says it, as a single line's would; Shift+Enter starts a new line.
/// </summary>
public class ChatEntry : TextEntry
{
	public ChatEntry()
	{
		// Styled as any text entry is
		ElementName = "textentry";
		Multiline = true;
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( (e.Button == "enter" || e.Button == "pad_enter") && !e.HasShift )
		{
			e.StopPropagation = true;
			CreateEvent( "onsubmit", Text );
			return;
		}

		base.OnButtonTyped( e );
	}
}
