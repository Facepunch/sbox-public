namespace Editor.AnimGraph;

/// <summary>
/// A titled bar separating the sections of a dock, holding that section's buttons.
/// </summary>
internal sealed class SectionHeader : Widget
{
	private string _title;
	private string _detail;

	public string Title
	{
		get => _title;
		set
		{
			if ( _title == value )
				return;

			_title = value;
			Update();
		}
	}

	/// <summary>
	/// Extra context drawn after the title, dimmed.
	/// </summary>
	public string Detail
	{
		get => _detail;
		set
		{
			if ( _detail == value )
				return;

			_detail = value;
			Update();
		}
	}

	/// <summary>
	/// Space kept clear on the left for the painted title, so stretchy content can't run under it.
	/// </summary>
	private const float TitleSpace = 96;

	public SectionHeader( Widget parent, string title ) : base( parent )
	{
		_title = title;

		// The bar is exactly button height - the root layout's spacing provides the gap below it
		FixedHeight = Theme.RowHeight;

		Layout = Layout.Row();
		Layout.Margin = 0;
		Layout.Spacing = 2;
		Layout.AddSpacingCell( TitleSpace );
		Layout.AddStretchCell();
	}

	public IconButton AddButton( string icon, Color foreground, string toolTip, Action onClick ) =>
		Layout.Add( CreateButton( this, icon, foreground, toolTip, onClick ) );

	/// <summary>
	/// A compact button styled to sit on a section header, or in a row that lines up with one.
	/// </summary>
	public static IconButton CreateButton( Widget parent, string icon, Color foreground, string toolTip, Action onClick, bool enabled = true )
	{
		return new IconButton( icon, onClick, parent )
		{
			ToolTip = toolTip,
			Enabled = enabled,
			IconSize = 13,
			FixedSize = Theme.RowHeight,
			Background = Theme.ControlBackground,
			Foreground = foreground,
		};
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.SurfaceBackground );
		Paint.DrawRect( LocalRect );

		var rect = LocalRect.Shrink( 6, 0, 4, 0 );

		Paint.SetHeadingFont( 11, 440, sizeInPixels: true );
		Paint.SetPen( Theme.Text );
		var drawn = Paint.DrawText( rect, _title, TextFlag.LeftCenter );

		if ( string.IsNullOrEmpty( _detail ) )
			return;

		rect.Left = drawn.Right + 6;
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
		drawn = Paint.DrawText( rect, "·", TextFlag.LeftCenter );

		rect.Left = drawn.Right + 6;
		Paint.SetDefaultFont();
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
		Paint.DrawText( rect, _detail, TextFlag.LeftCenter );
	}
}
