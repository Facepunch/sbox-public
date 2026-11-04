namespace Sandbox.UI;

/// <summary>
/// Default window styling, available without an addon stylesheet.
/// </summary>
public partial class FloatingWindow
{
	const string Styles = """
		.floating-window
		{
			flex-direction: column;
			flex-shrink: 0;
			overflow: hidden;
			border: 1px solid rgba( white, 0.2 );
			background-color: #222;
			position: absolute;
			left: 0;
			top: 0;
			width: 384px;
			z-index: 500;
			pointer-events: all;
			color: white;

			.floating-window-titlebar
			{
				cursor: move;
				flex-shrink: 0;
			}

			.floating-window-nodrag
			{
				cursor: pointer;
			}

			.floating-window-header
			{
				align-items: center;
				height: 32px;
			}

			.floating-window-icon
			{
				flex-shrink: 0;
				font-size: 18px;
				margin-left: 8px;
			}

			.floating-window-title
			{
				flex-grow: 1;
				font-size: 12px;
				font-weight: 700;
				margin-left: 4px;
			}

			.floating-window-control
			{
				flex-shrink: 0;
				width: 32px;
				height: 100%;
				padding: 0;
				align-items: center;
				justify-content: center;
				background-color: transparent;
				opacity: 0.5;

				.icon
				{
					font-size: 18px;
				}

				&:hover
				{
					opacity: 1;
				}
			}

			.floating-window-content
			{
				flex-direction: column;
				flex-grow: 1;
				min-height: 0;
				overflow-y: scroll;
			}

			.floating-window-restore
			{
				display: none;
				width: 100%;
				height: 100%;
				padding: 0 0 8px 0;
				align-items: center;
				justify-content: center;
				background-color: transparent;

				.button-text
				{
					font-size: 26px;
				}

				&:hover
				{
					background-color: rgba( white, 0.1 );
				}
			}

			&.minimized
			{
				.floating-window-header, .floating-window-content
				{
					display: none;
				}

				.floating-window-restore
				{
					display: flex;
				}
			}

			&.animating, &.animating *
			{
				pointer-events: none;
			}
		}
		""";
}
