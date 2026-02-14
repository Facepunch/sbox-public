namespace Editor;


public class ToastWidget : Widget
{
	public string Title;
	public string Subtitle;
	public string Icon = "notifications";
	public bool DrawTimer;
	public float ProgressDelta;
	public bool IsRunning;
	public Color BorderColor = Theme.Primary;
	Widget body;

	System.Diagnostics.Stopwatch stopWatch;

	public ToastWidget() : base( null )
	{
		IsTooltip = true;
		NoSystemBackground = true;
		TranslucentBackground = true;
		DrawTimer = true;
		stopWatch = System.Diagnostics.Stopwatch.StartNew();
		Position = new Vector2( -1000, 500 );
		IsRunning = true;
		SetSizeMode( SizeMode.Flexible, SizeMode.Flexible );

		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 48 + 8, 32 + 8, 16, 16 );

		ToastManager.Add( this );
	}

	/// <summary>
	/// Called when it's about to be re-used by a new compiler
	/// </summary>
	public virtual void Reset()
	{
		IsRunning = true;
		ToastManager.Reset( this );
	}

	/// <summary>
	/// Set a body widget to which the notice will stretch
	/// </summary>
	public void SetBodyWidget( Widget body )
	{
		this.body?.Destroy();

		this.body = body;

		if ( !body.IsValid() )
			return;

		Layout.Add( body );
		body.AdjustSize();

		Animate.Add( this, 0.3f, Height, body.Height + Layout.Margin.Top + Layout.Margin.Bottom, f => FixedHeight = f, "ease-out" );
		Animate.Add( this, 0.3f, Width, body.Width + Layout.Margin.Left + Layout.Margin.Right, f => FixedWidth = f, "ease-out" );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.RightMouseButton )
		{
			ToastManager.Dismiss( this );
			e.Accepted = true;
			return;
		}

		base.OnMousePress( e );
	}

	public virtual void Tick()
	{

	}

	protected override void OnPaint()
	{
		var textColor = Theme.Text.WithAlpha( 0.8f );
		var borderColor = BorderColor;

		if ( IsRunning )
		{
			borderColor = borderColor.Lighten( MathF.Sin( RealTime.Now * 20.0f ) * 0.2f );
		}
		else
		{
			if ( stopWatch.IsRunning )
				stopWatch.Stop();
		}

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.2f ) );

		var shadow = LocalRect.Shrink( 3, 3, 0, 0 );

		Paint.DrawRect( shadow, 8 );

		var rect = LocalRect.Shrink( 0, 0, 3, 3 );

		Paint.SetPen( borderColor, 2 );
		Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.98f ) );
		Paint.DrawRect( rect.Shrink( 2 ), 4 );

		if ( ProgressDelta > 0 )
		{
			Paint.ClearPen();
			Paint.SetBrush( borderColor.WithAlpha( 0.2f ) );

			var progressRect = rect.Shrink( 5 );
			progressRect.Width *= ProgressDelta.Clamp( 0, 1 );
			Paint.DrawRect( progressRect, 2 );
		}

		var leftColumn = rect;
		leftColumn.Width = 64;

		Paint.SetPen( borderColor );
		Paint.DrawIcon( leftColumn.Shrink( 16, 16 ), Icon, 32, TextFlag.LeftTop );

		if ( DrawTimer )
		{
			Paint.SetBrush( borderColor );
			Paint.ClearPen();
			Paint.SetHeadingFont( 8, 450 );
			Paint.DrawTextBox( rect.Shrink( 12, 8 ), stopWatch.Elapsed.TotalSeconds < 1d
				? $"{Math.Round( stopWatch.Elapsed.TotalMilliseconds / 10 ) * 10:n0}ms"
				: $"{stopWatch.Elapsed.TotalSeconds:n0}s", borderColor.Darken( 0.7f ), new( 4, 0 ), 4, TextFlag.RightTop );

		}

		Paint.SetPen( textColor );
		Paint.SetHeadingFont( 9, 450 );
		Paint.DrawText( rect.Shrink( 60, 16, 12, 12 ), Title, TextFlag.LeftTop );

		if ( !body.IsValid() )
		{
			Paint.SetPen( textColor.WithAlpha( 0.5f ) );
			Paint.SetDefaultFont();
			Paint.DrawText( rect.Shrink( 60, 38, 12, 12 ), Subtitle, TextFlag.LeftTop | TextFlag.WordWrap );
		}

		Update();
	}

	public virtual bool WantsVisible => true;
}
