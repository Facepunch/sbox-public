using Sandbox.UI;
using System.Collections.Generic;
using System.Threading;

namespace UITests;

/// <summary>
/// The tooltip system on its own: which panel gets the tooltip, when it appears, when it goes
/// away, and what it hands the host. The host here just records what it was told.
/// </summary>
[TestClass]
[DoNotParallelize] // Panels share global UI state
public class TooltipTests
{
	/// <summary>
	/// Stands in for the game screen or a window - remembers what the tooltip system asked of it.
	/// </summary>
	sealed class RecordingHost : ITooltipHost
	{
		public Panel Shown;
		public Panel ShownFor;
		public Vector2 ShownAt;
		public int ShowCount;
		public int HideCount;
		public bool ClosedByHost;

		public void ShowTooltip( Panel tooltip, Panel owner, Vector2 cursor )
		{
			Shown = tooltip;
			ShownFor = owner;
			ShownAt = cursor;
			ShowCount++;
			ClosedByHost = false;
		}

		public bool UpdateTooltip( Panel tooltip, Vector2 cursor ) => !ClosedByHost;

		public void HideTooltip( Panel tooltip, bool immediate )
		{
			Shown = null;
			HideCount++;
		}
	}

	UISurface surface;
	RecordingHost host;
	TooltipSystem tooltips;

	[TestInitialize]
	public void Setup()
	{
		surface = new UISurface();
		host = new RecordingHost();

		tooltips = surface.Tooltips;
		tooltips.Host = host;
		tooltips.Delay = 0;
		tooltips.GraceTime = 0;
	}

	[TestCleanup]
	public void Teardown()
	{
		surface.Dispose();
	}

	Panel Add( string tooltip = null, Panel parent = null )
	{
		return new Panel { Parent = parent ?? surface.Root, Tooltip = tooltip };
	}

	static string TextOf( Panel tooltip ) => (tooltip.Children.First() as Label)?.Text;

	/// <summary>
	/// A surface's tooltips wait for the cursor to settle; the game's don't.
	/// </summary>
	[TestMethod]
	public void SurfacesWaitByDefault()
	{
		using var fresh = new UISurface();

		Assert.AreEqual( 0.5f, fresh.Tooltips.Delay );
		Assert.AreEqual( 0.0f, new UISystem().Tooltips.Delay );
	}

	/// <summary>
	/// Hovering a panel with tooltip text builds a .tooltip panel holding that text and hands it to
	/// the host, with the cursor position it was asked for.
	/// </summary>
	[TestMethod]
	public void HoverShowsTooltipText()
	{
		var panel = Add( "Hello" );

		tooltips.SetHovered( panel );
		tooltips.Frame( new Vector2( 10, 20 ), true );

		Assert.IsNotNull( host.Shown );
		Assert.IsTrue( host.Shown.HasClass( "tooltip" ) );
		Assert.AreEqual( "Hello", TextOf( host.Shown ) );
		Assert.AreEqual( panel, host.ShownFor );
		Assert.AreEqual( new Vector2( 10, 20 ), host.ShownAt );
		Assert.IsTrue( tooltips.IsShowing );
	}

	/// <summary>
	/// A panel with nothing to say gets nothing, and neither does the empty space.
	/// </summary>
	[TestMethod]
	public void NoTooltipNoShow()
	{
		var panel = Add();

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		Assert.IsNull( host.Shown );

		tooltips.SetHovered( null );
		tooltips.Frame( default, true );
		Assert.IsNull( host.Shown );
		Assert.AreEqual( 0, host.ShowCount );
	}

	/// <summary>
	/// A tooltip on a container covers the panels inside it - hovering a child with none of its own
	/// shows the parent's.
	/// </summary>
	[TestMethod]
	public void BubblesUpToTheFirstPanelWithOne()
	{
		var group = Add( "Group" );
		var child = Add( parent: group );
		var grandchild = Add( parent: child );

		tooltips.SetHovered( grandchild );
		tooltips.Frame( default, true );

		Assert.AreEqual( group, host.ShownFor );
		Assert.AreEqual( "Group", TextOf( host.Shown ) );

		// Moving between children of the same container is not a change
		tooltips.SetHovered( child );
		tooltips.Frame( default, true );

		Assert.AreEqual( 1, host.ShowCount );
		Assert.AreEqual( 0, host.HideCount );
	}

	/// <summary>
	/// Content scrolling under a resting cursor changes the hovered panel without the mouse
	/// moving. That takes the old tooltip down but shows nothing new until the mouse itself moves,
	/// so a wheel scroll doesn't flick through a tooltip for every row that passes.
	/// </summary>
	[TestMethod]
	public void ScrollingUnderARestingCursorWaitsForMovement()
	{
		var a = Add( "A" );
		var b = Add( "B" );
		var cursor = new Vector2( 10, 10 );

		tooltips.SetHovered( a, cursor );
		tooltips.Frame( cursor, true );
		Assert.AreEqual( "A", TextOf( host.Shown ) );

		// B scrolled under the same cursor position
		tooltips.SetHovered( b, cursor );
		Assert.IsFalse( tooltips.IsShowing );
		tooltips.Frame( cursor, true );
		Assert.IsFalse( tooltips.IsShowing );

		// The mouse moves, still over B
		cursor += new Vector2( 1, 0 );
		tooltips.SetHovered( b, cursor );
		tooltips.Frame( cursor, true );
		Assert.AreEqual( "B", TextOf( host.Shown ) );
	}

	/// <summary>
	/// The tooltip comes down when the cursor leaves, and moving to another panel swaps it for
	/// that panel's.
	/// </summary>
	[TestMethod]
	public void LeavingHidesAndMovingSwaps()
	{
		var a = Add( "A" );
		var b = Add( "B" );

		tooltips.SetHovered( a );
		tooltips.Frame( default, true );
		Assert.AreEqual( "A", TextOf( host.Shown ) );

		tooltips.SetHovered( b );
		Assert.AreEqual( 1, host.HideCount );
		tooltips.Frame( default, true );
		Assert.AreEqual( "B", TextOf( host.Shown ) );

		tooltips.SetHovered( null );
		Assert.AreEqual( 2, host.HideCount );
		Assert.IsFalse( tooltips.IsShowing );
	}

	/// <summary>
	/// With a delay, the tooltip waits for the cursor to rest before it appears.
	/// </summary>
	[TestMethod]
	public void WaitsForTheDelay()
	{
		tooltips.Delay = 0.1f;

		var panel = Add( "Later" );

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		Assert.IsNull( host.Shown );

		Thread.Sleep( 150 );

		tooltips.Frame( default, true );
		Assert.IsNotNull( host.Shown );
	}

	/// <summary>
	/// Straight after one tooltip closes the next opens without the wait, so a row of buttons reads
	/// one after another.
	/// </summary>
	[TestMethod]
	public void NoDelayRightAfterAnother()
	{
		tooltips.Delay = 10;
		tooltips.GraceTime = 10;

		var a = Add( "A" );
		var b = Add( "B" );

		tooltips.SetHovered( a );
		tooltips.Frame( default, true );
		Assert.IsNull( host.Shown, "the first one waits" );

		// Force it open by dropping the delay for a frame
		tooltips.Delay = 0;
		tooltips.Frame( default, true );
		tooltips.Delay = 10;
		Assert.AreEqual( "A", TextOf( host.Shown ) );

		tooltips.SetHovered( b );
		tooltips.Frame( default, true );
		Assert.AreEqual( "B", TextOf( host.Shown ), "the second one doesn't" );
	}

	/// <summary>
	/// When the host takes a tooltip down itself - a click closed its window - it stays down while
	/// the cursor is still on that panel, and comes back once the cursor has been away.
	/// </summary>
	[TestMethod]
	public void StaysDownAfterTheHostClosesIt()
	{
		var panel = Add( "Sticky" );

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		Assert.AreEqual( 1, host.ShowCount );

		host.ClosedByHost = true;
		host.Shown = null;
		tooltips.Frame( default, true );
		Assert.IsFalse( tooltips.IsShowing );

		tooltips.Frame( default, true );
		tooltips.Frame( default, true );
		Assert.AreEqual( 1, host.ShowCount, "not put straight back" );

		tooltips.SetHovered( null );
		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		Assert.AreEqual( 2, host.ShowCount, "back after leaving and returning" );
	}

	/// <summary>
	/// No cursor, no tooltip - and it returns with the cursor.
	/// </summary>
	[TestMethod]
	public void HidesWithTheCursor()
	{
		var panel = Add( "Cursor" );

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		Assert.IsTrue( tooltips.IsShowing );

		tooltips.Frame( default, false );
		Assert.IsFalse( tooltips.IsShowing );

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		Assert.IsTrue( tooltips.IsShowing );
	}

	/// <summary>
	/// OnTooltip fills the tooltip with whatever the panel wants - after the text, if there is any.
	/// </summary>
	[TestMethod]
	public void OnTooltipBuildsRichContent()
	{
		var panel = Add( "Title" );
		panel.OnTooltip = tooltip =>
		{
			tooltip.AddChild( new Image() );
			tooltip.AddChild( new Label { Text = "Detail" } );
		};

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );

		var children = host.Shown.Children.ToList();
		Assert.AreEqual( 3, children.Count );
		Assert.AreEqual( "Title", (children[0] as Label)?.Text );
		Assert.IsInstanceOfType( children[1], typeof( Image ) );
		Assert.AreEqual( "Detail", (children[2] as Label)?.Text );

		// A callback alone is enough - no text needed
		var bare = Add();
		bare.OnTooltip = tooltip => tooltip.AddChild( new Label { Text = "Only me" } );
		Assert.IsTrue( bare.HasTooltip );

		tooltips.SetHovered( bare );
		tooltips.Frame( default, true );
		Assert.AreEqual( "Only me", TextOf( host.Shown ) );
	}

	/// <summary>
	/// Plain text tooltips follow the panel's text while they're up.
	/// </summary>
	[TestMethod]
	public void TextUpdatesWhileShown()
	{
		var panel = Add( "Before" );

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );

		panel.Tooltip = "After";
		tooltips.Frame( default, true );

		Assert.AreEqual( "After", TextOf( host.Shown ) );
	}

	/// <summary>
	/// A panel that says it has a tooltip and then builds nothing isn't asked again every frame.
	/// </summary>
	[TestMethod]
	public void EmptyBuildIsAskedOnce()
	{
		var panel = new CountingPanel { Parent = surface.Root };

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		tooltips.Frame( default, true );
		tooltips.Frame( default, true );

		Assert.AreEqual( 1, panel.Builds );
		Assert.AreEqual( 0, host.ShowCount );
	}

	sealed class CountingPanel : Panel
	{
		public int Builds;

		public override bool HasTooltip => true;

		protected override Panel CreateTooltipPanel()
		{
			Builds++;
			return null;
		}
	}

	/// <summary>
	/// Clearing takes the tooltip down for good and forgets the hover.
	/// </summary>
	[TestMethod]
	public void ClearTakesEverythingDown()
	{
		var panel = Add( "Gone" );

		tooltips.SetHovered( panel );
		tooltips.Frame( default, true );
		Assert.IsTrue( tooltips.IsShowing );

		tooltips.Clear();

		Assert.IsFalse( tooltips.IsShowing );
		Assert.AreEqual( 1, host.HideCount );

		tooltips.Frame( default, true );
		Assert.AreEqual( 1, host.ShowCount, "nothing comes back on its own" );
	}

	/// <summary>
	/// Without a host of its own, a tooltip lands in the hovered panel's root - the game path.
	/// </summary>
	[TestMethod]
	public void DefaultHostPutsItInTheRoot()
	{
		var own = new UISurface();
		own.Size = new Vector2( 1000, 500 );

		var panel = new Panel { Parent = own.Root, Tooltip = "Root" };

		own.Tooltips.Delay = 0;
		own.Tooltips.SetHovered( panel );
		own.Tooltips.Frame( new Vector2( 100, 100 ), true );

		var tooltip = own.Tooltips.Current;
		Assert.IsNotNull( tooltip );
		Assert.AreEqual( own.Root, tooltip.Parent );

		// Left of centre and below the top edge - so it sits right of and above the cursor
		Assert.IsNotNull( tooltip.Style.Left );
		Assert.IsNotNull( tooltip.Style.Bottom );
		Assert.IsNull( tooltip.Style.Right );
		Assert.IsNull( tooltip.Style.Top );

		own.Tooltips.SetHovered( null );
		Assert.IsTrue( tooltip.IsDeleting );

		own.Dispose();
	}
}
