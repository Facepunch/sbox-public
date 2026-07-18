using System;

namespace Editor;

public partial class BaseWindow : Widget
{
	/// <summary>
	/// Position the window at the centre of the screen (or main editor window if one is present) by default.
	/// </summary>
	public bool StartCentered { get; set; } = true;

	public Action OnWindowClosed { get; set; }

	public override void SetWindowIcon( string name )
	{
		var icon = new Pixmap( 128, 128 );
		icon.Clear( Color.Transparent );


		using ( Paint.ToPixmap( icon ) )
		{
			var r = new Rect( 0, 0, 128, 128 );

			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary );
			Paint.DrawRect( r, 16 );

			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( r, name, 120 );
		}

		base.SetWindowIcon( icon );
	}

	public BaseWindow() : base( null, true )
	{
		IsWindow = true;
		DeleteOnClose = true;
	}

	protected override void OnClosed()
	{
		OnWindowClosed?.Invoke();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;

		Paint.SetPen( Theme.Primary.WithAlpha( 0.2f ), 3 );
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect.Shrink( 1 ), 3 );

		Paint.SetPen( Theme.WindowBackground.WithAlpha( 0.9f ), 1 );
		Paint.ClearBrush();
		Paint.DrawRect( LocalRect.Shrink( 0 ), 1 );

		var l = LocalRect.Shrink( 1 );

		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( l, 1 );
	}

	public override void Show()
	{
		if ( StartCentered )
		{
			CenterWindow();
		}

		base.Show();
	}
}
