namespace Editor.CodeEditors;

public sealed partial class ScriptEditor
{
	const string Styles = """
		.script-editor {
			width: 100%; height: 100%; flex-direction: column;
			background-color: #1e1e1e; color: #d4d4d4;
			font-family: Segoe UI; font-size: 13px; pointer-events: all;
			.menubar { background-color: #2d2d30; padding: 2px 8px; }
			.toolbar { background-color: #2d2d30; padding: 4px 8px; gap: 4px; }
			.toolbar .button { padding: 5px 8px; gap: 6px; border-radius: 2px; }
			.toolbar .button:hover { background-color: #3e3e42; }
			.toolbar .button > .icon { font-size: 17px; width: 17px; height: 17px; flex-shrink: 0; }
			.toolbar-separator { width: 1px; height: 18px; background-color: #555; margin: 0 5px; }
			.splitcontainer { min-height: 0; }
			.split-left { flex-direction: column; background-color: #252526; overflow: hidden; }
			.split-right { flex-direction: column; overflow: hidden; }
			.splitter { width: 4px; background-color: #333337; }
			.splitter:hover { background-color: #007acc; }
			.solution-explorer { flex-direction: column; }
			.solution-explorer > .toolbar { background-color: #252526; }
			.solution-explorer .textentry { flex-grow: 1; padding: 6px; background-color: #333337; border: 1px solid #555; }
			.treeview { flex-grow: 1; min-height: 0; }
			.tree-row { align-items: center; padding: 0 6px; gap: 5px; }
			.tree-row:hover { background-color: #2a2d2e; }
			.tree-row.selected { background-color: #094771; }
			.tree-icon { color: #86c691; font-size: 16px; }
			.tree-row.folder > .tree-icon { color: #dcb67a; }
			.tree-label { white-space: nowrap; text-overflow: ellipsis; }
			.tabbar { background-color: #252526; min-height: 32px; }
			.tab { padding: 7px 10px; gap: 8px; border-right: 1px solid #1e1e1e; }
			.tab:hover { background-color: #3e3e42; }
			.tab.selected { background-color: #1e1e1e; border-top: 2px solid #007acc; }
			.tab-close:hover { background-color: #555; }
			.editor-message { padding: 8px 10px; background-color: #5a302a; color: white; flex-shrink: 0; }
			.close-overlay { position: absolute; inset: 0; background-color: #0009; align-items: center; justify-content: center; }
			.close-dialog { flex-direction: column; padding: 24px; gap: 20px; background-color: #2d2d30; border: 1px solid #555; }
		}
		""";
}
