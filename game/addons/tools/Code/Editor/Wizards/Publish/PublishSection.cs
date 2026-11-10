namespace Editor;

/// <summary>Consistent action sizing and native button styling throughout publishing.</summary>
internal sealed class PublishButton : Button
{
	public static Color SecondaryTint => "#48494c";

	public PublishButton( string title, string icon = null, bool primary = false ) : base( title, icon )
	{
		FixedHeight = 30;
		Tint = primary ? Theme.Primary : SecondaryTint;
	}

	// Virtual list actions use the same border and gradient as native Buttons.
	public static void DrawBackground( Rect rect, bool hovered )
	{
		var color = SecondaryTint.ToHsv();
		var background = color with { Value = color.Value + (hovered ? 0.2f : 0) };
		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( background with { Value = background.Value + 0.04f, Saturation = color.Saturation * 0.8f } );
		Paint.DrawRect( rect, 3 );
		Paint.SetBrushLinear( rect.TopLeft, rect.BottomRight, background, background with { Value = background.Value - 0.03f } );
		Paint.DrawRect( rect.Shrink( 1 ), 3 );
		Paint.SetDefaultFont();
	}

	public static void DrawAction( Rect rect, string text, string icon, Color color )
	{
		Paint.SetDefaultFont();
		var textWidth = Paint.MeasureText( text ).x;
		var left = rect.Center.x - (textWidth + 20) / 2;
		Paint.SetPen( color );
		Paint.DrawIcon( new Rect( left, rect.Center.y - 8, 16, 16 ), icon, 16 );
		Paint.DrawText( new Rect( left + 20, rect.Top, textWidth, rect.Height ), text, TextFlag.Center );
	}
}

/// <summary>A lightly separated section shared by the publishing settings and wizard.</summary>
internal class PublishSection : Widget
{
	public Layout Header { get; }

	/// <summary>
	/// The header across the top - the rest of the section is under it
	/// </summary>
	readonly Widget HeaderArea;

	/// <param name="description">What the section's for, at the right of its title in the header - or null for just the title</param>
	public PublishSection( Widget parent, string title, Color accent, string icon, string description = null, int? count = null, Pixmap iconImage = null ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 12, 0, 12, 12 );
		Layout.Spacing = 10;
		Layout.Alignment = TextFlag.Top;

		HeaderArea = Layout.Add( new Widget( this ) );
		HeaderArea.Layout = Layout.Row();
		HeaderArea.Layout.Margin = new Sandbox.UI.Margin( 0, 6, 0, 6 );
		Header = HeaderArea.Layout;
		Header.Spacing = 8;
		if ( iconImage is not null || !string.IsNullOrEmpty( icon ) )
		{
			var symbol = Header.Add( new Widget( this ) { FixedSize = 20 } );
			symbol.OnPaintOverride = () =>
			{
				if ( iconImage is not null )
				{
					Paint.ClearPen();
					Paint.Draw( symbol.LocalRect.Shrink( 2 ), iconImage );
				}
				else
				{
					Paint.SetPen( accent );
					Paint.DrawIcon( symbol.LocalRect, icon, 18 );
				}
				return true;
			};
		}
		Header.Add( new Label.Subtitle( title ) { Color = Theme.Text, WordWrap = false, FixedHeight = 32 } );
		if ( count is { } value )
		{
			var badge = Header.Add( new Label( $"{value:n0}" ) { Alignment = TextFlag.Center, FixedHeight = 22, MinimumWidth = 28 } );
			badge.SetStyles( $"background-color: {Theme.Text.WithAlpha( 0.08f ).Hex}; color: {Theme.Text.Hex}; border-radius: 10px; padding: 2px 8px;" );
		}
		Header.AddStretchCell();

		// On the title's line, over on the right
		if ( !string.IsNullOrEmpty( description ) )
			Header.Add( new Label( description ) { Color = Theme.TextControl.WithAlpha( 0.5f ), Alignment = TextFlag.RightCenter } );
	}

	/// <summary>
	/// Something small and secondary - a hint under a field, what something will look like
	/// </summary>
	public static Label AddNote( Layout layout, string text ) => layout.Add( new Label( text ) { WordWrap = true, Color = Theme.TextControl.WithAlpha( 0.5f ) } );

	/// <summary>
	/// A row lined up with a <see cref="ControlSheet"/>'s - the label in its label column, and the returned
	/// column where its control would be
	/// </summary>
	public static Layout AddRow( Layout layout, string label )
	{
		var row = layout.Add( new Widget( null ) );
		row.Layout = Layout.Row();
		row.Layout.Margin = new Sandbox.UI.Margin( 6, 2, 4, 0 );

		row.Layout.Add( new Label( label )
		{
			FixedWidth = 144,
			MinimumHeight = Theme.RowHeight,
			ContentMargins = 4,
			Alignment = TextFlag.LeftTop,
			Color = Theme.TextControl.WithAlpha( 0.7f )
		} );

		var value = row.Layout.AddColumn( 1 );
		value.Spacing = 4;
		return value;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.SetPen( Theme.Text.WithAlpha( 0.10f ), 1 );
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.32f ) );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 6 );
	}
}
