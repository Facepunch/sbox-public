using System;

namespace Editor.Copilot;

/// <summary>
/// A read-only code-block widget styled like VS Code's chat code blocks.
///
///  ┌─────────────────────────────────────────┐
///  │ csharp                  [Copy] [Insert] │   ← header bar
///  ├─────────────────────────────────────────┤
///  │ public void Hello() {                   │   ← monospace text
///  │     Log.Info( "hi" );                   │
///  │ }                                       │
///  └─────────────────────────────────────────┘
/// </summary>
public class CodeBlockWidget : Widget
{
	private readonly Label    _langLabel;
	private readonly TextEdit _editor;
	private          string   _code = "";

	/// <summary>Optional handler invoked when the user presses "Insert".</summary>
	public Action<string> OnInsert { get; set; }

	public CodeBlockWidget( string language, Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin  = 0;
		Layout.Spacing = 0;

		SetStyles( "border: 1px solid rgba(255,255,255,0.10); border-radius: 4px; background: #1e1e1e;" );

		// ── header ────────────────────────────────────────────────────────────
		var header   = new Widget( this );
		header.Layout = Layout.Row();
		header.Layout.Margin  = new Margin( 8, 4, 4, 4 );
		header.Layout.Spacing = 4;
		header.FixedHeight    = 26;
		header.SetStyles( "background: rgba(255,255,255,0.04); border-top-left-radius: 4px; border-top-right-radius: 4px;" );

		_langLabel = new Label( string.IsNullOrWhiteSpace( language ) ? "code" : language, header );
		_langLabel.SetStyles( "color: rgba(255,255,255,0.55); font-size: 10px; font-family: Consolas,monospace;" );
		header.Layout.Add( _langLabel );

		header.Layout.AddStretchCell();

		var copyBtn = new Button( "", header )
		{
			Icon    = "content_copy",
			ToolTip = "Copy code",
			Clicked = OnCopyClicked
		};
		copyBtn.FixedHeight = 22;
		header.Layout.Add( copyBtn );

		var insertBtn = new Button( "", header )
		{
			Icon    = "playlist_add",
			ToolTip = "Insert into editor (opens in code editor)",
			Clicked = OnInsertClicked
		};
		insertBtn.FixedHeight = 22;
		header.Layout.Add( insertBtn );

		Layout.Add( header );

		// ── editor ────────────────────────────────────────────────────────────
		_editor = new TextEdit( this )
		{
			ReadOnly = true,
			TabSize  = 16
		};
		_editor.SetStyles(
			"background: #1e1e1e; color: #d4d4d4; " +
			"font-family: Consolas, 'Courier New', monospace; font-size: 12px; " +
			"padding: 6px; border: 0;" );

		Layout.Add( _editor );
	}

	/// <summary>
	/// Update the code shown.  Cheap to call repeatedly during streaming.
	/// </summary>
	public void SetCode( string code )
	{
		if ( code == _code ) return;
		_code = code ?? "";
		_editor.PlainText = _code;

		// Auto-size to fit content (cap at 400px so very long blocks scroll)
		var lines  = _code.Length == 0 ? 1 : _code.Split( '\n' ).Length;
		var height = Math.Min( 400, 26 + 14 + lines * 16 );
		_editor.MinimumHeight = Math.Max( 40, height - 26 );
	}

	public void SetLanguage( string language )
	{
		_langLabel.Text = string.IsNullOrWhiteSpace( language ) ? "code" : language;
	}

	private void OnCopyClicked()
	{
		EditorUtility.Clipboard.Copy( _code );
	}

	private void OnInsertClicked()
	{
		OnInsert?.Invoke( _code );
	}
}
