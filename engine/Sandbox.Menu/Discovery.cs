using System;
using Sandbox.UI;
using Sandbox.UI.Navigation;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Describes a place in the menu that shows packages: a front page shelf, search results, a friend's
/// activity. Panels declare it by implementing <see cref="IDiscoverySurface"/>; every package tile
/// underneath them picks it up, so moving tiles between pages doesn't need any tracking changes.
/// </summary>
public sealed class DiscoveryContext
{
	/// <summary>
	/// Which part of the menu, e.g. "home", "search", "friends". Keep these stable, dashboards group by them.
	/// </summary>
	public string Surface { get; init; }

	/// <summary>
	/// Which shelf or row within the surface, if it has more than one.
	/// </summary>
	public string Shelf { get; init; }

	/// <summary>
	/// The <see cref="Package.ListResult.RequestId"/> the packages came from, if any.
	/// </summary>
	public Guid List { get; init; }

	/// <summary>
	/// The query behind the packages, if any. Only tells apart views from different searches - it's
	/// never sent, player-typed text stays on the machine.
	/// </summary>
	public string Query { get; init; }

	/// <summary>
	/// The packages in the order they're shown, so a tile's position can be worked out.
	/// </summary>
	public IReadOnlyList<Package> Packages { get; init; }

	public int PositionOf( Package package )
	{
		if ( Packages is null || package is null ) return -1;

		for ( int i = 0; i < Packages.Count; i++ )
		{
			if ( Packages[i]?.FullIdent == package.FullIdent )
				return i;
		}

		return -1;
	}
}

/// <summary>
/// A panel that shows packages and says where they're shown. See <see cref="DiscoveryContext"/>.
/// </summary>
public interface IDiscoverySurface
{
	DiscoveryContext DiscoveryContext { get; }
}

/// <summary>
/// Records how players find games: which tiles they saw, hovered and clicked, and where the game they
/// launched came from. Tiles call <see cref="Track"/> every tick; the rest is called at the matching
/// user action. Where a launched game came from also goes on the activity heartbeat.
/// </summary>
public static class Discovery
{
	/// <summary>
	/// A tile counts as seen once at least half of it has been on screen for this long.
	/// </summary>
	const float SeenSeconds = 1.0f;
	const float SeenFraction = 0.5f;

	/// <summary>
	/// A click on a tile still explains a launch this much later - the player may read the game page first.
	/// </summary>
	const float ClickLifetime = 30 * 60;

	const int FlushCount = 50;
	const float FlushSeconds = 15;

	sealed class Tile
	{
		public string Ident;
		public float VisibleFor;
		public bool Reported;
	}

	static readonly ConditionalWeakTable<Panel, Tile> tiles = new();
	static readonly HashSet<string> seen = new();
	static readonly List<Dictionary<string, object>> pending = new();
	static RealTimeSince sinceFlush;

	static string clickedIdent;
	static DiscoveryContext clickedContext;
	static int clickedPosition;
	static RealTimeSince clickedAge;

	/// <summary>
	/// Call every tick from a panel that shows a package. Reports it once when it has been seen.
	/// </summary>
	public static void Track( Panel tile, Package package )
	{
		if ( package is null || !tile.IsValid() ) return;

		var state = tiles.GetOrCreateValue( tile );

		// Virtualised lists reuse panels for other packages
		if ( state.Ident != package.FullIdent )
		{
			state.Ident = package.FullIdent;
			state.VisibleFor = 0;
			state.Reported = false;
		}

		if ( !state.Reported )
		{
			state.VisibleFor = VisibleFraction( tile ) >= SeenFraction ? state.VisibleFor + RealTime.Delta : 0;

			if ( state.VisibleFor >= SeenSeconds )
			{
				state.Reported = true;
				Seen( tile, package );
			}
		}

		if ( pending.Count >= FlushCount || (pending.Count > 0 && sinceFlush > FlushSeconds) )
			Flush();
	}

	/// <summary>
	/// The hover card for a package opened.
	/// </summary>
	public static void Hovered( Panel source, Package package )
	{
		if ( package is null ) return;

		var context = Find( source );
		Submit( "discovery.hover", Describe( package, context, context.PositionOf( package ) ) );
	}

	/// <summary>
	/// A package tile was clicked, usually opening its page.
	/// </summary>
	public static void Clicked( Panel source, Package package ) => Clicked( Find( source ), package );

	public static void Clicked( DiscoveryContext context, Package package )
	{
		if ( package is null ) return;

		context ??= new DiscoveryContext { Surface = "unknown" };
		Clicked( context, package.FullIdent, context.PositionOf( package ) );
	}

	/// <summary>
	/// A package was opened from outside the menu, like a link from the website.
	/// </summary>
	public static void Clicked( DiscoveryContext context, string ident ) => Clicked( context ?? new DiscoveryContext { Surface = "unknown" }, ident, -1 );

	static void Clicked( DiscoveryContext context, string ident, int position )
	{
		if ( string.IsNullOrEmpty( ident ) ) return;

		clickedIdent = ident;
		clickedContext = context;
		clickedPosition = position;
		clickedAge = 0;

		Submit( "discovery.click", Describe( ident, context, position ) );
	}

	/// <summary>
	/// The player is launching a package. <paramref name="via"/> names the button, e.g. "gamepage" or
	/// "hovercard". Without a source panel the launch is credited to the tile last clicked for this package.
	/// </summary>
	public static void Launching( Package package, string via, Panel source = null )
	{
		if ( package is null ) return;

		DiscoveryContext context;
		int position;

		if ( source is not null )
		{
			context = Find( source );
			position = context.PositionOf( package );
		}
		else if ( clickedIdent == package.FullIdent && clickedAge < ClickLifetime )
		{
			context = clickedContext;
			position = clickedPosition;
		}
		else
		{
			context = new DiscoveryContext { Surface = "unknown" };
			position = -1;
		}

		var data = Describe( package, context, position );
		data["via"] = via;

		// Views first, so the play lands after what led to it
		Flush();
		Submit( "discovery.play", data );

		Api.Activity.GameRequested( new Api.Activity.Origin( "menu", package.FullIdent, context.Surface, context.Shelf, position, ListId( context ), via ) );
	}

	/// <summary>
	/// The context a panel is shown in: the nearest <see cref="IDiscoverySurface"/>, or the page url.
	/// </summary>
	public static DiscoveryContext Find( Panel panel )
	{
		if ( panel is null ) return new DiscoveryContext { Surface = "unknown" };

		foreach ( var p in panel.AncestorsAndSelf )
		{
			if ( p is IDiscoverySurface { DiscoveryContext: { } context } )
				return context;
		}

		return new DiscoveryContext { Surface = PageSurface( panel ) };
	}

	/// <summary>
	/// The url of the page a panel is on, without its query, e.g. "games/all".
	/// </summary>
	public static string PageSurface( Panel panel )
	{
		var url = panel?.Ancestors.OfType<NavigationHost>().FirstOrDefault()?.CurrentUrl;
		return url?.Split( '?' )[0].Trim( '/' ) ?? "unknown";
	}

	static void Seen( Panel tile, Package package )
	{
		var context = Find( tile );
		var key = $"{context.Surface}|{context.Shelf}|{context.List}|{context.Query}|{package.FullIdent}";

		if ( seen.Count > 10_000 ) seen.Clear();
		if ( !seen.Add( key ) ) return;

		if ( pending.Count == 0 ) sinceFlush = 0;
		pending.Add( Describe( package, context, context.PositionOf( package ) ) );
	}

	static void Flush()
	{
		if ( pending.Count == 0 ) return;

		var e = new Api.Events.EventRecord( "discovery.view" );
		e.SetValue( "items", pending.ToArray() );
		e.Submit();

		pending.Clear();
	}

	static void Submit( string name, Dictionary<string, object> data )
	{
		var e = new Api.Events.EventRecord( name );
		foreach ( var (k, v) in data ) e.SetValue( k, v );
		e.Submit();
	}

	static Dictionary<string, object> Describe( Package package, DiscoveryContext context, int position ) => Describe( package.FullIdent, context, position );

	static Dictionary<string, object> Describe( string ident, DiscoveryContext context, int position )
	{
		var d = new Dictionary<string, object>
		{
			["ident"] = ident,
			["surface"] = context.Surface,
		};

		if ( context.Shelf is not null ) d["shelf"] = context.Shelf;
		if ( position >= 0 ) d["pos"] = position;
		if ( ListId( context ) is { } list ) d["list"] = list;

		return d;
	}

	static string ListId( DiscoveryContext context ) => context.List == Guid.Empty ? null : context.List.ToString( "N" );

	/// <summary>
	/// How much of the panel is on screen, after clipping by every scrolling or clipping ancestor.
	/// </summary>
	static float VisibleFraction( Panel panel )
	{
		if ( !panel.IsVisible ) return 0;

		var rect = panel.Box.Rect;
		var area = rect.Width * rect.Height;
		if ( area <= 0 ) return 0;

		var clip = Rect.Intersect( rect, new Rect( 0, 0, Screen.Width, Screen.Height ) );

		foreach ( var ancestor in panel.Ancestors )
		{
			if ( ancestor.ComputedStyle?.Overflow is null or OverflowMode.Visible ) continue;
			clip = Rect.Intersect( clip, ancestor.Box.ClipRect );
		}

		if ( clip.Width <= 0 || clip.Height <= 0 ) return 0;
		return clip.Width * clip.Height / area;
	}
}
