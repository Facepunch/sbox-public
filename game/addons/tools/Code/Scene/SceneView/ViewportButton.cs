namespace Editor;

internal class ViewportButton : Widget
{
	private string Icon;
	private Action OnClick;

	public Color Color { get; set; } = Theme.TextLight;

	public ViewportButton( string icon, Action onClick ) : base( null )
	{
		Icon = icon;
		OnClick = onClick;

		FixedWidth = Theme.ControlHeight;
		FixedHeight = Theme.ControlHeight;
		Cursor = CursorShape.Finger;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton && Enabled )
		{
			e.Accepted = true;
			Activate();
		}
	}

	public void Activate()
	{
		if ( !Enabled )
			return;

		OnClick();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Paint.ClearBrush();
		Paint.SetPen( !Enabled ? Color.WithAlphaMultiplied( 0.4f ) : Paint.HasMouseOver ? Color.Lighten( 0.8f ) : Color );
		Paint.DrawIcon( LocalRect, Icon, HeaderBarStyle.IconSize, TextFlag.Center );
	}
}
