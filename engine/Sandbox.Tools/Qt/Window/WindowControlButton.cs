using System;

namespace Editor;

internal class WindowControlButton : Widget
{
	private static string SymbolFont = GetFont();

	private static string GetFont()
	{
		// On Windows 11 we should be using 'Segoe Fluent Icons', but this isn't available on Windows 10.
		// Version major and minor are 10.0 in both 10 and 11, confusingly, so we use the build number;
		// Windows 11 starts at build number 22000, Windows 10 ends at 21390
		if ( Environment.OSVersion.Version.Build >= 22000 )
			return "Segoe Fluent Icons";

		return "Segoe MDL2 Assets";
	}

	private Action _onClick;
	private WindowControlIcon _icon;
	public WindowControlIcon Icon
	{
		get => _icon;
		set
		{
			if ( _icon == value )
				return;

			_icon = value;
			Update();
		}
	}

	public Color HighlightColor { get; set; } = Theme.Text.WithAlpha( 0.1f );

	public WindowControlButton( WindowControlIcon icon, Action onClick = null )
	{
		_onClick = onClick;

		Icon = icon;
		FixedSize = new Vector2( 40, 32 );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		_onClick?.Invoke();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();

		if ( Paint.HasMouseOver && Enabled )
		{
			Paint.SetBrush( HighlightColor );
			Paint.DrawRect( LocalRect );
		}

		Paint.ClearBrush();
		Paint.SetFont( SymbolFont, 7.0f );
		Paint.SetPen( Theme.Text );

		if ( !Enabled )
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );

		Paint.DrawText( LocalRect, new string( (char)Icon, 1 ) );
	}
}
