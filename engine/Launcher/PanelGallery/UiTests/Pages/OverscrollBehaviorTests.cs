using Label = Sandbox.UI.Label;

namespace PanelGallery.UiTests;

[Title( "overscroll-behavior" )]
[Description( "CSS scroll chaining and boundary bounce: auto, contain and none" )]
[Icon( "swap_vert" )]
[Order( 68 )]
public class OverscrollBehaviorTests : UiTestPage
{
	public OverscrollBehaviorTests()
	{
		StyleSheet.Load( "/Pages/OverscrollBehaviorTests.scss" );
		Add.Label( "overscroll-behavior", "heading" );
		Add.Label( "Every six seconds, the inner panel starts at its bottom edge and receives a wheel step. Auto scrolls the outer panel; contain keeps the original local spring; none holds still. You can also wheel or drag inside each box.", "intro" );
		var cases = Add.Panel( "examples" );
		foreach ( var behavior in new[] { "auto", "contain", "none", "auto none" } )
		{
			var card = cases.Add.Panel( "example" );
			card.Add.Label( $"overscroll-behavior: {behavior}", "caption" );
			var demo = card.AddChild<OverscrollBehaviorDemo>();
			demo.Configure( behavior );
			demo.Readout = card.Add.Label( "Waiting for layout", "readout" );
		}
	}
}

public class OverscrollBehaviorDemo : Panel
{
	public Label Readout { get; set; }
	Panel inner;
	string behavior;
	RealTimeSince elapsed;
	bool started;
	bool checkedResult;
	float peak;
	float movement;

	public void Configure( string value )
	{
		behavior = value;
		AddClass( "outer" );
		var content = Add.Panel( "outer-content" );
		content.Add.Label( "OUTER — scrolls only with auto", "marker" );
		inner = content.Add.Panel( "inner" );
		inner.Style.Set( $"overscroll-behavior: {behavior};" );
		var rows = inner.Add.Panel( "rows" );
		for ( int i = 0; i < 12; i++ ) rows.Add.Label( $"Inner row {i + 1}", "row" );
		content.Add.Label( "OUTER CONTENT", "marker" );
	}

	public override void Tick()
	{
		base.Tick();
		if ( !IsVisible || inner is null || !inner.HasScrollY ) return;
		if ( !started || elapsed > 6 )
		{
			started = true;
			checkedResult = false;
			elapsed = 0;
			peak = 0;
			movement = 0;
			ScrollTo( 0 );
			inner.ScrollTo( inner.ScrollSize );
			inner.OnMouseWheel( new Vector2( 0, 1 ) );
		}
		if ( checkedResult ) return;
		peak = MathF.Max( peak, (inner.ScrollOffset.y - inner.ScrollSize.y) * ScaleFromScreen );
		movement = MathF.Max( movement, ScrollOffset.y * ScaleFromScreen );
		Readout.Text = $"Inner bounce: {peak:0.00}px | Outer movement: {movement:0.00}px";
		if ( elapsed < 4 ) return;
		checkedResult = true;
		var settled = MathF.Abs( inner.ScrollOffset.y - inner.ScrollSize.y ) * ScaleFromScreen < 0.1f;
		var passed = settled && (behavior == "auto" ? movement > 0 && peak == 0
			: behavior == "contain" ? movement == 0 && (RealTime.SmoothDelta >= 1.0f / 120 || peak > 0.1f) && peak <= MathF.Min( inner.Box.Rect.Height * ScaleFromScreen * 0.2f, 150 )
			: movement == 0 && peak == 0);
		Readout.Text = $"{(passed ? "PASS" : "FAIL")} — {Readout.Text}";
		Readout.SetClass( "pass", passed );
		Readout.SetClass( "fail", !passed );
	}
}
