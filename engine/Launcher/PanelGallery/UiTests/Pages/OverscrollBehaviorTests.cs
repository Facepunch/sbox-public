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
		Add.Label( "Wheel or drag inside each box. At the inner panel's edge, auto scrolls the outer panel; contain bounces locally; none holds still.", "intro" );
		var cases = Add.Panel( "examples" );
		foreach ( var behavior in new[] { "auto", "contain", "none", "auto none" } )
		{
			var card = cases.Add.Panel( "example" );
			card.Add.Label( $"overscroll-behavior: {behavior}", "caption" );
			var demo = card.AddChild<OverscrollBehaviorDemo>();
			demo.Configure( behavior );
		}

		Add.Label( "Single scroll containers", "heading" );
		Add.Label( "Wheel or drag past either edge to compare bounce on and off. These examples stay where you scroll them.", "intro" );
		var singleCases = Add.Panel( "examples" );
		foreach ( var behavior in new[] { "contain", "none" } )
		{
			var card = singleCases.Add.Panel( "example" );
			card.Add.Label( $"Bounce {(behavior == "contain" ? "on" : "off")} — overscroll-behavior: {behavior}", "caption" );
			var scroll = card.Add.Panel( "single" );
			scroll.Style.Set( $"overscroll-behavior: {behavior};" );
			var rows = scroll.Add.Panel( "rows" );
			for ( int i = 0; i < 12; i++ ) rows.Add.Label( $"Row {i + 1}", "row" );
		}
	}
}

public class OverscrollBehaviorDemo : Panel
{
	public void Configure( string behavior )
	{
		AddClass( "outer" );
		var content = Add.Panel( "outer-content" );
		content.Add.Label( "OUTER — scrolls only with auto", "marker" );
		var inner = content.Add.Panel( "inner" );
		inner.Style.Set( $"overscroll-behavior: {behavior};" );
		var rows = inner.Add.Panel( "rows" );
		for ( int i = 0; i < 12; i++ ) rows.Add.Label( $"Inner row {i + 1}", "row" );
		content.Add.Label( "OUTER CONTENT", "marker" );
	}
}
