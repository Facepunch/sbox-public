namespace Editor;

/// <summary>
/// A button that shows as an icon and tries to keep itself square.
/// </summary>
public class ToolButton : Widget
{
	/// <summary>
	/// Icon to display when the <see cref="Checked"/> is <see langword="true"/>.
	/// </summary>
	public string IconChecked { get; set; }

	/// <summary>
	/// Icon for the tool button.
	/// </summary>
	public string Icon { get; set; }

	/// <summary>
	/// Whether the button is toggle-able or not.
	/// </summary>
	public bool IsToggle { get; set; }

	bool _checked;
	/// <summary>
	/// Whether the tool button is currently checked or not.
	/// </summary>
	public bool Checked
	{
		get => _checked;
		set
		{
			if ( _checked == value ) return;
			_checked = value;
			Update();
		}
	}

	public ToolButton( string name, string icon, Widget parent ) : base( parent )
	{
		Icon = icon;
		IconChecked = icon;
		MinimumSize = Theme.RowHeight;
		ToolTip = name;
		Cursor = CursorShape.Finger;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton && IsToggle )
		{
			Checked = !Checked;
			e.Accepted = true;
			return;
		}

		base.OnMousePress( e );
		e.Accepted = true;
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		MinimumWidth = Height;
	}

	protected override void OnPaint()
	{
		var col = Theme.TextControl;
		var icon = Icon;

		var r = LocalRect;

		Paint.ClearPen();
		if ( !Enabled )
		{
			col = col.WithAlpha( 0.3f );
		}
		else
		{
			if ( Paint.HasMouseOver )
				Paint.SetBrush( Theme.SurfaceBackground );
			else
				Paint.SetBrush( Theme.ControlBackground );

			col = Color.White;
		}

		Paint.DrawRect( r.Shrink( 1.0f ), Theme.ControlRadius );

		if ( IsToggle && Checked )
		{
			icon = IconChecked;
		}

		if ( IsToggle && Checked && IconChecked == Icon )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.5f ) );
			Paint.DrawRect( r );
		}

		Paint.SetPen( col );
		Paint.DrawIcon( r, icon, 14, TextFlag.Center );
	}
}
