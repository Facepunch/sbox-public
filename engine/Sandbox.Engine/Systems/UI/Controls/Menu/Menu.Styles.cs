namespace Sandbox.UI;

public partial class Menu
{
	/// <summary>
	/// The default look - a dark Windows-style menu. Restyle by class: menu, menulist.
	/// </summary>
	internal const string Styles = """
		.menulist
		{
			position: absolute;
			z-index: 2000;
			flex-direction: column;
			min-width: 180px;
			padding: 4px;
			background-color: #1f2229;
			border: 1px solid #3a3e48;
			border-radius: 6px;
			box-shadow: 0px 6px 20px rgba( 0, 0, 0, 0.5 );
			font-size: 12px;
			color: #e0e3ea;
			pointer-events: all;
			overflow-y: scroll;

			// Styled under its heading, but not dressed like it
			font-weight: normal;
			text-transform: none;
			letter-spacing: 0px;
		}

		// In a popup window of its own the OS draws the window's edge
		.os-popup .menulist
		{
			border: none;
			box-shadow: none;
		}

		.menu
		{
			flex-direction: row;
			align-items: center;
			flex-shrink: 0;
			height: 26px;
			padding: 0px 8px 0px 4px;
			border-radius: 4px;
			white-space: nowrap;
			cursor: pointer;
			pointer-events: all;
			transition: background-color 0.08s ease-out;
		}

		.menu > .gutter
		{
			width: 24px;
			flex-shrink: 0;
			align-items: center;
			justify-content: center;
		}

		.menu > .gutter > .check { display: none; font-size: 15px; }
		.menu > .gutter > .icon { display: none; font-size: 15px; opacity: 0.8; }
		.menu.checked > .gutter > .check { display: flex; }
		.menu.has-icon > .gutter > .icon { display: flex; }
		.menu.checked > .gutter > .icon { display: none; }

		.menu > .text { flex-grow: 1; }
		.menu > .shortcut { display: none; margin-left: 32px; opacity: 0.45; }
		.menu.has-shortcut > .shortcut { display: flex; }
		.menu > .chevron { display: none; margin-left: 12px; font-size: 15px; opacity: 0.6; }
		.menu.has-submenu > .chevron { display: flex; }

		// One highlight - hovering sets active, the same as the keyboard does
		.menu.active, .menu.open
		{
			background-color: rgba( 255, 255, 255, 0.1 );
		}

		// Pressing
		.menu:active { background-color: rgba( 255, 255, 255, 0.18 ); }

		.menu.disabled { opacity: 0.4; cursor: default; }
		.menu.disabled.active, .menu.disabled:active { background-color: transparent; }

		.menu.separator
		{
			height: 1px;
			margin: 4px 6px;
			padding: 0px;
			background-color: rgba( 255, 255, 255, 0.12 );
			cursor: default;
		}

		.menu.separator > * { display: none; }
		.menu.separator.active, .menu.separator:active { background-color: rgba( 255, 255, 255, 0.12 ); }

		// Rows of a submenu wait inside its row until it opens
		.menu > .menu-row { display: none; }
		""";
}
