namespace Sandbox.UI;

public partial class CurveEditor
{
	sealed class CurveEditorToolbar : Toolbar
	{
		readonly CurveEditor _editor;
		readonly Button _undoButton, _redoButton, _deleteButton, _snapButton, _inspectorButton;
		public CurveEditorToolbar( CurveEditor editor )
		{
			_editor = editor;
			_undoButton = AddButton( "", "undo", editor.Undo );
			_undoButton.Tooltip = "Undo (Ctrl+Z)";
			_redoButton = AddButton( "", "redo", editor.Redo );
			_redoButton.Tooltip = "Redo (Ctrl+Y)";
			_deleteButton = AddButton( "", "delete", editor.RemoveSelected );
			_deleteButton.Tooltip = "Delete selected keys";
			AddSeparator();
			_snapButton = AddToggle( "Snap", "grid_on", false, value => editor.SnapToGrid = value );
			_snapButton.Tooltip = "Snap to grid (Ctrl while dragging)";
			AddSpacer();
			AddButton( "Fit", "fit_screen", editor.FitView ).Tooltip = "Fit all curves (Home)";
			AddButton( "", "center_focus_strong", editor.FitSelection ).Tooltip = "Frame selection (F)";
			_inspectorButton = AddToggle( "Advanced", "tune", false, value => editor.ShowInspector = value );
			_inspectorButton.Tooltip = "Show advanced key editing";

		}
		public void Sync()
		{
			_undoButton.Disabled = !_editor.CanUndo;
			_redoButton.Disabled = !_editor.CanRedo;
			_deleteButton.Disabled = !_editor._selection.Any( x => _editor._curves[x.CurveIndex].Length > 1 );
			_inspectorButton.Active = _editor.ShowInspector;
			_snapButton.Active = _editor.SnapToGrid;
			_snapButton.Tooltip = $"Snap: time {_editor.SnapIncrement.x:g}, value {_editor.SnapIncrement.y:g} (Ctrl while dragging)";
		}
	}
}
