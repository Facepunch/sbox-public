namespace Editor;

internal class WelcomeOverlay : Widget
{
	Widget highlightedWidget;
	Color overlayColor = Color.Black.WithAlpha( 0.5f );
	RealTimeSince timeSinceCreated;

	public WelcomeOverlay( Widget parent ) : base( parent )
	{
		WindowFlags = WindowFlags.FramelessWindowHint | WindowFlags.Tool;
		TranslucentBackground = true;
		NoSystemBackground = true;
		TransparentForMouseEvents = true;
		FocusMode = FocusMode.None;
		Enabled = false;

		if ( parent != null )
		{
			Position = Vector2.Zero;
			Size = parent.Size;
		}

		timeSinceCreated = 0;
	}

	[EditorEvent.Frame]
	void OnFrame()
	{
		// Update to animate the pulse effect
		if ( highlightedWidget != null && highlightedWidget.IsValid )
		{
			Update();
		}
	}

	public void ClearHighlight()
	{
		highlightedWidget = null;
		Update();
	}

	public void SetHighlight( Widget widget )
	{
		highlightedWidget = widget;
		Update();
	}

	public void UpdateSize()
	{
		if ( Parent != null && Parent.IsValid )
		{
			Position = Vector2.Zero;
			Size = Parent.Size;
		}
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = true;

		if ( highlightedWidget == null || !highlightedWidget.IsValid || !highlightedWidget.Visible )
		{
			// No highlight, just draw a full rectangle to grey-out the background
			Paint.ClearPen();
			Paint.SetBrush( overlayColor );
			Paint.DrawRect( LocalRect );
			return;
		}

		// Draw overlay with cutout for highlighted widget
		Paint.ClearPen();
		Paint.SetBrush( overlayColor );

		var widgetScreenPos = highlightedWidget.ScreenPosition;
		var overlayScreenPos = ScreenPosition;
		var relativePos = widgetScreenPos - overlayScreenPos;
		var highlightRect = new Rect( relativePos, highlightedWidget.Size );
		var padding = 4f;

		// Draw 4 greyed-out rectangles around the highlighted widget to create a cutout effect
		if ( highlightRect.Top > 0 )
		{
			// Top rectangle
			Paint.DrawRect( new Rect( 0, 0, LocalRect.Width, highlightRect.Top - padding ) );
		}
		if ( highlightRect.Left > 0 )
		{
			// Left rectangle
			Paint.DrawRect( new Rect( 0, highlightRect.Top - padding, highlightRect.Left - padding, highlightRect.Height + padding * 2 ) );
		}
		if ( highlightRect.Right < LocalRect.Width )
		{
			// Right rectangle
			Paint.DrawRect( new Rect( highlightRect.Right + padding, highlightRect.Top - padding, LocalRect.Width - (highlightRect.Right + padding), highlightRect.Height + padding * 2 ) );
		}
		if ( highlightRect.Bottom < LocalRect.Height )
		{
			// Bottom rectangle
			Paint.DrawRect( new Rect( 0, highlightRect.Bottom + padding, LocalRect.Width, LocalRect.Height - (highlightRect.Bottom + padding) ) );
		}

		// Pulsing outline
		Paint.ClearBrush();
		float pulse = MathF.Sin( timeSinceCreated * MathF.PI * 2.0f );
		float pulseWidth = 3.0f + pulse * 1.5f;
		Paint.SetPen( Theme.Primary.WithAlpha( 1.0f ), pulseWidth );
		Paint.DrawRect( highlightRect.Shrink( -2 ), 4 );
	}
}
