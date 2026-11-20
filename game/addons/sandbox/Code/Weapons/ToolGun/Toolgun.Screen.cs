public partial class Toolgun : ScreenWeapon
{
	/// <summary>
	/// Colour of the tool name and swipe edge on the viewmodel screen, independent of the HUD tint.
	/// Goes past 1 for HDR glow.
	/// </summary>
	[Property] public Color ScreenColor { get; set; } = HudStyle.Brightened( HudLayer.DefaultTint, 2f );

	/// <summary>
	/// The mode the screen is switching to, and the one it showed before
	/// </summary>
	ToolMode _screenTarget;
	ToolMode _screenPrevious;

	const float SwipeJitter = 60f;
	const float SwipeEdgeWidth = 6f;

	/// <summary>
	/// Draws the current tool mode on the viewmodel screen. A new mode swipes in from the left over the old one,
	/// along a glowing, ragged edge that jumps about as it goes.
	/// </summary>
	protected override void DrawScreenContent( Rect rect, Painter paint )
	{
		var currentMode = GetCurrentMode();
		if ( currentMode != _screenTarget )
		{
			_screenPrevious = _screenTarget;
			_screenTarget = currentMode;
			StartScreenTransition();
		}

		if ( !InScreenTransition )
		{
			_screenTarget?.DrawScreen( rect, paint );
			return;
		}

		// the edge runs from just off the left to just off the right, each row of dots offset at random
		var tick = (int)(Time.Now * 30f);
		var edgeBase = rect.Left - SwipeJitter + TransitionProgress * (rect.Width + SwipeJitter * 2);
		var rows = (int)rect.Height;
		const float rowHeight = 1f;
		var edgeColor = ScreenColor;

		for ( var row = 0; row < rows; row++ )
		{
			var top = rect.Top + row * rowHeight;

			// whole pixels, so the dots either side of the edge are fully lit and keep their HDR glow
			var edge = MathF.Round( edgeBase + (Hash( row, tick ) - 0.5f) * SwipeJitter );

			if ( _screenTarget.IsValid() )
			{
				using var _ = paint.Scope();
				paint.Clip( new Rect( rect.Left, top, MathF.Max( edge - rect.Left, 0 ), rowHeight ) );
				_screenTarget.DrawScreen( rect, paint );
			}

			if ( _screenPrevious.IsValid() )
			{
				using var _ = paint.Scope();
				paint.Clip( new Rect( edge, top, MathF.Max( rect.Right - edge, 0 ), rowHeight ) );
				_screenPrevious.DrawScreen( rect, paint );
			}

			paint.Fill = edgeColor;
			paint.Rect( new Rect( edge - SwipeEdgeWidth * 0.5f, top, SwipeEdgeWidth, rowHeight ) );
		}
	}

	/// <summary>
	/// Cheap repeatable 0-1 noise for the swipe edge
	/// </summary>
	static float Hash( int a, int b )
	{
		var h = (uint)(a * 374761393 + b * 668265263);
		h = (h ^ (h >> 13)) * 1274126177;
		return (h & 0xffff) / 65535f;
	}
}
