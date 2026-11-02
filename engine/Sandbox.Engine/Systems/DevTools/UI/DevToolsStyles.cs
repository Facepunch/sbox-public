using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The dev tools' look. Self contained, because the game has none of the editor's stylesheets to lean on.
/// <para>
/// A tool pane, not a page: neutral greys, flat sections under header bands, numbers in tables. Colour is kept for
/// status - a value that's out of line goes amber or red - and for telling series apart in a chart.
/// </para>
/// </summary>
internal static class DevToolsStyles
{
	internal static readonly Color Good = Color.Parse( "#7CC46A" ).Value;
	internal static readonly Color Warning = Color.Parse( "#E8B04A" ).Value;
	internal static readonly Color Bad = Color.Parse( "#E45A4C" ).Value;
	internal static readonly Color Text = Color.Parse( "#D9D9D9" ).Value;
	internal static readonly Color Bright = Color.Parse( "#F2F2F2" ).Value;
	internal static readonly Color Dim = Color.Parse( "#8C8C8C" ).Value;
	internal static readonly Color Faint = Color.Parse( "#5E5E5E" ).Value;
	internal static readonly Color Line = Color.Parse( "#2B2B2B" ).Value;
	internal static readonly Color Raised = Color.Parse( "#242424" ).Value;
	internal static readonly Color Plot = Color.Parse( "#111111" ).Value;

	/// <summary>
	/// The frame chart's series. CPU work is the bar, the GPU a line over it.
	/// </summary>
	internal static readonly Color Cpu = Color.Parse( "#B3B3B3" ).Value;
	internal static readonly Color Gpu = Color.Parse( "#E0823D" ).Value;
	internal static readonly Color CapWait = Color.Parse( "#333333" ).Value;
	internal static readonly Color SelfCost = Color.Parse( "#686868" ).Value;

	/// <summary>
	/// For series that need telling apart - GPU pass families, timeline scopes. Muted, and kept off the status colours.
	/// </summary>
	internal static readonly Color[] Series =
	[
		Color.Parse( "#C2A46E" ).Value, Color.Parse( "#8FAE6B" ).Value, Color.Parse( "#C47F63" ).Value, Color.Parse( "#A98BB3" ).Value,
		Color.Parse( "#78AA9A" ).Value, Color.Parse( "#C9B45A" ).Value, Color.Parse( "#9C9C9C" ).Value, Color.Parse( "#B88F8F" ).Value,
	];

	/// <summary>
	/// The main thread categories, muted like the rest. The ones that usually carry the frame get colours of their own;
	/// the rest share greys.
	/// </summary>
	internal static Color Category( PerformanceStats.Timings timing ) => timing.Name switch
	{
		"Render" => Color.Parse( "#8FAE6B" ).Value,
		"Update" => Color.Parse( "#78AA9A" ).Value,
		"Physics" => Color.Parse( "#C47F63" ).Value,
		"Animation" => Color.Parse( "#A98BB3" ).Value,
		"UI" => Color.Parse( "#C2A46E" ).Value,
		"Network" => Color.Parse( "#B88F8F" ).Value,
		"Particles" => Color.Parse( "#C9B45A" ).Value,
		"GcPause" => Color.Parse( "#D07A6E" ).Value,
		"Audio" => Color.Parse( "#9C9C9C" ).Value,
		_ => Color.Parse( "#7A7A7A" ).Value,
	};

	/// <summary>
	/// What a category is called on screen.
	/// </summary>
	internal static string CategoryName( PerformanceStats.Timings timing ) => timing.Name == "GcPause" ? "GC pause" : timing.Name;

	internal static Color For( DevTools.Severity severity ) => severity switch
	{
		DevTools.Severity.Good => Good,
		DevTools.Severity.Info => Dim,
		DevTools.Severity.Warning => Warning,
		_ => Bad,
	};

	internal static string ClassFor( DevTools.Severity severity ) => severity switch
	{
		DevTools.Severity.Good => "good",
		DevTools.Severity.Info => "info",
		DevTools.Severity.Warning => "warning",
		_ => "bad",
	};

	static StyleSheet _sheet;
	internal static StyleSheet Sheet => _sheet ??= StyleSheet.FromString( Styles, "devtools" );

	const string Styles = """
		$chrome: #0e0e0e;
		$bg: #161616;
		$band: #1e1e1e;
		$plot: #111111;
		$line: #2b2b2b;
		$line-soft: #212121;
		$hover: #242424;
		$text: #d9d9d9;
		$bright: #f2f2f2;
		$dim: #8c8c8c;
		$faint: #5e5e5e;
		$good: #7cc46a;
		$warn: #e8b04a;
		$bad: #e45a4c;

		.devtools-root
		{
			font-family: Inter;
			font-size: 12px;
			color: $text;
			pointer-events: none;
		}

		//
		// Shell: one strip with the tabs and the buttons, then the page
		//

		// The whole of a window of its own. Opaque: translucent, faint copies of earlier frames showed through it - not
		// from the swap chain, which is cleared every frame; the cause isn't pinned down
		.devtools
		{
			width: 100%;
			height: 100%;
			flex-direction: column;
			background-color: $bg;
			pointer-events: all;
			overflow: hidden;
		}

		.devtools .strip
		{
			flex-shrink: 0;
			height: 30px;
			align-items: stretch;
			background-color: $chrome;
			border-bottom: 1px solid $line;
		}

		.devtools .strip .title
		{
			align-items: center;
			padding: 0px 10px;
			color: $dim;
			font-weight: 600;
		}

		.devtools .tabs
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-width: 0px;
			overflow: hidden;
		}

		.devtools .tab
		{
			flex-shrink: 0;
			padding: 0px 11px;
			align-items: center;
			color: $dim;
			cursor: pointer;
			border-right: 1px solid $line-soft;
		}

		.devtools .tab:hover
		{
			color: $text;
			background-color: $hover;
		}

		.devtools .tab.active
		{
			color: $bright;
			background-color: $bg;
		}

		.devtools .strip .grow
		{
			flex-grow: 1;
		}

		.devtools .host-controls
		{
			flex-shrink: 0;
			align-items: stretch;
		}

		.devtools .host-controls .divider
		{
			width: 1px;
			margin: 7px 3px;
			background-color: $line;
		}

		// A label: text-align centres its glyph across the button, align-items down it
		.devtools .icon-button
		{
			width: 32px;
			text-align: center;
			align-items: center;
			color: $dim;
			font-family: Material Icons;
			font-size: 16px;
			cursor: pointer;
		}

		.devtools .icon-button:hover
		{
			background-color: $hover;
			color: $bright;
		}

		.devtools .icon-button.active
		{
			color: $bright;
			background-color: $hover;
		}

		.devtools .icon-button.close:hover
		{
			background-color: #c42b1c;
			color: white;
		}

		// Docked along the bottom of the game window, under a grip that drags its height. A little of the game shows
		// through; a window of its own stays opaque
		.game-dock
		{
			position: absolute;
			left: 0px;
			right: 0px;
			bottom: 0px;
			flex-direction: column;
			pointer-events: all;
			opacity: 0.92;
		}

		.game-dock > .devtools
		{
			height: auto;
			flex-grow: 1;
			min-height: 0px;
		}

		.game-dock .dock-grip
		{
			flex-shrink: 0;
			height: 4px;
			background-color: $line;
			cursor: ns-resize;
		}

		.game-dock .dock-grip:hover, .game-dock .dock-grip.dragging
		{
			background-color: $faint;
		}

		// The strip buttons' menus. Popups: in a window of their own from a popped out dev tools, so nothing here can
		// lean on being inside .devtools. Every root loads the base popup look, rounded and animated; this beats it
		.popup-panel.devtools-menu
		{
			position: absolute;
			flex-direction: column;
			min-width: 220px;
			padding: 4px 0px;
			background-color: $band;
			border: 1px solid $line;
			border-radius: 0px;
			box-shadow: none;
			transition: none;
			font-family: Inter;
			font-size: 12px;
			color: $text;
			pointer-events: all;
			overflow-y: scroll;
		}

		.devtools-menu .menu-section
		{
			flex-shrink: 0;
			padding: 6px 10px 3px 10px;
			color: $faint;
			font-size: 11px;
		}

		.devtools-menu .menu-row
		{
			flex-shrink: 0;
			height: 24px;
			align-items: center;
			gap: 6px;
			padding: 0px 10px 0px 6px;
			cursor: pointer;
		}

		.devtools-menu .menu-row:hover
		{
			background-color: $hover;
			color: $bright;
		}

		.devtools-menu .menu-row.selected
		{
			color: $bright;
		}

		.devtools-menu .menu-row.disabled
		{
			color: $faint;
			cursor: default;
		}

		.devtools-menu .menu-row.disabled:hover
		{
			background-color: transparent;
		}

		.devtools-menu .menu-row.stepper
		{
			cursor: default;
		}

		.devtools-menu .menu-check, .devtools-menu .menu-icon, .devtools-menu .menu-step
		{
			flex-shrink: 0;
			width: 16px;
			text-align: center;
			font-family: Material Icons;
			font-size: 14px;
			color: $dim;
		}

		.devtools-menu .menu-text
		{
			flex-grow: 1;
			white-space: nowrap;
		}

		.devtools-menu .menu-step
		{
			width: 22px;
			height: 22px;
			align-items: center;
			cursor: pointer;
		}

		.devtools-menu .menu-step:hover
		{
			background-color: $line;
			color: $bright;
		}

		.devtools-menu .menu-value
		{
			min-width: 34px;
			text-align: center;
			font-family: Roboto Mono;
		}

		.devtools .body
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
			flex-direction: row;
		}

		.devtools .workspace
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
			min-width: 0px;
			flex-direction: column;
		}

		// Issue counts in the strip, the way dev tools count errors and warnings
		.devtools .badges
		{
			flex-shrink: 0;
			align-items: center;
			gap: 10px;
			padding: 0px 10px;
			cursor: pointer;
			font-family: Roboto Mono;
		}

		.devtools .badges:hover
		{
			background-color: $hover;
		}

		.devtools .badge
		{
			align-items: center;
			gap: 3px;
		}

		.devtools .badge-icon
		{
			font-family: Material Icons;
			font-size: 15px;
		}

		.devtools .badge.bad { color: $bad; }
		.devtools .badge.warning { color: $warn; }

		.devtools .section-title.bad { color: $bad; }

		// The budget ruler, and the strip of recent frames under it
		.devtools .budget
		{
			flex-shrink: 0;
			height: 176px;
			cursor: pointer;
		}

		.devtools .dock-history
		{
			flex-shrink: 0;
			height: 34px;
			margin: 0px 64px 0px 56px;
			cursor: pointer;
		}

		.devtools .gauge
		{
			flex-grow: 1;
			flex-shrink: 1;
			flex-basis: 0px;
			min-width: 0px;
			flex-direction: column;
			padding: 8px 12px 10px 12px;
			border-right: 1px solid $line-soft;
			border-top: 2px solid transparent;
			cursor: pointer;
		}

		.devtools .gauge:hover
		{
			background-color: $hover;
		}

		.devtools .gauge.warning { border-top: 2px solid $warn; }
		.devtools .gauge.bad { border-top: 2px solid $bad; }

		.devtools .gauge-key
		{
			color: $dim;
			font-size: 11px;
			white-space: nowrap;
		}

		.devtools .gauge-line
		{
			align-items: flex-end;
			gap: 4px;
			height: 38px;
		}

		.devtools .gauge-value
		{
			font-family: Roboto Mono;
			font-size: 28px;
			font-weight: 600;
			color: $bright;
			white-space: nowrap;
		}

		.devtools .gauge.warning .gauge-value { color: $warn; }
		.devtools .gauge.bad .gauge-value { color: $bad; }

		.devtools .gauge-unit
		{
			color: $dim;
			padding-bottom: 6px;
			white-space: nowrap;
		}

		.devtools .gauge-chart
		{
			flex-grow: 1;
			flex-direction: column;
			justify-content: flex-end;
			gap: 6px;
			margin-top: 8px;
			min-height: 40px;
		}

		.devtools .gauge-chart .sparkline
		{
			height: 40px;
		}

		.devtools .gauge-chart .histogram
		{
			height: 56px;
			background-color: transparent;
		}

		.devtools .stack-bar
		{
			flex-shrink: 0;
			height: 8px;
		}

		// Glance: a row of gauges - a name, a number, a chart - over the budget ruler. The words are in tooltips.
		.devtools .page.dock-glance
		{
			grid-template-columns: minmax(0, 1fr);
		}

		.devtools .glance-gauges
		{
			flex-direction: row;
			flex-shrink: 0;
			height: 92px;
			min-width: 0px;
			border-bottom: 1px solid $line;
		}

		.devtools .glance-gauges .gauge
		{
			padding: 6px 12px 8px 12px;
		}

		.devtools .glance-gauges .gauge-line
		{
			height: 34px;
		}

		.devtools .glance-gauges .gauge-value
		{
			font-size: 26px;
		}

		.devtools .glance-gauges .gauge-unit
		{
			padding-bottom: 5px;
			font-size: 11px;
		}

		.devtools .glance-gauges .gauge-chart
		{
			margin-top: 4px;
			min-height: 22px;
			gap: 3px;
		}

		.devtools .glance-gauges .gauge-chart .sparkline,
		.devtools .glance-gauges .gauge-chart .histogram
		{
			height: 22px;
		}

		.devtools .gpu-zoom
		{
			flex-shrink: 0;
			margin: 4px 0px 6px 0px;
			border-top: 1px solid $line;
			cursor: pointer;
		}

		.devtools .glance-ruler
		{
			flex-direction: column;
			flex-shrink: 0;
			padding-top: 6px;
		}

		.devtools .page-scroll
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
			flex-direction: column;
			overflow-y: scroll;
			overflow-x: hidden;
		}

		// A tab that scrolls itself - the console, a list - takes the page's height instead
		.devtools .page-scroll.fill
		{
			overflow-y: hidden;
		}

		.devtools .page.fills
		{
			display: flex;
			flex-direction: column;
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
		}

		//
		// Pages: sections stacked edge to edge, split into columns where there's room
		//

		// Sections flow into columns rather than down: the dev tools are wide and short
		.devtools .page
		{
			display: grid;
			grid-template-columns: repeat(auto-fill, minmax(440px, 1fr));
			grid-auto-rows: auto;
			flex-shrink: 0;
			min-width: 0px;
		}

		.devtools .split
		{
			display: grid;
			grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
			grid-auto-rows: auto;
			min-width: 0px;
			margin-left: -1px;
		}

		.devtools .split > .section
		{
			border-left: 1px solid $line;
		}

		.devtools .span-all
		{
			grid-column: 1 / -1;
		}

		.devtools .render-submission
		{
			grid-column: 1 / -1;
		}

		.devtools.wide .render-submission
		{
			grid-column: span 2;
		}

		.devtools .good { color: $good; }
		.devtools .info { color: $dim; }
		.devtools .warning { color: $warn; }
		.devtools .bad { color: $bad; }
		.devtools .dim { color: $dim; }
		.devtools .faint { color: $faint; }

		.devtools .quiet
		{
			color: $faint;
			padding: 6px 10px;
		}

		//
		// Section: a header band, then its content
		//

		.devtools .section
		{
			flex-direction: column;
			min-width: 0px;
			border-bottom: 1px solid $line;
		}

		.devtools .section-head
		{
			flex-shrink: 0;
			align-items: center;
			gap: 8px;
			height: 24px;
			padding: 0px 10px;
			background-color: $band;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .section-title
		{
			flex-shrink: 0;
			color: $text;
			font-weight: 600;
		}

		.devtools .section-aside
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-width: 0px;
			justify-content: flex-end;
			text-align: right;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
			color: $dim;
			font-family: Roboto Mono;
			font-size: 11px;
		}

		.devtools .section-link
		{
			flex-shrink: 0;
			font-family: Material Icons;
			font-size: 16px;
			color: $faint;
			margin-right: -4px;
		}

		.devtools .section.link > .section-head
		{
			cursor: pointer;
		}

		.devtools .section.link > .section-head:hover
		{
			background-color: $hover;
		}

		.devtools .section.link > .section-head:hover .section-link
		{
			color: $bright;
		}

		.devtools .section-body
		{
			flex-direction: column;
			min-width: 0px;
		}

		.devtools .section-body.padded
		{
			padding: 6px 10px;
		}

		.devtools .section.collapsed > .section-body
		{
			display: none;
		}

		.devtools .section.collapsible > .section-head
		{
			cursor: pointer;
		}

		.devtools .section.collapsible > .section-head:hover
		{
			background-color: $hover;
		}

		.devtools .section-chevron
		{
			font-family: Material Icons;
			font-size: 16px;
			color: $dim;
			margin-left: -4px;
		}

		//
		// Facts: label and value pairs in a grid of cells
		//

		.devtools .facts
		{
			display: grid;
			grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
			grid-auto-rows: 24px;
			min-width: 0px;
			border-bottom: 1px solid $line;
		}

		.devtools .fact
		{
			align-items: center;
			gap: 8px;
			min-width: 0px;
			padding: 0px 10px;
			border-right: 1px solid $line-soft;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .fact-key
		{
			flex-shrink: 0;
			color: $dim;
		}

		.devtools .fact-value
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-width: 0px;
			justify-content: flex-end;
			text-align: right;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
			font-family: Roboto Mono;
			color: $bright;
		}

		.devtools .fact-value.warning { color: $warn; }
		.devtools .fact-value.bad { color: $bad; }

		//
		// Tables: grid rows sharing one column definition, numbers right aligned in mono
		//

		.devtools .table
		{
			flex-direction: column;
			flex-shrink: 0;
			min-width: 0px;
		}

		.devtools .data-header, .devtools .data-row
		{
			display: grid;
			align-items: center;
			gap: 10px;
			min-width: 0px;
			padding: 0px 10px;
		}

		.devtools .data-header
		{
			height: 20px;
			color: $faint;
			font-size: 11px;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .table.banded > .data-header
		{
			height: 24px;
			background-color: $band;
			color: $dim;
			font-size: 11px;
		}

		.devtools .table.banded > .data-header.link
		{
			cursor: pointer;
		}

		.devtools .table.banded > .data-header.link:hover
		{
			background-color: $hover;
		}

		.devtools .table.banded > .data-header > .cell.first
		{
			color: $text;
			font-size: 12px;
			font-weight: 600;
		}

		.devtools .data-row
		{
			height: 22px;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .data-row.last
		{
			border-bottom: 0px solid transparent;
		}

		.devtools .data-row.link
		{
			cursor: pointer;
		}

		.devtools .data-row:hover
		{
			background-color: $hover;
		}

		.devtools .data-row.selected
		{
			background-color: $line;
		}

		.devtools .data-row.group
		{
			height: 20px;
			color: $faint;
			font-size: 11px;
			font-weight: 600;
		}

		.devtools .data-row.group > .cell
		{
			grid-column: 1 / -1;
		}

		.devtools .data-row.total
		{
			border-top: 1px solid $line;
		}

		.devtools .cell
		{
			min-width: 0px;
			align-items: center;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.devtools .cell-text
		{
			min-width: 0px;
			flex-shrink: 1;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.devtools .cell.number
		{
			justify-content: flex-end;
			text-align: right;
			font-family: Roboto Mono;
			color: $bright;
		}

		.devtools .cell.note
		{
			color: $dim;
		}

		.devtools .cell.number.dim { color: $dim; }
		.devtools .cell.number.warning { color: $warn; }
		.devtools .cell.number.bad { color: $bad; }

		.devtools .cell.swatched
		{
			gap: 7px;
		}

		.devtools .swatch
		{
			flex-shrink: 0;
			width: 3px;
			height: 12px;
		}

		.devtools .cell-bar
		{
			flex-grow: 1;
			height: 6px;
			background-color: $line-soft;
		}

		.devtools .cell-bar-fill
		{
			height: 100%;
			background-color: #6e6e6e;
		}

		.devtools .cell.warning .cell-bar-fill { background-color: $warn; }
		.devtools .cell.bad .cell-bar-fill { background-color: $bad; }

		//
		// Alerts: one line each - what, the number, where to look
		//

		.devtools .findings
		{
			flex-direction: column;
			flex-shrink: 0;
			border-bottom: 1px solid $line;
		}

		.devtools .finding
		{
			display: grid;
			grid-template-columns: 3px 150px minmax(0, 1fr) auto;
			align-items: center;
			gap: 0px 10px;
			height: 24px;
			padding-right: 10px;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .finding-mark
		{
			height: 100%;
			background-color: $warn;
		}

		.devtools .finding.bad .finding-mark { background-color: $bad; }
		.devtools .finding.info .finding-mark { background-color: $faint; }

		.devtools .finding-title
		{
			color: $warn;
			font-weight: 600;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.devtools .finding.bad .finding-title { color: $bad; }
		.devtools .finding.info .finding-title { color: $text; }

		.devtools .finding-detail
		{
			color: $text;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.devtools .finding-link
		{
			color: $faint;
			font-size: 11px;
		}

		.devtools .finding.link
		{
			cursor: pointer;
		}

		.devtools .finding.link:hover
		{
			background-color: $hover;
		}

		.devtools .finding.link:hover .finding-link
		{
			color: $bright;
		}

		.devtools .findings-more
		{
			height: 22px;
			align-items: center;
			padding: 0px 13px;
			color: $dim;
			cursor: pointer;
		}

		.devtools .findings-more:hover
		{
			color: $bright;
		}

		//
		// The frame chart
		//

		.devtools .chart
		{
			flex-direction: column;
			flex-shrink: 0;
			min-width: 0px;
			background-color: $plot;
		}

		.devtools .chart-plot
		{
			flex-shrink: 0;
			height: 130px;
		}

		.devtools .chart-legend
		{
			flex-shrink: 0;
			overflow: hidden;
			gap: 14px;
			height: 20px;
			align-items: center;
			padding: 0px 10px;
			color: $dim;
			font-size: 11px;
		}

		.devtools .legend-key
		{
			align-items: center;
			gap: 5px;
		}

		.devtools .legend-key .swatch
		{
			width: 8px;
			height: 8px;
		}

		.devtools .chart-legend .grow
		{
			flex-grow: 1;
		}

		.devtools .chart-legend .legend-note
		{
			font-family: Roboto Mono;
			color: $faint;
		}

		.devtools .histogram
		{
			flex-shrink: 0;
			height: 96px;
			background-color: $plot;
		}

		//
		// Hosted debug overlay drawers, and the device report
		//

		.devtools .overlay-view
		{
			flex-shrink: 0;
			overflow: hidden;
			margin: 6px 10px;
		}

		.devtools .device-lines
		{
			flex-direction: column;
			padding: 6px 10px;
			font-family: Roboto Mono;
			font-size: 11px;
			color: $dim;
			gap: 2px;
		}

		.devtools .hint
		{
			color: $faint;
			padding: 4px 10px;
		}

		.devtools.narrow .hide-narrow
		{
			display: none;
		}

		//
		// Network analyzer: a toolbar, filters, then the traffic as rows of one grid, newest first
		//

		.devtools .net-toolbar, .devtools .net-filters
		{
			flex-shrink: 0;
			align-items: center;
			gap: 6px;
			padding: 0px 6px;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .net-toolbar
		{
			height: 34px;
			background-color: $band;
		}

		.devtools .net-filters
		{
			height: 34px;
		}

		.devtools .net-toolbar .grow
		{
			flex-grow: 1;
		}

		.devtools .net-toolbar .icon-button
		{
			height: 26px;
			width: 28px;
		}

		.devtools .net-toolbar .icon-button.record.on
		{
			color: $bad;
		}

		.devtools .segments
		{
			flex-shrink: 0;
			border: 1px solid $line;
		}

		.devtools .segment
		{
			height: 24px;
			padding: 0px 12px;
			align-items: center;
			color: $dim;
			cursor: pointer;
			border-right: 1px solid $line;
		}

		.devtools .segment:hover
		{
			color: $text;
			background-color: $hover;
		}

		.devtools .segment.active
		{
			color: $bright;
			background-color: $line;
		}

		.devtools .net-session
		{
			padding-left: 6px;
			color: $faint;
		}

		.devtools .net-session.live
		{
			color: $good;
		}

		.devtools .net-view
		{
			flex-direction: column;
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
		}

		.devtools .net-view.scrolls
		{
			overflow-y: scroll;
		}

		// A scrolling column's sections keep their height and scroll, rather than squash
		.devtools .net-view.scrolls > *, .devtools .ui-details > *, .devtools .ui-tree-pane > *
		{
			flex-shrink: 0;
		}

		.devtools .net-search, .devtools .net-select
		{
			height: 24px;
			align-items: center;
			background-color: $plot;
			border: 1px solid $line;
			color: $text;
		}

		.devtools .net-search
		{
			flex-grow: 1;
			padding: 0px 8px;
		}

		.devtools .net-search:focus
		{
			border: 1px solid $faint;
		}

		.devtools .net-select
		{
			flex-shrink: 0;
			width: 140px;
			padding: 0px 4px 0px 8px;
			cursor: pointer;
		}

		.devtools .net-select:hover
		{
			background-color: $hover;
		}

		.devtools .net-select-text
		{
			flex-grow: 1;
		}

		.devtools .net-select-icon, .devtools .net-icon
		{
			font-family: Material Icons;
			color: $dim;
		}

		.devtools .net-select-icon
		{
			font-size: 16px;
		}

		.devtools .net-row
		{
			display: grid;
			grid-template-columns: 96px 110px minmax(0, 1fr) minmax(0, 0.8fr) 64px;
			gap: 10px;
			align-items: center;
			height: 22px;
			padding: 0px 10px;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .net-row.net-head
		{
			flex-shrink: 0;
			height: 24px;
			background-color: $band;
			color: $dim;
			font-size: 11px;
		}

		.devtools .net-list
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
			overflow-y: scroll;
		}

		.devtools .net-list .net-row:hover
		{
			background-color: $hover;
		}

		.devtools .net-cell
		{
			min-width: 0px;
			white-space: nowrap;
			overflow: hidden;
		}

		.devtools .net-cell.number
		{
			justify-content: flex-end;
		}

		.devtools .net-cell.mono
		{
			font-family: Roboto Mono;
			font-size: 11px;
		}

		.devtools .net-cell.dim
		{
			color: $dim;
		}

		.devtools .net-event
		{
			align-items: center;
			gap: 6px;
		}

		.devtools .net-icon
		{
			font-size: 14px;
		}

		.devtools .net-icon.sent { color: #c2a46e; }
		.devtools .net-icon.received { color: #78aa9a; }
		.devtools .net-icon.session { color: $faint; }

		.devtools .net-footer
		{
			flex-shrink: 0;
			height: 24px;
			align-items: center;
			padding: 0px 10px;
			background-color: $chrome;
			border-top: 1px solid $line;
			color: $dim;
			font-size: 11px;
		}

		.devtools .net-footer .grow
		{
			flex-grow: 1;
		}

		.devtools .net-footer-rate
		{
			font-family: Roboto Mono;
		}

		//
		// UI inspector: the panel tree beside what's picked - its box, the rules that matched, what it computed to
		//

		.devtools .ui-inspector
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
		}

		.devtools .ui-tree-pane
		{
			flex-direction: column;
			width: 42%;
			flex-shrink: 0;
			overflow-y: scroll;
			border-right: 1px solid $line;
		}

		.devtools .ui-tree-pane .data-row
		{
			height: 20px;
		}

		.devtools .ui-node
		{
			font-family: Roboto Mono;
			font-size: 11px;
			white-space: nowrap;
		}

		.devtools .ui-node.dim
		{
			color: $faint;
		}

		.devtools .ui-details
		{
			flex-direction: column;
			flex-grow: 1;
			flex-shrink: 1;
			min-width: 0px;
			overflow-y: scroll;
		}

		.devtools .ui-details > .quiet
		{
			padding: 12px;
		}

		.devtools .ui-head
		{
			flex-shrink: 0;
			flex-direction: column;
			padding: 8px 10px;
			background-color: $band;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .ui-signature
		{
			font-family: Roboto Mono;
			color: $bright;
		}

		.devtools .ui-source
		{
			color: $dim;
			font-size: 11px;
		}

		.devtools .ui-filter
		{
			width: 140px;
			height: 20px;
			margin-left: 8px;
			padding: 0px 6px;
			align-items: center;
			background-color: $plot;
			border: 1px solid $line;
			color: $text;
			font-size: 11px;
		}

		.devtools .box-model
		{
			padding: 10px;
			justify-content: center;
			font-family: Roboto Mono;
			font-size: 11px;
		}

		.devtools .bm-layer
		{
			flex-direction: column;
			align-items: stretch;
			flex-grow: 1;
			max-width: 420px;
			border: 1px dashed $faint;
			padding: 0px 4px;
		}

		.devtools .bm-margin { background-color: #3a2f22; }
		.devtools .bm-border { background-color: #3b3622; border: 1px solid #8c7d45; }
		.devtools .bm-padding { background-color: #27331f; }

		.devtools .bm-row
		{
			justify-content: space-between;
		}

		.devtools .bm-name
		{
			width: 60px;
			color: $dim;
		}

		.devtools .bm-name-spacer
		{
			width: 60px;
		}

		.devtools .bm-edge
		{
			justify-content: center;
			color: $text;
			padding: 2px 0px;
		}

		.devtools .bm-edge.side
		{
			width: 34px;
			align-items: center;
		}

		.devtools .bm-middle
		{
			align-items: stretch;
		}

		.devtools .bm-inner
		{
			flex-grow: 1;
		}

		.devtools .bm-content
		{
			flex-grow: 1;
			justify-content: center;
			padding: 8px 4px;
			margin: 2px 0px;
			background-color: #263240;
			border: 1px solid #4a5a6a;
			color: $bright;
		}

		.devtools .ui-rule
		{
			flex-direction: column;
			flex-shrink: 0;
			padding: 4px 10px 6px 10px;
			border-bottom: 1px solid $line-soft;
			font-family: Roboto Mono;
			font-size: 11px;
		}

		.devtools .ui-rule-head
		{
			justify-content: space-between;
			gap: 10px;
			padding-bottom: 2px;
		}

		.devtools .ui-selector
		{
			color: $bright;
		}

		.devtools .ui-rule-source
		{
			color: $dim;
			flex-shrink: 0;
		}

		.devtools .ui-decl
		{
			padding-left: 14px;
			gap: 6px;
		}

		.devtools .ui-prop
		{
			color: #c2a46e;
		}

		.devtools .ui-value
		{
			color: $text;
		}

		.devtools .ui-decl.overridden .ui-prop, .devtools .ui-decl.overridden .ui-value
		{
			color: $faint;
			text-decoration: line-through;
		}

		.devtools .table .ui-prop, .devtools .table .ui-value
		{
			font-family: Roboto Mono;
			font-size: 11px;
		}

		//
		// Console
		//

		.devtools .console
		{
			flex-direction: column;
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
			background-color: $plot;
		}

		.devtools .console-bar
		{
			flex-shrink: 0;
			height: 26px;
			align-items: stretch;
			background-color: $band;
			border-bottom: 1px solid $line-soft;
		}

		.devtools .console-filter, .devtools .console-input
		{
			background-color: $bg;
			border: 1px solid $line;
			font-family: Roboto Mono;
			font-size: 12px;
			color: $text;
			align-items: center;
			padding: 0px 6px;
		}

		.devtools .console-filter
		{
			flex-grow: 1;
			margin: 3px 4px;
		}

		.devtools .console-input
		{
			flex-shrink: 0;
			height: 26px;
			margin: 4px 6px 6px 6px;
		}

		.devtools .console-filter:focus, .devtools .console-input:focus
		{
			border: 1px solid $faint;
		}

		.devtools .console-output
		{
			flex-grow: 1;
			overflow-y: scroll;
			padding: 2px 10px;
			font-family: Roboto Mono;
			font-size: 12px;
		}

		.devtools .console-line
		{
			white-space: nowrap;
			height: 17px;
			color: $text;
		}

		.devtools .console-line.trace { color: $faint; }
		.devtools .console-line.warn { color: $warn; }
		.devtools .console-line.error { color: $bad; }

		// Last, and doubled up, so nothing that sets a display beats it. A block counts as the first of its selectors that
		// matches, so a hidden page that fills needs a block of its own to beat .page.fills
		.devtools .hidden, .devtools .hidden.page
		{
			display: none;
		}

		.devtools .page.fills.hidden
		{
			display: none;
		}

		//
		// Tooltips and the console's autocomplete aren't under .devtools: each opens in an OS window of its own
		//

		.tooltip
		{
			background-color: #202020;
			border: 1px solid $line;
			padding: 5px 8px;
			color: $text;
			font-family: Inter;
			font-size: 12px;
		}

		.popup-panel.autocomplete
		{
			flex-direction: column;
			background-color: #202020;
			border: 1px solid $line;
			padding: 2px;
			font-family: Roboto Mono;
			font-size: 12px;
			color: $text;
		}

		.popup-panel.autocomplete .button
		{
			padding: 3px 8px;
			cursor: pointer;
		}

		.popup-panel.autocomplete .button:hover, .popup-panel.autocomplete .button.active
		{
			background-color: #333333;
		}
		""";
}
