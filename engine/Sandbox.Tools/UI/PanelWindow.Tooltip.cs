using Sandbox.UI;

namespace Editor;

//
// Tooltips. A window's panels get their tooltips the same way panels in a game do - Panel.Tooltip,
// or OnTooltip for something richer - but here they open in a window of their own, so they can
// hang outside the window like the OS's do. The engine's TooltipSystem decides when; this is where.
//
public partial class PanelWindow : ITooltipHost
{
	PanelWindow _tooltip;

	/// <summary>
	/// The tooltip window open over this one, if there is one.
	/// </summary>
	public PanelWindow TooltipWindow => _tooltip is { IsOpen: true } ? _tooltip : null;

	void ITooltipHost.ShowTooltip( Panel tooltip, Panel owner, Vector2 cursor )
	{
		CloseTooltip();

		// Below and to the right of the cursor, clear of the arrow, the way OS tooltips sit. The
		// OS keeps it on the screen from there.
		var offset = new Vector2( 12, 20 ) * Surface.DpiScale;

		var window = Popup( this, cursor + offset, ignoresInput: true );
		window.Root.AddClass( "os-tooltip" );

		// The tooltip is a piece of the UI it came from, in another window - so it's styled by
		// the sheets that style its owner, wherever up the tree they were loaded. Outermost
		// first, so they cascade in the order the owner sees them in.
		var sheets = window.Root.StyleSheet;

		foreach ( var sheet in OwnerStyleSheets( owner ) )
		{
			sheets.Add( sheet );
		}

		tooltip.Parent = window.Root;

		// The panel came positioned absolutely for a root it would float over. Here the window
		// is what floats, so it's laid out as an ordinary child - and it has to be: an absolute
		// panel is measured before its max-width applies, so long text gets a one-line box.
		tooltip.Style.Position = PositionMode.Relative;
		tooltip.Style.Left = null;
		tooltip.Style.Top = null;
		tooltip.Style.Right = null;
		tooltip.Style.Bottom = null;

		_tooltip = window;
	}

	bool ITooltipHost.UpdateTooltip( Panel tooltip, Vector2 cursor )
	{
		// It stays where it opened. If it's gone, a click took it down along with the other
		// popups - saying so keeps the tooltip system from putting it straight back.
		return _tooltip is { IsOpen: true };
	}

	void ITooltipHost.HideTooltip( Panel tooltip, bool immediate ) => CloseTooltip();

	void CloseTooltip()
	{
		var window = _tooltip;
		_tooltip = null;

		window?.Dispose();
	}

	/// <summary>
	/// Every stylesheet in force on a panel, from the root down to the panel itself, oldest first.
	/// Adding them in this order to another collection leaves it arranged the way the owner's are.
	/// </summary>
	static IEnumerable<StyleSheet> OwnerStyleSheets( Panel owner )
	{
		var chain = new List<Panel>();

		for ( var panel = owner; panel is not null; panel = panel.Parent )
		{
			chain.Add( panel );
		}

		for ( int i = chain.Count - 1; i >= 0; i-- )
		{
			var list = chain[i].StyleSheet.List;
			if ( list is null ) continue;

			// A collection keeps its newest sheet first
			for ( int j = list.Count - 1; j >= 0; j-- )
			{
				yield return list[j];
			}
		}
	}
}
