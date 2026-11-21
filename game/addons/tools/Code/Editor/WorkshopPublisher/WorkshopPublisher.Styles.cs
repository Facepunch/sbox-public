namespace Editor;

/// <summary>
/// The inspector layout and menus for the Workshop publishing window.
/// </summary>
public sealed partial class WorkshopPublisher
{
	const string Styles = """
		.workshop-publisher {
			width: 100%; height: 100%; flex-direction: column;
			background-color: #2b2b2b; color: #eee; font-family: Segoe UI; font-size: 13px;
			pointer-events: all;
			.body { padding: 24px; gap: 20px; flex-direction: column; flex-grow: 1; min-height: 0; overflow-y: auto; }
			.field-row { gap: 18px; align-items: center; flex-shrink: 0; }
			.field-label { width: 108px; flex-shrink: 0; color: #aaa; font-size: 12px; }
			.target-fields { flex-direction: column; flex-grow: 1; min-width: 0; gap: 7px; }
			.target-row { border-bottom: 1px solid #414141; padding-bottom: 20px; }
			.item-details { color: #aaa; font-size: 11px; }
			.identity { gap: 20px; flex-shrink: 0; }
			.identity-fields { flex-direction: column; flex-grow: 1; gap: 20px; justify-content: center; min-width: 0; }
			.identity-fields .field-label { width: 58px; }
			.button { padding: 9px 14px; gap: 7px; border-radius: 4px; align-items: center; justify-content: center; background-color: #353535; border: 1px solid #484848; }
			.button:hover { background-color: #414141; }
			.button .icon { font-size: 17px; }
			.button.disabled { opacity: 0.45; pointer-events: none; }
			.textentry, .dropdown { flex-grow: 1; min-width: 0; padding: 9px 10px; background-color: #1b1b1b; border: 1px solid #414141; border-radius: 4px; }
			.textentry:focus, .dropdown:focus { border-color: #3479ed; }
			.textentry.disabled { opacity: 0.6; }
			.dropdown { justify-content: flex-start; padding-right: 32px; gap: 9px; }
			.dropdown:hover { background-color: #242424; border-color: #606060; }
			.dropdown.open { background-color: #242424; border-color: #3479ed; }
			.dropdown .button-right-column { overflow: hidden; flex-direction: row; align-items: center; justify-content: flex-start; text-align: left; }
			.dropdown .button-label { white-space: nowrap; text-overflow: ellipsis; }
			.dropdown .dropdown_indicator { font-size: 16px; }
			.item-picker > .icon { width: 24px; height: 24px; flex-shrink: 0; border-radius: 3px; background-size: cover; background-position: center; align-items: center; justify-content: center; }
			.icon-picker { width: 108px; height: 108px; flex-shrink: 0; flex-direction: column; padding: 8px; background-color: #1b1b1b; background-size: cover; background-position: center; }
			.icon-picker .button-label { font-size: 11px; }
			.icon-picker.has-preview { justify-content: flex-end; }
			.icon-picker.has-preview > .icon { display: none; }
			.icon-picker.has-preview .button-right-column { background-color: #171717dd; padding: 4px; border-radius: 3px; }
			.addon-types-row { align-items: flex-start; }
			.addon-types-row .field-label { padding-top: 8px; }
			.addon-type-choices { flex-grow: 1; flex-wrap: wrap; gap: 6px; }
			.addon-type-choices .button { padding: 6px 9px; }
			.addon-type-choices .button.selected { background-color: #253e60; border-color: #3479ed; color: #90baff; }
			.notes-row { align-items: flex-start; flex-grow: 1; min-height: 110px; }
			.notes-row .field-label { padding-top: 10px; }
			.notes-row .textentry { height: 100%; min-height: 110px; }
			.message { color: #efba82; flex-shrink: 0; white-space: normal; }
			.progress { height: 3px; background-color: #414141; flex-shrink: 0; }
			.progress-fill { height: 100%; background-color: #3479ed; }
			.footer { padding: 16px 24px; gap: 9px; align-items: center; background-color: #242424; border-top: 1px solid #414141; flex-shrink: 0; }
			.status { flex-grow: 1; color: #aaa; font-size: 12px; }
			.primary { background-color: #3479ed; border-color: #3479ed; color: white; }
			.primary:hover { background-color: #4388fc; }
		}
		.os-menu .popup-panel {
			flex-direction: column;
			padding: 6px;
			gap: 3px;
			background-color: #242424;
			color: #eee;
			border: 1px solid #505050;
			border-radius: 5px;
			font-family: Segoe UI;
			font-size: 13px;
			font-weight: normal;
			max-height: 320px;
			overflow-y: auto;

			> .button {
				min-height: 36px;
				padding: 9px 10px;
				gap: 10px;
				align-items: center;
				justify-content: flex-start;
				border-radius: 3px;
				flex-shrink: 0;
				color: #ddd;
				background-color: transparent;
				border: 0;
				cursor: pointer;
				font-family: Segoe UI;
				font-weight: normal;
			}

			> .button:hover, > .button.active { background-color: #393939; color: white; }
			> .button.selected { background-color: #253e60; color: white; }
			> .button.selected:hover, > .button.selected.active { background-color: #2e4c73; }
			> .button.selected::after { content: "check"; font-family: Material Symbols Outlined; font-size: 16px; color: #90baff; }
			> .button .icon { font-size: 18px; width: 20px; flex-shrink: 0; color: #aaa; }
			> .button.selected .icon { color: #90baff; }
			> .button .button-right-column { flex-direction: row; flex-grow: 1; min-width: 0; text-align: left; }
			> .button .button-label { white-space: nowrap; text-overflow: ellipsis; }
			&.workshop-item-menu > .button > .icon { width: 32px; height: 32px; border-radius: 3px; background-size: cover; background-position: center; align-items: center; justify-content: center; }

			&.workshop-item-menu > .button:first-child {
				color: #90baff;
				margin-bottom: 4px;
				padding-bottom: 13px;
				border-bottom: 1px solid #414141;
			}
		}
		""";
}
