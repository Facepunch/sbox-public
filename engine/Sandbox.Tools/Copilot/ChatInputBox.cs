using System;

namespace Editor.Copilot;

/// <summary>
/// Multi-line text input that behaves like the VS Code chat input box:
///   • Enter           → submit
///   • Shift+Enter     → newline
///   • Ctrl+Enter      → submit (alternate)
///   • Up arrow on first line, when empty → cycle history
///   • Down arrow on last line             → cycle history forward
///
/// Also auto-grows from 1 line up to <see cref="MaxLines"/>, then scrolls.
/// </summary>
public class ChatInputBox : TextEdit
{
	public int MaxLines { get; set; } = 8;

	/// <summary>Fires when the user submits via Enter / Ctrl+Enter.</summary>
	public Action<string> OnSubmit { get; set; }

	private const int LineHeightPx = 18;
	private const int PaddingPx    = 16;

	public ChatInputBox( Widget parent ) : base( parent )
	{
		PlaceholderText = "Ask Copilot about your game…  (Shift+Enter for newline,  /  for commands,  #file:path  for context)";
		TabSize         = 16;
		MinimumHeight   = LineHeightPx + PaddingPx;
		SetStyles(
			"background: #1e1e1e; color: #d4d4d4; " +
			"font-family: Consolas, monospace; font-size: 12px; " +
			"padding: 6px; border: 1px solid rgba(255,255,255,0.10); border-radius: 4px;" );

		TextChanged += _ => AutoResize();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		var isEnter = e.Key == KeyCode.Enter || e.Key == KeyCode.Return;

		if ( isEnter && !e.HasShift )
		{
			e.Accepted = true;

			var text = PlainText?.Trim();
			if ( !string.IsNullOrEmpty( text ) )
			{
				OnSubmit?.Invoke( PlainText );
				PlainText = "";
				AutoResize();
			}
			return;
		}

		base.OnKeyPress( e );
	}

	private void AutoResize()
	{
		var text  = PlainText ?? "";
		var lines = Math.Max( 1, text.Split( '\n' ).Length );
		lines     = Math.Min( lines, MaxLines );
		MinimumHeight = lines * LineHeightPx + PaddingPx;
	}
}
