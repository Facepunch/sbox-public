namespace Editor;

/// <summary>A lightly separated section shared by the publishing settings and wizard.</summary>
internal class PublishSection : Widget
{
	public Layout Header { get; }

	public PublishSection( Widget parent, string title, Color accent, string icon ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 10;
		Layout.Spacing = 8;
		Header = Layout.AddRow();
		Header.Spacing = 8;
		if ( !string.IsNullOrEmpty( icon ) )
		{
			var symbol = Header.Add( new Widget( this ) { FixedSize = 20 } );
			symbol.OnPaintOverride = () =>
			{
				Paint.SetPen( accent );
				Paint.DrawIcon( symbol.LocalRect, icon, 18 );
				return true;
			};
		}
		Header.Add( new Label.Subtitle( title ) { Color = accent }, 1 );
	}

	protected override void OnPaint()
	{
		Paint.SetPen( Theme.Text.WithAlpha( 0.07f ), 1 );
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.2f ) );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 4 );
	}
}
