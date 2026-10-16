using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The hub's look. Self contained, because the game has none of the editor's stylesheets to lean on.
/// <para>
/// A tool pane, not a page: neutral greys, flat sections under header bands, numbers in tables. Colour is kept for
/// status - a value that's out of line goes amber or red - and for telling series apart in a chart.
/// </para>
/// </summary>
internal static class HubStyles
{
	internal static readonly Color Good = Color.Parse( "#7CC46A" ).Value;
	internal static readonly Color Warning = Color.Parse( "#E8B04A" ).Value;
	internal static readonly Color Bad = Color.Parse( "#E45A4C" ).Value;
	internal static readonly Color Text = Color.Parse( "#D9D9D9" ).Value;
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
	internal static readonly Color HubWork = Color.Parse( "#686868" ).Value;

	/// <summary>
	/// For series that need telling apart - GPU pass families, timeline scopes. Muted, and kept off the status colours.
	/// </summary>
	internal static readonly Color[] Series =
	[
		Color.Parse( "#C2A46E" ).Value, Color.Parse( "#8FAE6B" ).Value, Color.Parse( "#C47F63" ).Value, Color.Parse( "#A98BB3" ).Value,
		Color.Parse( "#78AA9A" ).Value, Color.Parse( "#C9B45A" ).Value, Color.Parse( "#9C9C9C" ).Value, Color.Parse( "#B88F8F" ).Value,
	];

	internal static Color For( PerfHub.Severity severity ) => severity switch
	{
		PerfHub.Severity.Good => Good,
		PerfHub.Severity.Info => Dim,
		PerfHub.Severity.Warning => Warning,
		_ => Bad,
	};

	internal static string ClassFor( PerfHub.Severity severity ) => severity switch
	{
		PerfHub.Severity.Good => "good",
		PerfHub.Severity.Info => "info",
		PerfHub.Severity.Warning => "warning",
		_ => "bad",
	};

	static StyleSheet _sheet;
	internal static StyleSheet Sheet => _sheet ??= StyleSheet.FromString( Styles, "perfhub" );

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

		.perfhub-root
		{
			font-family: Inter;
			font-size: 12px;
			color: $text;
			pointer-events: none;
		}

		//
		// Shell: one strip with the tabs and window buttons, the readout line, then the page and the console
		//

		// The whole of a window of its own. Opaque: translucent, faint copies of earlier frames showed through it - not
		// from the swap chain, which is cleared every frame; the cause isn't pinned down
		.perfhub
		{
			width: 100%;
			height: 100%;
			flex-direction: column;
			background-color: $bg;
			pointer-events: all;
			overflow: hidden;
		}

		.perfhub .strip
		{
			flex-shrink: 0;
			height: 30px;
			align-items: stretch;
			background-color: $chrome;
			border-bottom: 1px solid $line;
		}

		.perfhub .strip .title
		{
			align-items: center;
			padding: 0px 10px;
			color: $dim;
			font-weight: 600;
		}

		.perfhub .tabs
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-width: 0px;
			overflow: hidden;
		}

		.perfhub .tab
		{
			flex-shrink: 0;
			padding: 0px 11px;
			align-items: center;
			color: $dim;
			cursor: pointer;
			border-right: 1px solid $line-soft;
		}

		.perfhub .tab:hover
		{
			color: $text;
			background-color: $hover;
		}

		.perfhub .tab.active
		{
			color: $bright;
			background-color: $bg;
		}

		.perfhub .strip .grow
		{
			flex-grow: 1;
		}

		.perfhub .host-controls
		{
			flex-shrink: 0;
			align-items: stretch;
		}

		.perfhub .host-controls .divider
		{
			width: 1px;
			margin: 7px 3px;
			background-color: $line;
		}

		.perfhub .icon-button
		{
			width: 32px;
			justify-content: center;
			align-items: center;
			color: $dim;
			font-family: Material Icons;
			font-size: 16px;
			cursor: pointer;
		}

		.perfhub .icon-button:hover
		{
			background-color: $hover;
			color: $bright;
		}

		.perfhub .icon-button.active
		{
			color: $bright;
			background-color: $hover;
		}

		.perfhub .icon-button.close:hover
		{
			background-color: #c42b1c;
			color: white;
		}

		// The readout: the numbers every tab needs, always in view
		.perfhub .readout
		{
			flex-shrink: 0;
			height: 26px;
			align-items: stretch;
			border-bottom: 1px solid $line;
			font-family: Roboto Mono;
			font-size: 12px;
		}

		.perfhub .readout-item
		{
			flex-shrink: 0;
			align-items: center;
			gap: 6px;
			padding: 0px 10px;
			border-right: 1px solid $line-soft;
		}

		.perfhub .readout-item.grow
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-width: 0px;
			overflow: hidden;
			border-right: 0px solid transparent;
		}

		.perfhub .readout-key
		{
			color: $faint;
			font-family: Inter;
			font-size: 11px;
		}

		.perfhub .readout-value
		{
			color: $bright;
			white-space: nowrap;
		}

		.perfhub .readout.paused
		{
			background-color: rgba( $warn, 0.08 );
		}

		.perfhub .readout .paused-flag
		{
			color: $warn;
			font-family: Inter;
			font-weight: 600;
		}

		.perfhub .readout .icon-button
		{
			width: 30px;
			border-left: 1px solid $line-soft;
		}

		.perfhub .workspace
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
			flex-direction: column;
		}

		.perfhub .page-scroll
		{
			flex-grow: 1;
			flex-shrink: 1;
			min-height: 0px;
			flex-direction: column;
			overflow-y: scroll;
			overflow-x: hidden;
		}

		.perfhub .console-splitter
		{
			flex-shrink: 0;
			height: 4px;
			background-color: $line;
			cursor: row-resize;
		}

		.perfhub .console-splitter:hover
		{
			background-color: $faint;
		}

		//
		// Pages: sections stacked edge to edge, split into columns where there's room
		//

		.perfhub .page
		{
			display: grid;
			grid-template-columns: minmax(0, 1fr);
			grid-auto-rows: auto;
			flex-shrink: 0;
			min-width: 0px;
		}

		.perfhub .split
		{
			display: grid;
			grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
			grid-auto-rows: auto;
			min-width: 0px;
			margin-left: -1px;
		}

		.perfhub .split > .section
		{
			border-left: 1px solid $line;
		}

		.perfhub .span-all
		{
			grid-column: 1 / -1;
		}

		.perfhub .render-submission
		{
			grid-column: 1 / -1;
		}

		.perfhub.wide .render-submission
		{
			grid-column: span 2;
		}

		.perfhub .good { color: $good; }
		.perfhub .info { color: $dim; }
		.perfhub .warning { color: $warn; }
		.perfhub .bad { color: $bad; }
		.perfhub .dim { color: $dim; }
		.perfhub .faint { color: $faint; }

		.perfhub .quiet
		{
			color: $faint;
			padding: 6px 10px;
		}

		//
		// Section: a header band, then its content
		//

		.perfhub .section
		{
			flex-direction: column;
			min-width: 0px;
			border-bottom: 1px solid $line;
		}

		.perfhub .section-head
		{
			flex-shrink: 0;
			align-items: center;
			gap: 8px;
			height: 24px;
			padding: 0px 10px;
			background-color: $band;
			border-bottom: 1px solid $line-soft;
		}

		.perfhub .section-title
		{
			flex-shrink: 0;
			color: $text;
			font-weight: 600;
		}

		.perfhub .section-aside
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

		.perfhub .section-link
		{
			flex-shrink: 0;
			font-family: Material Icons;
			font-size: 16px;
			color: $faint;
			margin-right: -4px;
		}

		.perfhub .section.link > .section-head
		{
			cursor: pointer;
		}

		.perfhub .section.link > .section-head:hover
		{
			background-color: $hover;
		}

		.perfhub .section.link > .section-head:hover .section-link
		{
			color: $bright;
		}

		.perfhub .section-body
		{
			flex-direction: column;
			min-width: 0px;
		}

		.perfhub .section-body.padded
		{
			padding: 6px 10px;
		}

		.perfhub .section.collapsed > .section-body
		{
			display: none;
		}

		.perfhub .section.collapsible > .section-head
		{
			cursor: pointer;
		}

		.perfhub .section.collapsible > .section-head:hover
		{
			background-color: $hover;
		}

		.perfhub .section-chevron
		{
			font-family: Material Icons;
			font-size: 16px;
			color: $dim;
			margin-left: -4px;
		}

		//
		// Facts: label and value pairs in a grid of cells
		//

		.perfhub .facts
		{
			display: grid;
			grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
			grid-auto-rows: 24px;
			min-width: 0px;
			border-bottom: 1px solid $line;
		}

		.perfhub .fact
		{
			align-items: center;
			gap: 8px;
			min-width: 0px;
			padding: 0px 10px;
			border-right: 1px solid $line-soft;
			border-bottom: 1px solid $line-soft;
		}

		.perfhub .fact-key
		{
			flex-shrink: 0;
			color: $dim;
		}

		.perfhub .fact-value
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

		.perfhub .fact-value.warning { color: $warn; }
		.perfhub .fact-value.bad { color: $bad; }

		//
		// Tables: grid rows sharing one column definition, numbers right aligned in mono
		//

		.perfhub .table
		{
			flex-direction: column;
			flex-shrink: 0;
			min-width: 0px;
		}

		.perfhub .data-header, .perfhub .data-row
		{
			display: grid;
			align-items: center;
			gap: 10px;
			min-width: 0px;
			padding: 0px 10px;
		}

		.perfhub .data-header
		{
			height: 20px;
			color: $faint;
			font-size: 11px;
			border-bottom: 1px solid $line-soft;
		}

		.perfhub .table.banded > .data-header
		{
			height: 24px;
			background-color: $band;
			color: $dim;
			font-size: 11px;
		}

		.perfhub .table.banded > .data-header.link
		{
			cursor: pointer;
		}

		.perfhub .table.banded > .data-header.link:hover
		{
			background-color: $hover;
		}

		.perfhub .table.banded > .data-header > .cell.first
		{
			color: $text;
			font-size: 12px;
			font-weight: 600;
		}

		.perfhub .data-row
		{
			height: 22px;
			border-bottom: 1px solid $line-soft;
		}

		.perfhub .data-row.last
		{
			border-bottom: 0px solid transparent;
		}

		.perfhub .data-row.link
		{
			cursor: pointer;
		}

		.perfhub .data-row:hover
		{
			background-color: $hover;
		}

		.perfhub .data-row.group
		{
			height: 20px;
			color: $faint;
			font-size: 11px;
			font-weight: 600;
		}

		.perfhub .data-row.group > .cell
		{
			grid-column: 1 / -1;
		}

		.perfhub .data-row.total
		{
			border-top: 1px solid $line;
		}

		.perfhub .cell
		{
			min-width: 0px;
			align-items: center;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.perfhub .cell-text
		{
			min-width: 0px;
			flex-shrink: 1;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.perfhub .cell.number
		{
			justify-content: flex-end;
			text-align: right;
			font-family: Roboto Mono;
			color: $bright;
		}

		.perfhub .cell.note
		{
			color: $dim;
		}

		.perfhub .cell.number.dim { color: $dim; }
		.perfhub .cell.number.warning { color: $warn; }
		.perfhub .cell.number.bad { color: $bad; }

		.perfhub .cell.swatched
		{
			gap: 7px;
		}

		.perfhub .swatch
		{
			flex-shrink: 0;
			width: 3px;
			height: 12px;
		}

		.perfhub .cell-bar
		{
			flex-grow: 1;
			height: 6px;
			background-color: $line-soft;
		}

		.perfhub .cell-bar-fill
		{
			height: 100%;
			background-color: #6e6e6e;
		}

		.perfhub .cell.warning .cell-bar-fill { background-color: $warn; }
		.perfhub .cell.bad .cell-bar-fill { background-color: $bad; }

		//
		// Alerts: one line each - what, the number, where to look
		//

		.perfhub .findings
		{
			flex-direction: column;
			flex-shrink: 0;
			border-bottom: 1px solid $line;
		}

		.perfhub .finding
		{
			display: grid;
			grid-template-columns: 3px 150px minmax(0, 1fr) auto;
			align-items: center;
			gap: 0px 10px;
			height: 24px;
			padding-right: 10px;
			border-bottom: 1px solid $line-soft;
		}

		.perfhub .finding-mark
		{
			height: 100%;
			background-color: $warn;
		}

		.perfhub .finding.bad .finding-mark { background-color: $bad; }
		.perfhub .finding.info .finding-mark { background-color: $faint; }

		.perfhub .finding-title
		{
			color: $warn;
			font-weight: 600;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.perfhub .finding.bad .finding-title { color: $bad; }
		.perfhub .finding.info .finding-title { color: $text; }

		.perfhub .finding-detail
		{
			color: $text;
			white-space: nowrap;
			overflow: hidden;
			text-overflow: ellipsis;
		}

		.perfhub .finding-link
		{
			color: $faint;
			font-size: 11px;
		}

		.perfhub .finding.link
		{
			cursor: pointer;
		}

		.perfhub .finding.link:hover
		{
			background-color: $hover;
		}

		.perfhub .finding.link:hover .finding-link
		{
			color: $bright;
		}

		.perfhub .findings-more
		{
			height: 22px;
			align-items: center;
			padding: 0px 13px;
			color: $dim;
			cursor: pointer;
		}

		.perfhub .findings-more:hover
		{
			color: $bright;
		}

		//
		// The frame chart
		//

		.perfhub .chart
		{
			flex-direction: column;
			flex-shrink: 0;
			min-width: 0px;
			background-color: $plot;
		}

		.perfhub .chart-plot
		{
			flex-shrink: 0;
			height: 130px;
		}

		.perfhub .chart-legend
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

		.perfhub .legend-key
		{
			align-items: center;
			gap: 5px;
		}

		.perfhub .legend-key .swatch
		{
			width: 8px;
			height: 8px;
		}

		.perfhub .chart-legend .grow
		{
			flex-grow: 1;
		}

		.perfhub .chart-legend .legend-note
		{
			font-family: Roboto Mono;
			color: $faint;
		}

		.perfhub .histogram
		{
			flex-shrink: 0;
			height: 96px;
			background-color: $plot;
		}

		//
		// Hosted debug overlay drawers, and the device report
		//

		.perfhub .overlay-view
		{
			flex-shrink: 0;
			overflow: hidden;
			margin: 6px 10px;
		}

		.perfhub .device-lines
		{
			flex-direction: column;
			padding: 6px 10px;
			font-family: Roboto Mono;
			font-size: 11px;
			color: $dim;
			gap: 2px;
		}

		.perfhub .hint
		{
			color: $faint;
			padding: 4px 10px;
		}

		.perfhub.narrow .hide-narrow
		{
			display: none;
		}

		//
		// Console
		//

		.perfhub .console
		{
			flex-direction: column;
			flex-shrink: 0;
			min-height: 120px;
			background-color: $plot;
		}

		.perfhub .console-bar
		{
			flex-shrink: 0;
			height: 26px;
			align-items: stretch;
			background-color: $band;
			border-bottom: 1px solid $line-soft;
		}

		.perfhub .console-bar .console-title
		{
			flex-grow: 1;
			align-items: center;
			padding: 0px 10px;
			font-weight: 600;
		}

		.perfhub .console-filter, .perfhub .console-input
		{
			background-color: $bg;
			border: 1px solid $line;
			font-family: Roboto Mono;
			font-size: 12px;
			color: $text;
			align-items: center;
			padding: 0px 6px;
		}

		.perfhub .console-filter
		{
			width: 180px;
			margin: 3px 4px;
		}

		.perfhub .console-input
		{
			flex-shrink: 0;
			height: 26px;
			margin: 4px 6px 6px 6px;
		}

		.perfhub .console-filter:focus, .perfhub .console-input:focus
		{
			border: 1px solid $faint;
		}

		.perfhub .console-output
		{
			flex-grow: 1;
			overflow-y: scroll;
			padding: 2px 10px;
			font-family: Roboto Mono;
			font-size: 12px;
		}

		.perfhub .console-line
		{
			white-space: nowrap;
			height: 17px;
			color: $text;
		}

		.perfhub .console-line.trace { color: $faint; }
		.perfhub .console-line.warn { color: $warn; }
		.perfhub .console-line.error { color: $bad; }

		// Last, and doubled up, so nothing that sets a display beats it
		.perfhub .hidden, .perfhub .hidden.page
		{
			display: none;
		}

		//
		// Tooltips and the console's autocomplete aren't under .perfhub: each opens in an OS window of its own
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
