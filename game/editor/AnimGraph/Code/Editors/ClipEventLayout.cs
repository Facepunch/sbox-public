namespace Editor.AnimGraph;

internal readonly struct ClipEventLayout( float width, int frameCount, float frameRate, float pixelsPerSecond, float offset )
{
	public const float HeaderWidth = 200;
	public const float LaneHeight = 32;
	public const float RulerHeight = 24;
	public static Color RangeBackground => Theme.ControlBackground.LerpTo( Theme.WidgetBackground, 0.2f );

	public float LastFrame => MathF.Max( frameCount - 1, 1 );
	public float FrameRate => frameRate > 0 ? frameRate : 1;
	public float PixelsPerFrame => pixelsPerSecond / FrameRate;
	public float Left => FrameToX( 0 );
	public float Right => FrameToX( LastFrame );
	public float FrameToX( float frame ) => offset + frame * PixelsPerFrame;
	public float XToFrame( float x ) => ((x - offset) / PixelsPerFrame).Clamp( 0, LastFrame );
	public float VisibleLeft => Left.Clamp( 0, width );
	public float VisibleRight => Right.Clamp( 0, width );

	public Rect PillRect( ClipDocument.Event item )
	{
		var start = FrameToX( item.StartFrame ).Clamp( Left, Right );
		var end = FrameToX( item.EndFrame ).Clamp( start, Right );
		return item.DurationFrames > 0
			? new Rect( start, 1, MathF.Max( end - start, 1 ), 30 )
			: new Rect( start - 5, 1, 10, 30 );
	}

	public Rect LabelRect( ClipDocument.Event item, float labelWidth )
	{
		var pill = PillRect( item );
		return item.DurationFrames > 0
			? new Rect( pill.Left + 6, pill.Top, MathF.Min( labelWidth, MathF.Max( pill.Width - 12, 0 ) ), pill.Height )
			: new Rect( pill.Right + 8, pill.Top, labelWidth, pill.Height );
	}

	public Rect EventRect( ClipDocument.Event item, float labelWidth = 72 )
	{
		var pill = PillRect( item );
		var label = LabelRect( item, labelWidth );
		var left = FrameToX( item.StartFrame ).Clamp( Left, Right ) - 6;
		return new Rect( left, pill.Top, MathF.Max( pill.Right + 6, label.Right ) - left, pill.Height );
	}

	public float RulerStep
	{
		get
		{
			var basis = MathF.Max( FrameRate * 0.1f, 1 );
			var minimum = MathF.Max( 100 / (basis * PixelsPerFrame), 1 );
			var magnitude = MathF.Pow( 10, MathF.Floor( MathF.Log10( minimum ) ) );
			var scale = minimum / magnitude;
			return basis * (scale <= 1 ? 1 : scale <= 2 ? 2 : scale <= 5 ? 5 : 10) * magnitude;
		}
	}

	private float MinorStep
	{
		get
		{
			var major = RulerStep;
			ReadOnlySpan<int> factors = [1, 2, 5];
			for ( var magnitude = 1f; magnitude <= major; magnitude *= 10 )
			{
				foreach ( var factor in factors )
				{
					var step = magnitude * factor;
					var divisions = major / step;
					if ( step * PixelsPerFrame >= 6 && MathF.Abs( divisions - MathF.Round( divisions ) ) < 0.0001f )
						return step;
				}
			}
			return major;
		}
	}

	public IEnumerable<float> RulerFrames( bool minor = false )
	{
		var step = minor ? MinorStep : RulerStep;
		var first = MathF.Max( MathF.Floor( -offset / PixelsPerFrame / step ) * step, 0 );
		var last = (width - offset) / PixelsPerFrame;
		var count = (int)MathF.Ceiling( width / (step * PixelsPerFrame) ) + 2;
		for ( var i = 0; i < count; i++ )
		{
			var frame = first + i * step;
			if ( frame > last )
				break;
			yield return frame;
		}
	}

	public void PaintGrid( float height )
	{
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.02f ), 1 );
		foreach ( var frame in RulerFrames( true ) )
		{
			var x = MathF.Round( FrameToX( frame ) ) + 0.5f;
			Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, height ) );
		}
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.02f ), 2 );
		foreach ( var frame in RulerFrames() )
		{
			var x = MathF.Round( FrameToX( frame ) ) + 0.5f;
			Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, height ) );
		}
	}
}
