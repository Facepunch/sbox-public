namespace Editor.AnimGraph;

/// <summary>
/// The scene-view style toolbar every 3D preview puts above its viewport - a surface strip of
/// pill shaped button groups. Owns its buttons so their active states can be polled each frame.
/// </summary>
internal sealed class PreviewToolbar
{
	private readonly List<PreviewToolButton> _buttons = [];

	/// <summary>
	/// The strip itself. Fill the layout it hands back with groups.
	/// </summary>
	public Widget CreateBar( Widget parent, out Layout row )
	{
		var bar = new Widget( parent ) { FixedHeight = Theme.RowHeight + 2 };

		bar.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( bar.LocalRect );
			return true;
		};

		row = bar.Layout = Layout.Row();
		row.Margin = new Sandbox.UI.Margin( 0, 0, 0, 2 );
		row.Spacing = 2;

		return bar;
	}

	/// <summary>
	/// A pill of buttons that belong together, like the scene view groups its tools.
	/// </summary>
	public static Layout AddGroup( Layout row )
	{
		var group = new Widget
		{
			FixedHeight = Theme.RowHeight,
			Layout = Layout.Row()
		};

		group.Layout.Spacing = 2;
		group.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( group.LocalRect, Theme.ControlRadius );
			return true;
		};

		row.Add( group );
		return group.Layout;
	}

	public static void AddSeparator( Layout row )
	{
		var separator = new Widget
		{
			FixedWidth = 5,
			FixedHeight = Theme.RowHeight
		};

		separator.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TextLight.WithAlpha( 0.2f ) );
			Paint.DrawRect( new Rect( 2, 4, 1, separator.Height - 8 ) );
			return true;
		};

		row.Add( separator );
	}

	public PreviewToolButton AddButton( Layout layout, string icon, string tooltip, Action onClick )
	{
		return layout.Add( new PreviewToolButton
		{
			Icon = icon,
			ToolTip = tooltip,
			Action = onClick,
		} );
	}

	public PreviewToolButton AddToggle( Layout layout, string icon, string tooltip, Func<bool> getValue, Action<bool> setValue )
	{
		var button = AddButton( layout, icon, tooltip, () => setValue( !getValue() ) );
		button.IsActive = getValue;

		_buttons.Add( button );
		return button;
	}

	/// <summary>
	/// Repaint the toggles whose state has changed. Nothing tells them, so they're polled.
	/// </summary>
	public void UpdateButtons()
	{
		foreach ( var button in _buttons )
		{
			button.UpdateState();
		}
	}
}

/// <summary>
/// Viewport-style icon button - blue pill when active, matching the scene view toolbar.
/// </summary>
internal class PreviewToolButton : Widget
{
	public string Icon { get; set; }
	public Action Action { get; set; }
	public Func<bool> IsActive { get; set; }

	public PreviewToolButton() : base( null )
	{
		Cursor = CursorShape.Finger;
		FixedWidth = Theme.RowHeight;
		FixedHeight = Theme.RowHeight;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
			return;

		Action?.Invoke();
		Update();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;

		if ( IsActive?.Invoke() ?? false )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );
			Paint.SetPen( Theme.Text );
		}
		else
		{
			Paint.ClearBrush();
			Paint.SetPen( Paint.HasMouseOver ? Theme.TextLight.Lighten( 0.8f ) : Theme.TextLight );
		}

		Paint.DrawIcon( LocalRect, Icon, 16, TextFlag.Center );
	}

	public void UpdateState() => SetContentHash( HashCode.Combine( IsActive() ), 0.1f );
}

/// <summary>
/// A value shown alongside a toolbar control, like the playback speed next to its slider.
/// </summary>
internal class ToolbarLabel : Widget
{
	private string _text;

	public string Text
	{
		get => _text;
		set { _text = value; Update(); }
	}

	public ToolbarLabel( string text ) : base( null )
	{
		_text = text;
		FixedHeight = Theme.RowHeight;
		FixedWidth = 38;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.TextLight );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( LocalRect.Shrink( 6, 0 ), _text, TextFlag.Center );
	}
}
