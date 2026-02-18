using Sandbox.UI;

namespace Editor;

public class WarningBox : Widget
{
	Color _bgColor;

	public Color BackgroundColor
	{
		get => _bgColor;
		set
		{
			_bgColor = value;
			Label.Color = _bgColor;
		}
	}

	public Label Label;

	string _icon;
	public string Icon
	{
		get => _icon;
		set
		{
			_icon = value;
			SetProperty( "hasIcon", string.IsNullOrEmpty( _icon ) ? "1" : "0" );
			Layout.Margin = new Margin( 32, 8, 8, 8 );
		}
	}

	private const float IconMargin = 32;
	private const float IconSize = 24;

	public WarningBox( Widget parent = null ) : this( null, parent ) { }

	public WarningBox( string title, Widget parent = null ) : base( parent )
	{
		Layout = Layout.Column();

		Label = new Label( title, this );
		Label.WordWrap = true;
		Label.Alignment = TextFlag.LeftTop;

		Layout.Add( Label );

		Icon = "warning";
		BackgroundColor = Theme.Yellow;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.ClearPen();

		var gradientCenter = LocalRect.TopLeft + new Vector2( IconMargin, Height * 0.5f );

		Paint.SetBrushRadial( gradientCenter, 400, BackgroundColor.Darken( 0.6f ), BackgroundColor.Darken( 0.7f ) );
		Paint.DrawRect( LocalRect, 2 );

		Paint.SetBrushRadial( gradientCenter, 400, BackgroundColor.Darken( 0.4f ), BackgroundColor.Darken( 0.45f ) );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 2 );

		if ( !string.IsNullOrEmpty( _icon ) )
		{
			var iconRect = new Rect(
				LocalRect.Left + (IconMargin - IconSize) * 0.5f,
				LocalRect.Top + (Height - IconSize) * 0.5f,
				IconSize,
				IconSize
			);

			Paint.SetPen( BackgroundColor );
			Paint.DrawIcon( iconRect, _icon, 18, TextFlag.Center );
		}
	}
}

public class InformationBox : WarningBox
{
	public InformationBox( Widget parent = null ) : this( null, parent ) { }

	public InformationBox( string title, Widget parent = null ) : base( title, parent )
	{
		BackgroundColor = Theme.Primary;
		Icon = "info";
	}
}
