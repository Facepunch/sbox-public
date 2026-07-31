using Sandbox.Helpers;

namespace Editor.MeshEditor;

sealed class ToolUndoStack
{
	readonly HashSet<UndoSystem.Entry> _entries = [];

	public void Push( string title, Action undo, Action redo = null )
	{
		_entries.Add( SceneEditorSession.Active.UndoSystem.Insert( title, undo, redo ) );
	}

	public void Clear()
	{
		if ( _entries.Count == 0 )
			return;

		var undo = SceneEditorSession.Active.UndoSystem;
		Clear( undo.Back );
		Clear( undo.Forward );
		_entries.Clear();
	}

	void Clear( Stack<UndoSystem.Entry> stack )
	{
		var kept = new Stack<UndoSystem.Entry>();

		while ( stack.TryPop( out var entry ) )
		{
			if ( !_entries.Remove( entry ) )
				kept.Push( entry );
		}

		while ( kept.TryPop( out var entry ) )
			stack.Push( entry );
	}
}
