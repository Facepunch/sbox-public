namespace Sandbox.UI;

public partial class ScriptControl
{
	const string Styles = """
		.script-control
		{
			position: relative;
			display: flex;
			flex-direction: column;
			flex-grow: 1;
			min-width: 0;
			min-height: 0;
			font-family: "Cascadia Mono";
			font-size: 13px;
			pointer-events: all;

			.editor-body
			{
				flex-grow: 1;
				min-height: 0;
				min-width: 0;
			}

			.line-gutter
			{
				width: 62px;
				flex-shrink: 0;
				overflow: hidden;
			}

			.script-entry
			{
				flex-grow: 1;
				min-width: 0;
				min-height: 0;
				padding: 12px 16px 80px 0;
				border: 0;
				border-radius: 0;
				background-color: transparent;
				align-items: flex-start;
				justify-content: flex-start;
				overflow: scroll;
				scrollbar-width: thin;
				cursor: text;

				.content-label
				{
					flex-grow: 1;
					flex-shrink: 0;
					min-width: 0;
					white-space: pre;
					line-height: 1.125;
				}
			}

			.editor-status
			{
				height: 27px;
				padding: 5px 14px;
				flex-shrink: 0;
				min-width: 0;
				align-items: center;
				gap: 16px;
				font-family: "Segoe UI";
				font-size: 12px;
				white-space: nowrap;
				overflow: hidden;
				.status-message { flex-grow: 1; flex-shrink: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
				.status-position { flex-shrink: 0; white-space: nowrap; }
			}
		}

		.script-control-popup
		{
			pointer-events: all;
			&.intellisense
			{
				position: absolute;
				z-index: 100;
				flex-shrink: 0;
				border-width: 1px;
				border-style: solid;
				border-radius: 3px;
				font-family: "Segoe UI";
				font-size: 13px;
			}

			&.completion-popup
			{
				flex-direction: column;
				max-height: 330px;
			}

			.completion-list
			{
				min-width: 140px;
				max-width: 320px;
				flex-shrink: 0;
				flex-direction: column;
				padding: 4px;
			}

			.completion-row
			{
				height: 25px;
				flex-shrink: 0;
				align-items: center;
				padding: 0 5px;
				border-radius: 2px;
				cursor: pointer;
				white-space: nowrap;
			}

			.symbol-icon
			{
				width: 23px;
				flex-shrink: 0;
				font-size: 13px;
			}

			.completion-name
			{
				min-width: 0;
				text-overflow: ellipsis;
				overflow: hidden;
			}

			.completion-footer
			{
				border-top-width: 1px;
				border-style: solid;
				padding: 7px 3px 3px;
				margin-top: 4px;
				font-size: 11px;
			}

			&.completion-detail
			{
				flex-direction: column;
				min-width: 240px;
				padding: 8px 10px;
				pointer-events: none;
				.info-description { max-width: 380px; }
				.info-title { max-width: 380px; }
			}

			.info-title
			{
				font-family: "Cascadia Mono";
				white-space: pre-wrap;
				flex-shrink: 0;
			}

			.info-description
			{
				margin-top: 8px;
				white-space: pre-wrap;
			}

			&.hover-popup
			{
				max-width: 580px;
				padding: 12px;
				flex-direction: column;
				pointer-events: none;
			}

			&.signature-popup
			{
				max-width: 760px;
				padding: 9px 12px;
				flex-direction: column;
				pointer-events: none;
			}

			.signature-counter
			{
				font-size: 11px;
				margin-bottom: 5px;
			}

			.signature-text
			{
				font-family: "Cascadia Mono";
				white-space: pre-wrap;
			}
		}
		""";
}
