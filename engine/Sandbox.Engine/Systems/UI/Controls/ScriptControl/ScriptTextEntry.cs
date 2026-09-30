using System;

namespace Sandbox.UI;

internal sealed partial class ScriptTextEntry : TextEntry
{
	internal ScriptControl Editor { get; }

	public ScriptTextEntry( ScriptControl editor )
	{
		Editor = editor;

		// TextEntry has no label factory yet; replace its plain label with our diagnostic decoration label.
		Label.Delete( true );
		Label = new ScriptCodeLabel( this )
		{
			Parent = this,
			Tokenize = false
		};
		Label.AddClass( "content-label" );
		Label.Style.WhiteSpace = WhiteSpace.Pre;

		Multiline = true;
		AddClass( "script-entry" );
	}

	// TextEntry selections use text elements; script analysis uses string offsets.
	internal int SelectionStart => Editor.Document.ToOffset( Math.Min( Label.SelectionStart, Label.SelectionEnd ) );
	internal int SelectionEnd => Editor.Document.ToOffset( Math.Max( Label.SelectionStart, Label.SelectionEnd ) );
	internal bool Selected => Label.SelectionStart != Label.SelectionEnd;

	internal void SelectElements( int start, int end )
	{
		Label.ShouldDrawSelection = true;
		Label.SetSelection( start, end );
		CaretPosition = end;
	}

	internal Rect ElementRect( int element ) => Label.GetCaretRect( element );
	internal void RebuildColors( bool resetStyles = false ) => ((ScriptCodeLabel)Label).Rebuild( resetStyles );

	public override void OnDraw( Painter painter )
	{
		if ( HasFocus && !Selected )
		{
			DrawCurrentLine( painter );
		}

		base.OnDraw( painter );
	}

	void DrawCurrentLine( Painter painter )
	{
		using var scope = painter.Scope();
		var caret = ElementRect( CaretPosition );
		var line = new Rect( 0, caret.Top - Box.Rect.Top, Box.Rect.Width, caret.Height );

		painter.Clip( new Rect( Vector2.Zero, Box.Rect.Size ) );
		painter.Fill = Editor.Theme.CurrentLine;
		painter.Stroke = Stroke.None;
		painter.Rect( line );
	}

	public override void OnValueChanged()
	{
		base.OnValueChanged();
		Editor.SourceChanged();
	}

	protected override void OnEvent( PanelEvent e )
	{
		base.OnEvent( e );
		// IME previews change the displayed text without raising OnValueChanged.
		if ( e.Name is "onimestart" or "onime" or "onimeend" )
		{
			Editor.SourceChanged();
		}
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		var screenPosition = PanelPositionToScreenPosition( MousePosition );
		var element = Label.GetCharacterAtScreenPosition( screenPosition );
		Editor.SetHoverOffset( element < 0 ? -1 : Editor.Document.ToOffset( element ) );
	}

	protected override void OnMouseOut( MousePanelEvent e )
	{
		base.OnMouseOut( e );
		Editor.CloseHover();
	}

	public override void OnMouseWheel( Vector2 value )
	{
		Editor.DismissIntelliSense();
		if ( UISystem?.Input.WheelModifiers.Contains( KeyboardModifiers.Ctrl ) == true )
		{
			var size = Editor.ComputedStyle.FontSize?.GetPixels( 100 ) ?? 13f;
			Editor.Style.FontSize = Math.Clamp( size - value.y, 8f, 48f );
			return;
		}

		base.OnMouseWheel( value );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		Editor.DismissIntelliSense();
		base.OnMouseDown( e );
	}

	protected override void OnEscape( PanelEvent e )
	{
		Editor.DismissIntelliSense();
		e.StopPropagation();
	}
}
