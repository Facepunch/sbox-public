using Sandbox;
using Sandbox.UI;

namespace MenuProject.Platform;

/// <summary>
/// A scrolling panel faded off at its top while there's more of it up there, and at its bottom while there's more down
/// there - all of it shown at an end it's scrolled to. Its owner calls <see cref="Apply"/> each tick; it works on any
/// panel that scrolls, a VirtualGrid's included.
/// </summary>
public static class ScrollFade
{
	/// <summary>
	/// The mask each panel was last given - set again only when it changes, or when it's a new panel (a hotload builds
	/// them again) with no mask yet
	/// </summary>
	static readonly Dictionary<Panel, string> applied = new();

	/// <summary>
	/// Fade <paramref name="list"/> over <paramref name="fade"/> px at whichever ends have more past them. Not
	/// <paramref name="scrolls"/> - its scrolling's off - and it's all shown, whatever it holds.
	/// </summary>
	public static void Apply( Panel list, float fade = 16, bool scrolls = true )
	{
		if ( !list.IsValid() ) return;

		var height = list.Box.Rect.Height * list.ScaleFromScreen;
		if ( height <= 0 ) return;

		var above = scrolls && list.ScrollOffset.y > 1;
		var below = scrolls && list.ScrollOffset.y < list.ScrollSize.y - 2;

		Set( list, height, fade, above, below );
	}

	static void Set( Panel list, float height, float fade, bool above, bool below )
	{
		var edge = fade / height;

		// An all-white mask where there's nothing past an edge, rather than "none" - the engine takes that as an empty
		// one, hiding the lot. Stops worked out from its height, since one in pixels is measured against the mask's
		// texture rather than the panel.
		var mask = $"linear-gradient( to bottom, {(above ? "black" : "white")} 0%, white {Percent( above ? edge : 0 )}%, white {Percent( below ? 1 - edge : 1 )}%, {(below ? "black" : "white")} 100% )";

		if ( applied.TryGetValue( list, out var last ) && last == mask && list.Style.MaskImage is not null ) return;

		// The ones gone since - deleted panels don't hang on here
		if ( applied.Count > 32 )
		{
			foreach ( var gone in applied.Keys.Where( x => !x.IsValid() ).ToList() )
				applied.Remove( gone );
		}

		applied[list] = mask;
		list.Style.Set( "mask-image", mask );
	}

	static string Percent( float x ) => (Math.Clamp( x, 0, 1 ) * 100).ToString( "0.##", System.Globalization.CultureInfo.InvariantCulture );
}
