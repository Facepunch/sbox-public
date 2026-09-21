using System;

namespace Sandbox.UI;

public partial class Popup
{
	/// <summary>
	/// Place side-by-side popups on whichever side has room, or use above/below placement.
	/// All arguments use the same coordinate space.
	/// </summary>
	internal static Vector2 AnchorPosition( Rect anchor, Vector2 size, Rect bounds, PositionMode position, float gap )
	{
		if ( position != PositionMode.RightTop )
			return AnchorPosition( anchor, size, bounds, position == PositionMode.AboveLeft, gap );

		var rightSpace = bounds.Right - anchor.Right - gap;
		var leftSpace = anchor.Left - bounds.Left - gap;
		var x = rightSpace < size.x && leftSpace > rightSpace ? anchor.Left - gap - size.x : anchor.Right + gap;
		return new Vector2(
			Math.Clamp( x, bounds.Left, Math.Max( bounds.Left, bounds.Right - size.x ) ),
			Math.Clamp( anchor.Top, bounds.Top, Math.Max( bounds.Top, bounds.Bottom - size.y ) ) );
	}

	/// <summary>
	/// Choose the requested side of an anchor, flip if the other side fits better, then clamp
	/// to the available bounds. All arguments use the same coordinate space.
	/// </summary>
	internal static Vector2 AnchorPosition( Rect anchor, Vector2 size, Rect bounds, bool above, float gap )
	{
		var belowSpace = bounds.Bottom - anchor.Bottom - gap;
		var aboveSpace = anchor.Top - bounds.Top - gap;
		if ( above ? aboveSpace < size.y && belowSpace > aboveSpace : belowSpace < size.y && aboveSpace > belowSpace )
			above = !above;

		return new Vector2(
			Math.Clamp( anchor.Left, bounds.Left, Math.Max( bounds.Left, bounds.Right - size.x ) ),
			Math.Clamp( above ? anchor.Top - gap - size.y : anchor.Bottom + gap,
				bounds.Top, Math.Max( bounds.Top, bounds.Bottom - size.y ) ) );
	}
}
