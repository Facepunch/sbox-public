namespace Editor;

internal sealed partial class SceneCompileWindow
{
	const string Styles = """
		.scene-compile
		{
			width: 100%; height: 100%; flex-direction: column; pointer-events: all;
			background-color: #25272b; color: #ebedf1; font-family: Segoe UI; font-size: 14px;

			.footer
			{
				padding: 14px 20px; align-items: center; gap: 16px; flex-shrink: 0;
				background-color: #2d3035;

				.status-right
				{
					gap: 8px;
				}
			}
			.tabbar
			{
				padding: 6px 12px 0; background-color: #2d3035; border-bottom: 1px solid #45484f;
				.tab
				{
					padding: 8px 14px; color: #b9c0ca; cursor: pointer;
					&.selected
					{
						color: white; border-bottom: 2px solid #5295de;
					}
				}
			}
			.scene-name
			{
				font-size: 20px; font-weight: 600; margin-bottom: 6px;
			}
			.sidebar, .steps, .log
			{
				flex-direction: column;
			}
			.sidebar
			{
				width: 295px; flex-shrink: 0; padding: 16px; gap: 8px; overflow-y: scroll;
				border-right: 1px solid #41444a;
			}
			.heading
			{
				color: #b9c0ca; font-weight: 600; font-size: 13px; margin-bottom: 4px;
			}
			.advanced
			{
				flex-direction: column; align-items: flex-start; padding: 18px; gap: 8px; overflow-y: scroll;
				.description
				{
					color: #b9c0ca; font-size: 13px; margin-bottom: 10px; flex-shrink: 0;
				}
			}
			.stage
			{
				align-items: center; gap: 4px;
				.checkbox
				{
					flex-grow: 1; min-width: 0;
				}
				.button
				{
					width: 28px; height: 28px; padding: 0; flex-shrink: 0;
				}
			}
			.steps
			{
				gap: 4px; flex-shrink: 0;
				&.locked
				{
					pointer-events: none; opacity: 0.65;
				}
			}
			.checkbox
			{
				cursor: pointer; align-items: center; gap: 8px; padding: 5px 0;
				&.unavailable
				{
					pointer-events: none; opacity: 0.65;
				}
				.checkmark
				{
					width: 17px; height: 17px; font-size: 15px; color: transparent;
					border: 1px solid #747b86; border-radius: 3px; align-items: center; justify-content: center;
				}
				&.checked .checkmark
				{
					background-color: #3675bd; border-color: #5295de; color: white;
				}
			}
			.field
			{
				align-items: center; justify-content: space-between; gap: 8px;
				.textentry
				{
					width: 72px; padding: 4px 7px; background-color: #191b1e;
					border: 1px solid #4c515b; border-radius: 3px;
				}
			}
			.form
			{
				flex-direction: column; width: 100%; max-width: 520px; gap: 4px; flex-shrink: 0;
				.button
				{
					align-self: flex-start;
				}
			}
			.diagnostics .treeview
			{
				flex-grow: 1; scrollbar-width: thin; scrollbar-color: #747b86 transparent;
			}
			.log
			{
				flex-grow: 1; min-width: 0; min-height: 0; padding: 10px; overflow-y: scroll;
				background-color: #191b1e;
				scrollbar-width: thin;
				scrollbar-color: #747b86 transparent;
				scrollbar-gutter: stable;
				.line
				{
					font-family: Consolas; font-size: 12px; flex-shrink: 0; white-space: pre-wrap; color: #c8d1dc;
				}
			}
			.progress
			{
				height: 4px; background-color: #393c42;
				.progress-fill
				{
					background-color: #5295de;
				}
			}
			.status-message
			{
				color: #c4cbd5;
			}
			.button
			{
				align-items: center; justify-content: center; gap: 6px; padding: 7px 12px;
				border-radius: 4px; background-color: #41454c; cursor: pointer;
				&:hover
				{
					background-color: #51565f;
				}
				&.primary
				{
					min-width: 100px; background-color: #3675bd;
				}
				&.disabled
				{
					opacity: 0.45; pointer-events: none;
				}
			}
		}
		""";
}
