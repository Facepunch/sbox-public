using System;

namespace Editor;

/// <summary>
/// A material icon label
/// </summary>
public class IconLabel : Widget
{
	public string Icon { get; set; }
	public float IconSize { get; set; } = 12.0f;
	public Color Background { get; set; }
	public Color Foreground { get; set; }

	public Action<bool> OnToggled { get; set; }

	public IconLabel( string icon, Widget parent = null ) : base( parent )
	{
		Icon = icon;

		Background = Color.Transparent;
		Foreground = Theme.Text;

		FixedHeight = Theme.RowHeight;
		FixedWidth = Theme.RowHeight;
	}

	protected override Vector2 SizeHint()
	{
		return MinimumWidth;
	}

	protected override void OnPaint()
	{
		Paint.ClearBrush();
		Paint.ClearPen();

		var bg = Background;
		var fg = Foreground;

		Paint.SetBrush( bg );
		Paint.DrawRect( LocalRect, 2.0f );

		Paint.ClearBrush();
		Paint.ClearPen();

		Paint.Pen = fg;
		if ( !Enabled ) Paint.Pen = fg.WithAlphaMultiplied( 0.25f );

		Paint.DrawIcon( LocalRect, Icon, IconSize, TextFlag.Center );
	}
}
