namespace Sandbox.UI;

public partial class DockHost
{
	const string Styles = """
		.dockhost
		{
			position: relative;
			flex-grow: 1;
			min-width: 0;
			min-height: 0;
			overflow: hidden;
			pointer-events: all;
			color: #c6ccd6;
			background-color: #101319;
			font-family: Inter;
			font-size: 12px;

			.dock-parking { display: none; }
			.dock-workspace, .dock-group, .dock-split, .dock-branch, .dock-body, .dock-content
			{
				flex-grow: 1;
				min-width: 0;
				min-height: 0;
				overflow: hidden;
			}
			.dock-workspace { width: 100%; height: 100%; }
			.dock-group { flex-direction: column; background-color: #20242c; }
			.dock-body { position: relative; }
			.dock-content { width: 100%; height: 100%; }
			.dock-content > * { flex-grow: 1; min-width: 0; min-height: 0; }
			.dock-tabs
			{
				flex-shrink: 0;
				height: 34px;
				padding: 4px 4px 0 4px;
				gap: 3px;
				background-color: #101319;
				overflow-x: scroll;
				overflow-y: hidden;
			}
			.dock-tab
			{
				flex-shrink: 0;
				align-items: center;
				padding: 0 6px 0 10px;
				gap: 7px;
				border: 1px solid transparent;
				border-top: 2px solid transparent;
				border-radius: 5px 5px 0 0;
				cursor: pointer;
				color: #929caa;
				background-color: #20242c;
				&:hover { background-color: #252b35; color: #e2e8f0; }
				&.selected
				{
					border-color: #343d4b;
					border-top-color: transparent;
					border-bottom-color: #20242c;
					background-color: #20242c;
					color: #f0f3f8;
				}
				&:focus { border-top-color: transparent; }
				> .dock-tab-action { opacity: 0.35; }
				&.selected > .dock-tab-action { opacity: 0.7; }
				&:hover > .dock-tab-action, &:focus > .dock-tab-action { opacity: 1; }
				&.dragging { opacity: 0.45; }
			}
			.dock-tab-title { white-space: nowrap; pointer-events: none; }
			.dock-tab-icon { width: 16px; height: 16px; font-size: 16px; flex-shrink: 0; pointer-events: none; }
			.dock-tab-action
			{
				width: 20px;
				height: 20px;
				flex-shrink: 0;
				align-items: center;
				justify-content: center;
				border-radius: 4px;
				&:hover { background-color: #ffffff18; color: white; }
				> * { pointer-events: none; font-size: 14px; }
			}
			.dock-branch { flex-basis: 0px; }
			.dock-splitter
			{
				width: 5px;
				flex-shrink: 0;
				background-color: #101319;
				cursor: ew-resize;
				&:hover { background-color: #4389e8; }
			}
			.dock-split.vertical
			{
				flex-direction: column;
				> .dock-splitter { height: 5px; width: 100%; cursor: ns-resize; }
			}
			.dock-split.resizing > .dock-splitter { background-color: #4389e8; }
			.dock-preview
			{
				position: absolute;
				z-index: 2;
				pointer-events: none;
				background-color: #499cff55;
				border: 2px solid #96d3ff;
			}
			.dock-targets { position: absolute; left: 0; top: 0; width: 100%; height: 100%; z-index: 100; }
			.dock-targets, .dock-targets * { pointer-events: none; }
			.dock-section
			{
				position: absolute;
				z-index: 0;
				background-color: #2875d82b;
				border: 1px solid #469aee99;
				&.hovered { background-color: #2875d842; border-color: #79bfff; }
			}
			.dock-guide
			{
				position: absolute;
				z-index: 3;
				padding: 5px;
				border: 1px solid #88bded;
				border-radius: 3px;
				background-color: #123e69;
				box-shadow: 0 2px 8px #0009;
				&.hovered { background-color: #2388df; border-color: white; }
			}
			.dock-guide-frame { position: relative; width: 100%; height: 100%; border: 1px solid #a0cefa; }
			.dock-guide-fill
			{
				position: absolute;
				background-color: #9bd5ff;
				&.left { left: 0; top: 0; width: 50%; height: 100%; }
				&.right { right: 0; top: 0; width: 50%; height: 100%; }
				&.top { left: 0; top: 0; width: 100%; height: 50%; }
				&.bottom { left: 0; bottom: 0; width: 100%; height: 50%; }
				&.center { display: none; }
			}
			.dock-guide-icon { position: absolute; left: 0; top: 0; width: 100%; height: 100%; font-size: 16px; color: #d4edff; }
			.dock-empty { margin: auto; color: #8993a3; pointer-events: none; }
			&.floating
			{
				flex-direction: column;
				border: 1px solid #39424f;
				border-radius: 6px;
				> .dock-workspace { height: auto; }
				> .dock-window-title
				{
					height: 30px;
					flex-shrink: 0;
					padding: 0 10px;
					align-items: center;
					background-color: #1b2028;
					cursor: default;
				}
			}
		}
		.style-light .dockhost
		{
			color: #303a48;
			background-color: #bdc7d6;
			.dock-group, .dock-tab.selected { background-color: #f4f6fa; color: #253249; }
			.dock-tabs { background-color: #bdc7d6; }
			.dock-tab { color: #586578; background-color: #f4f6fa; }
			.dock-tab:hover { background-color: #eaf0f8; }
			.dock-tab.selected
			{
				border-color: #c2cddd;
				border-top-color: transparent;
				border-bottom-color: #f4f6fa;
				background-color: #f4f6fa;
			}
			.dock-tab:focus { border-top-color: transparent; }
			.dock-tab-action:hover { background-color: #233c6014; color: #182c48; }
			.dock-splitter { background-color: #bdc7d6; }
			&.floating
			{
				border-color: #aab8ca;
				> .dock-window-title { background-color: #e6ecf4; }
			}
		}
		""";
}
