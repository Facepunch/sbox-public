namespace Editor;

internal class EditorToolButton : Widget
{
	public Action Action { get; set; }
	public Func<bool> IsActive { get; set; }

	public Func<string> GetIcon { get; set; } = null!;

	public new bool Enabled
	{
		get => base.Enabled;
		set
		{
			base.Enabled = value;
			Cursor = value ? CursorShape.Finger : CursorShape.Arrow;
		}
	}

	public Color Color { get; set; } = Theme.TextLight;

	public EditorToolButton()
	{
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight;
	}

	protected override Vector2 SizeHint()
	{
		return new Vector2( Theme.ControlHeight );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		//	e.Accepted = false;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !Enabled )
			return;

		if ( e.LeftMouseButton )
		{
			Action?.Invoke();
			e.Accepted = true;
		}
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		bool active = IsActive?.Invoke() ?? false;

		if ( active )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );

			Paint.Pen = Theme.Text;
		}
		else
		{
			Paint.ClearPen();
			Paint.SetPen( Color );

			if ( Paint.HasMouseOver )
			{
				Paint.SetPen( Color.Lighten( 0.8f ) );
			}
		}

		if ( !Enabled )
		{
			Paint.SetPen( Color.Darken( 0.2f ).WithAlpha( 0.5f ) );
		}

		Paint.DrawIcon( LocalRect, GetIcon(), HeaderBarStyle.IconSize, TextFlag.Center );
	}

	public void UpdateState()
	{
		SetContentHash( HashCode.Combine( IsActive?.Invoke() ?? false, Color ), 0.1f );
	}
}
