using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// A tiny bar chart of one value a frame, newest on the right, for a headline number to carry its recent history.
/// Scaled to the series' own typical peak; anything over the top is capped in red.
/// </summary>
internal sealed class DevToolsSparkline : Panel
{
	/// <summary>
	/// The value <c>age</c> frames ago, and how many frames there are.
	/// </summary>
	public Func<int, float> Sample { get; set; }
	public Func<int> Count { get; set; } = () => DevTools.ShownFrames.Count;

	/// <summary>
	/// The least the top of the chart stands for, so a flat, tiny series stays flat rather than filling the chart.
	/// </summary>
	public float Floor { get; set; } = 1f;

	public Color Color { get; set; } = DevToolsStyles.Cpu;

	/// <summary>
	/// A reference value - the frame budget - drawn as a line, and kept at or under half the chart's height.
	/// </summary>
	public Func<float> Target { get; set; }

	/// <summary>
	/// The bars are frames in <see cref="DevTools.ShownFrames"/>, so clicking one picks that frame.
	/// </summary>
	public bool Selectable { get; set; }

	/// <summary>
	/// Bars fill the width with the whole ring when there's room, and are never thinner than this.
	/// </summary>
	float BarW => MathF.Max( 1.5f, Box.Rect.Width / ScaleToScreen / Capacity );

	/// <summary>
	/// The most bars there can be, so the history fills the width when it's full.
	/// </summary>
	public int Capacity { get; set; } = DevTools.FrameHistory.Capacity;

	public DevToolsSparkline()
	{
		AddClass( "sparkline" );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		if ( !Selectable ) return;
		var age = (int)((Box.Rect.Width / ScaleToScreen - e.LocalPosition.x / ScaleToScreen) / BarW);
		if ( age >= 0 && age < DevTools.ShownFrames.Count ) DevTools.SelectFrame( age );
		e.StopPropagation();
	}

	public override void OnDraw( Painter painter )
	{
		if ( Sample is null ) return;

		var scale = ScaleToScreen;
		var w = Box.Rect.Width / scale;
		var h = Box.Rect.Height / scale;
		if ( w < 4 || h < 4 ) return;

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;

		var count = Math.Min( Count(), (int)(w / BarW) );
		if ( count == 0 ) return;

		// The top is a high percentile rather than the max, so one spike doesn't flatten the rest
		Span<float> values = count <= 1024 ? stackalloc float[count] : new float[count];
		for ( int i = 0; i < count; i++ ) values[i] = Sample( i );
		Span<float> sorted = count <= 1024 ? stackalloc float[count] : new float[count];
		values.CopyTo( sorted );
		sorted.Sort();
		var target = Target?.Invoke() ?? 0;
		var top = MathF.Max( MathF.Max( Floor, target * 2 ), sorted[(int)((count - 1) * 0.97f)] * 1.25f );

		var selected = DevTools.SelectedAge;
		for ( int i = 0; i < count; i++ )
		{
			var v = values[i];
			var x = w - (i + 1) * BarW;
			if ( i == selected )
			{
				painter.Fill = Color.White.WithAlpha( 0.25f );
				painter.Rect( new Rect( x - 1, 0, BarW + 2, h ) );
			}

			if ( v <= 0 ) continue;
			var bh = MathF.Max( 0.75f, MathF.Min( v / top * h, h ) );
			painter.Fill = v > top ? DevToolsStyles.Bad : Color.WithAlpha( 0.75f );
			painter.Rect( new Rect( x, h - bh, BarW - 0.25f, bh ) );
		}

		if ( target > 0 )
		{
			painter.Fill = DevToolsStyles.Text.WithAlpha( 0.35f );
			for ( float x = 0; x < w; x += 5 )
				painter.Rect( new Rect( x, h - target / top * h, 2.5f, 1 ) );
		}
	}
}
