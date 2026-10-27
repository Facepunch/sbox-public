namespace Editor;

public class VerticalToolbarGroup : Widget
{
	public string Title { get; set; }

	public VerticalToolbarGroup( Widget parent, string title, string icon ) : base( parent )
	{
		FixedWidth = Theme.ControlHeight;

		Title = title?.ToUpperInvariant() ?? "";
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 0, 0, 0, 0 );
		Layout.Spacing = 4;

		if ( icon is not null )
		{
			Layout.Add( new ToolbarIcon( icon ) );
		}

		Build();
	}

	public virtual void Build()
	{

	}

	public Widget AddToggleButton( string tooltip, string icon, Func<bool> getVal, Action<bool> setVal )
	{
		var __getVal = () => { try { return getVal(); } catch ( System.Exception ) { return false; } };
		var __setVal = ( bool b ) => { try { setVal( b ); } catch ( System.Exception ) { } };

		var b = new EditorToolButton();
		b.Icon = icon;
		b.ToolTip = tooltip;
		b.Action = () => __setVal( !__getVal() );
		b.IsActive = () => __getVal();

		Layout.Add( b );
		return b;
	}

	public Widget AddButton( string tooltip, string icon, Action onPressed = null, Func<bool> isActive = null )
	{
		var b = new EditorToolButton();
		b.Icon = icon;
		b.ToolTip = tooltip;
		b.Action = onPressed;
		b.IsActive = isActive;

		Layout.Add( b );
		return b;
	}
}

file class ToolbarIcon : Widget
{
	private string icon;

	public ToolbarIcon( string icon )
	{
		this.icon = icon;
		FixedSize = Theme.ControlHeight * 2;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Paint.SetPen( Theme.TextControl.WithAlpha( 0.2f ) );
		Paint.DrawIcon( LocalRect, icon, HeaderBarStyle.IconSize, TextFlag.Center );
	}
}

file class EditorToolButton : Widget
{
	public string Icon { get; set; }

	public Action Action { get; set; }
	public Func<bool> IsActive { get; set; }

	public EditorToolButton()
	{
		FixedSize = Theme.ControlHeight * 2;
		Cursor = CursorShape.Finger;
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		//	e.Accepted = false;
	}

	protected override void OnMousePress( MouseEvent e )
	{
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
		active = active || Paint.HasPressed;

		if ( active )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue.WithAlpha( 0.5f ) );
			Paint.DrawRect( LocalRect, 4 );

			Paint.Pen = Theme.Text;
		}
		else
		{
			Paint.SetPen( Theme.SurfaceLightBackground );
		}

		Paint.DrawIcon( LocalRect, Icon, HeaderBarStyle.IconSize, TextFlag.Center );
	}

	[EditorEvent.Frame]
	public void UpdateState()
	{
		if ( IsActive is null )
			return;

		SetContentHash( HashCode.Combine( IsActive() ), 0.1f );
	}

}

file class Separator : Widget
{
	public bool LightMode { get; set; }

	public Separator() : base( null )
	{
		FixedHeight = Theme.ControlHeight;
		FixedWidth = 2;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		Paint.SetBrush( Color.Black.WithAlpha( 0.1f ) );
		Paint.DrawRect( LocalRect );
	}
}
