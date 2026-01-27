using System.Threading;
namespace Editor;

internal class ProgressWindow : Dialog
{
	public string TaskTitle;
	public double ProgressCurrent;
	public double ProgressTotal;

	Button CancelButton;
	CancellationTokenSource CancellationTokenSource;

	public ProgressWindow()
	{
		Window.FixedWidth = 500;
		Window.FixedHeight = 150;
		Window.CloseButtonVisible = false;
		Window.WindowTitle = "Progress Popup";

		Window.SetWindowIcon( "timer" );
		Window.SetModal( true, true );

		CancelButton = new Button( this );
		CancelButton.Text = "Stop";

		CancellationTokenSource = new CancellationTokenSource();
		CancelButton.MouseLeftPress += () => CancellationTokenSource.Cancel();
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		CancelButton.AdjustSize();
		CancelButton.Width = 100;
		CancelButton.Position = Size - CancelButton.Size - new Vector2( 32, 16 );
	}

	public CancellationToken GetCancel()
	{
		return CancellationTokenSource.Token;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var bottomSection = LocalRect;
		bottomSection.Top = CancelButton.Position.y - 16;

		var scrollbarRect = bottomSection.Shrink( 16, 0 );
		scrollbarRect.Top -= 48;
		scrollbarRect.Bottom = scrollbarRect.Top + 24;

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect, 0.0f );

		if ( TaskTitle != null )
		{
			Paint.SetPen( Theme.Text );
			Paint.SetDefaultFont();
			Paint.DrawText( LocalRect.Shrink( 24, 16 ), TaskTitle, TextFlag.LeftTop );
		}

		//
		// ProgressBar
		//
		Paint.SetPen( Theme.ControlBackground, 1.0f );
		Paint.SetBrush( Theme.WidgetBackground.Darken( 0.1f ) );
		Paint.DrawRect( scrollbarRect, 2.0f );

		if ( ProgressTotal > 0 )
		{
			var delta = (float)(ProgressCurrent / ProgressTotal);

			scrollbarRect = scrollbarRect.Shrink( 1 );
			scrollbarRect.Width *= delta;

			Paint.SetPen( Theme.Primary, 1.0f );
			Paint.SetBrush( Theme.Primary.Darken( 0.1f ) );
			Paint.DrawRect( scrollbarRect, 2.0f );

			Paint.SetPen( Theme.Text );
			Paint.SetDefaultFont();
			Paint.DrawText( LocalRect.Shrink( 24, 16 ), $"{ProgressCurrent:n0} / {ProgressTotal:n0}", TextFlag.RightTop );
		}

		//
		// Bottom section background
		//
		Paint.SetPen( Theme.ControlBackground, 1.0f );
		Paint.SetBrush( Theme.WidgetBackground.Darken( 0.1f ) );
		Paint.DrawRect( bottomSection.Grow( 1 ) );

	}
}
