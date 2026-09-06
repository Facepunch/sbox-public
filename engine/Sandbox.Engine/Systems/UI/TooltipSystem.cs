namespace Sandbox.UI;

/// <summary>
/// Where a UI's tooltips end up. The default drops them into the hovered panel's own root, which is
/// what a game screen wants. A window hosting a surface implements this to put them in a window of
/// their own instead, so they can hang outside the window like the OS's do.
/// </summary>
internal interface ITooltipHost
{
	/// <summary>
	/// A tooltip was just built for <paramref name="owner"/> and should appear. The cursor is in
	/// the UI's own pixels.
	/// </summary>
	void ShowTooltip( Panel tooltip, Panel owner, Vector2 cursor );

	/// <summary>
	/// Called every frame the tooltip is up. Return false if the host has already taken it down -
	/// a click closed the window, say - so the tooltip stays down until the cursor moves on.
	/// </summary>
	bool UpdateTooltip( Panel tooltip, Vector2 cursor );

	/// <summary>
	/// Take the tooltip down. Immediate means no outro - the UI it belongs to is going away.
	/// </summary>
	void HideTooltip( Panel tooltip, bool immediate );
}

/// <summary>
/// Tooltips for one <see cref="UISystem"/>: which panel is being hovered, whether it has waited long
/// enough, and the tooltip panel that's up. Every UI instance has one - the game screen, the menu,
/// and each window the editor opens - so a tooltip in one window has nothing to do with another.
/// </summary>
internal sealed class TooltipSystem
{
	// The panel whose tooltip is up or pending - the first one up the tree from the cursor that has one
	Panel hovered;
	Panel tooltip;

	// When the cursor arrived on the hovered panel, and when a tooltip was last taken down
	float hoverStart;
	float lastHidden = float.NegativeInfinity;

	// A panel whose tooltip went away for a reason other than the cursor leaving - a click, or
	// there being nothing to show. It stays down until the cursor moves off and comes back,
	// the way the OS's do, rather than popping straight back up under the cursor.
	Panel suppressed;

	// The hover changed under a still cursor - content scrolled beneath it. No tooltip until it moves.
	Vector2 lastCursor;
	bool waitForMove;

	/// <summary>
	/// How long the cursor rests on a panel before its tooltip appears, in seconds. Game UI shows
	/// them straight away; a surface waits, the way the desktop does.
	/// </summary>
	public float Delay { get; set; }

	/// <summary>
	/// For this long after a tooltip closes, the next one opens without waiting for
	/// <see cref="Delay"/> - moving along a row of buttons reads their tooltips one after another.
	/// </summary>
	public float GraceTime { get; set; } = 0.5f;

	/// <summary>
	/// Where tooltips go. The default puts them in the hovered panel's root.
	/// </summary>
	internal ITooltipHost Host { get; set; } = new RootHost();

	/// <summary>
	/// The tooltip panel that's up right now, if any.
	/// </summary>
	public Panel Current => tooltip.IsValid() ? tooltip : null;

	/// <summary>
	/// Is a tooltip up?
	/// </summary>
	public bool IsShowing => tooltip.IsValid();

	/// <summary>
	/// The panel under the cursor this frame, or null. Walks up to the first ancestor with a
	/// tooltip, so a tooltip on a container covers everything in it.
	/// </summary>
	internal void SetHovered( Panel current, Vector2 cursor )
	{
		var moved = cursor != lastCursor;
		lastCursor = cursor;

		SetHovered( current, moved );
	}

	/// <summary>
	/// The panel under the cursor this frame, treating it as the cursor having moved there.
	/// </summary>
	internal void SetHovered( Panel current ) => SetHovered( current, true );

	void SetHovered( Panel current, bool moved )
	{
		while ( current is not null && !current.HasTooltip )
		{
			current = current.Parent;
		}

		if ( moved )
			waitForMove = false;

		if ( current == hovered )
			return;

		Hide( false );

		hovered = current;
		hoverStart = RealTime.Now;

		// Scrolling brought a new panel under a resting cursor - showing its tooltip would flicker
		// through every row that passes. Wait for the mouse itself to move.
		if ( !moved )
			waitForMove = true;

		// The cursor moved on - whatever kept the old panel's tooltip down is over
		if ( suppressed != current )
			suppressed = null;
	}

	/// <summary>
	/// Once a frame. Shows the pending tooltip when it's time, keeps the shown one up to date, and
	/// takes it down when the cursor goes away.
	/// </summary>
	internal void Frame( Vector2 cursor, bool cursorVisible )
	{
		if ( !cursorVisible )
		{
			// No cursor, no tooltip - and forget the hover, so it comes back with the cursor
			Hide( false );
			hovered = null;
			return;
		}

		if ( tooltip.IsValid() )
		{
			if ( !hovered.IsValid() )
			{
				Hide( false );
				return;
			}

			if ( !Host.UpdateTooltip( tooltip, cursor ) )
			{
				// The host took it down itself - a click on the window under it, say
				tooltip = null;
				lastHidden = RealTime.Now;
				suppressed = hovered;
				return;
			}

			hovered.UpdateTooltip( tooltip );
			return;
		}

		if ( hovered is null || hovered == suppressed || waitForMove )
			return;

		if ( !hovered.IsValid() )
		{
			hovered = null;
			return;
		}

		var delay = RealTime.Now - lastHidden < GraceTime ? 0.0f : Delay;
		if ( RealTime.Now - hoverStart < delay )
			return;

		Show( cursor );
	}

	void Show( Vector2 cursor )
	{
		tooltip = hovered.BuildTooltip();

		// Nothing to show after all - HasTooltip said yes but the panel built nothing. Don't
		// ask again every frame; wait for the cursor to move on.
		if ( tooltip is null )
		{
			suppressed = hovered;
			return;
		}

		Host.ShowTooltip( tooltip, hovered, cursor );

		// Positioned on the frame it appears, not the one after
		Host.UpdateTooltip( tooltip, cursor );
	}

	void Hide( bool immediate )
	{
		if ( tooltip is null )
			return;

		var current = tooltip;
		tooltip = null;
		lastHidden = RealTime.Now;

		if ( current.IsValid() )
			Host.HideTooltip( current, immediate );
	}

	/// <summary>
	/// The UI is being torn down. Drop the tooltip and everything we remember.
	/// </summary>
	internal void Clear()
	{
		Hide( true );
		hovered = null;
		suppressed = null;
	}

	/// <summary>
	/// The default home for a tooltip: the hovered panel's own root, positioned beside the cursor
	/// and kept on the screen.
	/// </summary>
	sealed class RootHost : ITooltipHost
	{
		public void ShowTooltip( Panel tooltip, Panel owner, Vector2 cursor )
		{
			// Panels that build their own tooltip may have put it somewhere already
			if ( tooltip.Parent is null )
				tooltip.Parent = owner.FindRootPanel();
		}

		public bool UpdateTooltip( Panel tooltip, Vector2 cursor )
		{
			//
			// Given the mouse position, try to position the tooltip so it's not hanging off the
			// screen - it goes to the left of the cursor in the right hand part of the screen,
			// and below it along the top edge.
			//

			var size = tooltip.UISystem.Size;

			TextFlag align = 0;

			if ( cursor.x < size.x * 0.70f )
			{
				align |= TextFlag.Right;
			}
			else
			{
				align |= TextFlag.Left;
			}

			if ( cursor.y > size.y * 0.1f )
			{
				align |= TextFlag.Top;
			}
			else
			{
				align |= TextFlag.Bottom;
			}

			tooltip.SetAbsolutePosition( align, cursor, 20 );

			return true;
		}

		public void HideTooltip( Panel tooltip, bool immediate )
		{
			tooltip.Delete( immediate );
		}
	}
}
