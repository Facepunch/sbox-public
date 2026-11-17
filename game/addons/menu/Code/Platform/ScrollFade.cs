using Sandbox;
using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// A scrolling panel faded off at its top while there's more of it up there, and at its bottom while there's more down
/// there - all of it shown at an end it's scrolled to. Its owner keeps one per panel it fades, and calls
/// <see cref="Apply"/> each tick; it works on any panel that scrolls, a VirtualGrid's included.
/// </summary>
public sealed class ScrollFade
{
	readonly float fade;

	// What it last gave - the mask's only made again when one of these changes, not every frame
	Panel shown;
	bool shownAbove;
	bool shownBelow;
	float shownEdge = -1;

	/// <param name="fade">How far, in px, it fades over at an end</param>
	public ScrollFade( float fade = 16 )
	{
		this.fade = fade;
	}

	/// <summary>
	/// Fade <paramref name="list"/> at whichever ends have more past them. Not <paramref name="scrolls"/> - its
	/// scrolling's off - and it's all shown, whatever it holds.
	/// </summary>
	public void Apply( Panel list, bool scrolls = true )
	{
		if ( !list.IsValid() ) return;

		var height = list.Box.Rect.Height * list.ScaleFromScreen;
		if ( height <= 0 ) return;

		var above = scrolls && list.ScrollOffset.y > 1;
		var below = scrolls && list.ScrollOffset.y < list.ScrollSize.y - 2;
		var edge = MathF.Round( fade / height, 4 );

		// A new panel (a hotload builds them again) has no mask yet
		if ( list == shown && above == shownAbove && below == shownBelow && edge == shownEdge && list.Style.MaskImage is not null ) return;

		shown = list;
		shownAbove = above;
		shownBelow = below;
		shownEdge = edge;

		list.Style.Set( "mask-image", Mask( above, below, edge ) );
	}

	/// <summary>
	/// An all-white mask where there's nothing past an edge, rather than "none" - the engine takes that as an empty one,
	/// hiding the lot. Stops worked out from its height, since one in pixels is measured against the mask's texture
	/// rather than the panel.
	/// </summary>
	static string Mask( bool above, bool below, float edge ) =>
		$"linear-gradient( to bottom, {(above ? "black" : "white")} 0%, white {Percent( above ? edge : 0 )}%, white {Percent( below ? 1 - edge : 1 )}%, {(below ? "black" : "white")} 100% )";

	static string Percent( float x ) => (Math.Clamp( x, 0, 1 ) * 100).ToString( "0.##", System.Globalization.CultureInfo.InvariantCulture );
}
