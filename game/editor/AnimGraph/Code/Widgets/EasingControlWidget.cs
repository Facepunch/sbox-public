using Easing = Sandbox.Utility.Easing;

namespace Editor.AnimGraph;

[CustomEditor( typeof( GraphBuilder.Easing ) )]
sealed class EasingControlWidget : ControlWidget
{
	private const float PreviewHeight = 140;

	public override bool IsWideMode => true;
	public override bool SupportsMultiEdit => true;

	protected override int ValueHash => HashCode.Combine( base.ValueHash, SerializedProperty.IsMultipleDifferentValues );

	public EasingControlWidget( SerializedProperty property ) : base( property )
	{
		PaintBackground = false;
		FixedHeight = Theme.RowHeight + 4 + PreviewHeight;
		Layout = Layout.Column();
		Layout.Spacing = 4;
		Layout.Add( new EnumControlWidget( property ) { FixedHeight = Theme.RowHeight } );
		Layout.AddSpacingCell( PreviewHeight );
	}

	protected override void OnValueChanged() => Update();

	protected override void PaintControl()
	{
		var area = new Rect( 0, Theme.RowHeight + 4, Width, PreviewHeight );
		Paint.SetBrushAndPen( Theme.ControlBackground, Theme.WidgetBackground, 1 );
		Paint.DrawRect( area.Shrink( 1 ), Theme.ControlRadius );

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( area, "Multiple Values" );
			return;
		}

		var plot = area.Shrink( 12 );
		plot.Left += 16;
		plot.Top += 10;
		plot.Bottom -= 16;

		Paint.SetPen( Theme.TextLight.WithAlpha( 0.12f ) );
		for ( var i = 0; i <= 4; i++ )
		{
			var x = plot.Left + plot.Width * i / 4;
			var y = plot.Top + plot.Height * i / 4;
			Paint.DrawLine( new Vector2( x, plot.Top ), new Vector2( x, plot.Bottom ) );
			Paint.DrawLine( new Vector2( plot.Left, y ), new Vector2( plot.Right, y ) );
		}

		Paint.SetPen( Theme.TextLight.WithAlpha( 0.25f ), 1, PenStyle.Dot );
		Paint.DrawLine( plot.BottomLeft, plot.TopRight );

		var easing = SerializedProperty.GetValue<GraphBuilder.Easing>();
		Paint.SetPen( Theme.Blue, 2 );
		Paint.DrawLine( Enumerable.Range( 0, 65 ).Select( i =>
		{
			var t = i / 64.0f;
			return new Vector2( plot.Left + plot.Width * t, plot.Bottom - plot.Height * Evaluate( easing, t ) );
		} ) );

		Paint.SetDefaultFont( 8 );
		Paint.SetPen( Theme.TextLight );
		Paint.DrawText( new Rect( plot.Left, area.Top + 2, plot.Width, 16 ), "Value", TextFlag.LeftCenter );
		Paint.DrawText( new Rect( plot.Left, plot.Bottom + 3, plot.Width, 16 ), "Time" );
		Paint.DrawText( new Rect( area.Left + 4, plot.Top - 6, 16, 12 ), "1", TextFlag.RightCenter );
		Paint.DrawText( new Rect( area.Left + 4, plot.Bottom - 6, 16, 12 ), "0", TextFlag.RightCenter );
		Paint.DrawText( new Rect( plot.Left, plot.Bottom + 3, 16, 16 ), "0", TextFlag.LeftCenter );
		Paint.DrawText( new Rect( plot.Right - 16, plot.Bottom + 3, 16, 16 ), "1", TextFlag.RightCenter );
	}

	private static float Evaluate( GraphBuilder.Easing easing, float t )
	{
		Easing.Function easeIn = easing switch
		{
			GraphBuilder.Easing.Linear or GraphBuilder.Easing.None => Easing.Linear,
			GraphBuilder.Easing.InQuad or GraphBuilder.Easing.OutQuad or GraphBuilder.Easing.InOutQuad => Easing.QuadraticIn,
			GraphBuilder.Easing.InCubic or GraphBuilder.Easing.OutCubic or GraphBuilder.Easing.InOutCubic => Easing.CubicIn,
			GraphBuilder.Easing.InQuart or GraphBuilder.Easing.OutQuart or GraphBuilder.Easing.InOutQuart => static value => value * value * value * value,
			GraphBuilder.Easing.InQuint or GraphBuilder.Easing.OutQuint or GraphBuilder.Easing.InOutQuint => static value => value * value * value * value * value,
			GraphBuilder.Easing.InSine or GraphBuilder.Easing.OutSine or GraphBuilder.Easing.InOutSine => Easing.SineEaseIn,
			// Match animationsystem2/easing.h, including its exponential offset.
			GraphBuilder.Easing.InExpo or GraphBuilder.Easing.OutExpo or GraphBuilder.Easing.InOutExpo => static value => MathF.Pow( 2, 10 * (value - 1) ) - 0.001f,
			GraphBuilder.Easing.InCirc or GraphBuilder.Easing.OutCirc or GraphBuilder.Easing.InOutCirc => static value => 1 - MathF.Sqrt( 1 - value * value ),
			_ => throw new ArgumentOutOfRangeException( nameof( easing ), easing, null )
		};

		return easing switch
		{
			GraphBuilder.Easing.OutQuad or GraphBuilder.Easing.OutCubic or GraphBuilder.Easing.OutQuart
				or GraphBuilder.Easing.OutQuint or GraphBuilder.Easing.OutSine or GraphBuilder.Easing.OutExpo
				or GraphBuilder.Easing.OutCirc => 1 - easeIn( 1 - t ),
			GraphBuilder.Easing.InOutQuad or GraphBuilder.Easing.InOutCubic or GraphBuilder.Easing.InOutQuart
				or GraphBuilder.Easing.InOutQuint or GraphBuilder.Easing.InOutSine or GraphBuilder.Easing.InOutExpo
				or GraphBuilder.Easing.InOutCirc => t < 0.5f ? easeIn( 2 * t ) * 0.5f : 1 - easeIn( 2 - 2 * t ) * 0.5f,
			_ => easeIn( t )
		};
	}
}
