namespace Sandbox.UI;

/// <summary>
/// Default table styling, available without an addon stylesheet - its layout, and a neutral look an addon's
/// stylesheet builds on.
/// </summary>
public partial class Table
{
	const string Styles = """
		table
		{
			flex-direction: column;
			min-height: 0;

			thead, tfoot
			{
				flex-direction: column;
				flex-shrink: 0;

				// Right in by the body's scrollbar too, till there's a row to line up with (Table.AlignEnds)
				> tr
				{
					height: 32px;
					padding-right: 16px;
				}
			}

			thead
			{
				border-bottom: 1px solid rgba( 255, 255, 255, 0.1 );
			}

			tfoot
			{
				border-top: 1px solid rgba( 255, 255, 255, 0.1 );
			}

			// Room kept for the scrollbar whether it's there or not, so it never sits over the rows' ends
			tbody
			{
				flex-grow: 1;
				min-height: 0;
				flex-direction: column;
				overflow-y: scroll;
				scrollbar-width: thin;
				scrollbar-gutter: stable;

				> *
				{
					flex-shrink: 0;
				}

				> tr
				{
					min-height: 36px;

					&:hover
					{
						background-color: rgba( 255, 255, 255, 0.05 );
					}

					&.selected
					{
						background-color: rgba( 255, 255, 255, 0.12 );
					}

					&.disabled
					{
						opacity: 0.55;
					}
				}
			}

			// Its cells its full height, centring their own content - so lines down their sides join up
			tr
			{
				flex-direction: row;
				flex-shrink: 0;
				align-items: stretch;
				padding: 0 8px;
			}

			// 12px between one cell's content and the next's, half each side of the edge between them, so a line drawn
			// on that edge (td + td { border-left }) runs down the middle. Part of the column's width.
			td, th
			{
				align-items: center;
				min-width: 0;
				overflow: hidden;
				white-space: nowrap;
				padding: 0 6px;

				&:first-child
				{
					padding-left: 0;
				}

				&:last-child
				{
					padding-right: 0;
				}

				&.center
				{
					justify-content: center;
					text-align: center;
				}

				&.right
				{
					justify-content: flex-end;
					text-align: right;
				}
			}

			th
			{
				gap: 4px;

				&.sortable
				{
					cursor: pointer;
				}

				// Its handle sits out over the edge after it
				&.resizable
				{
					position: relative;
					overflow: visible;
				}
			}

			// The edge between two headings, to drag along - nothing to see but the cursor
			.resize-handle
			{
				position: absolute;
				top: 0;
				bottom: 0;
				right: -6px;
				width: 12px;
				cursor: col-resize;
			}
		}
		""";
}
