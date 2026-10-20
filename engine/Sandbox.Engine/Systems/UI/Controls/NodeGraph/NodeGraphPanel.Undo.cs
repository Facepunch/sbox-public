namespace Sandbox.UI.NodeGraph;

//
// Undo: user edits call PushUndo before and PushRedo after, which hosts with undo override.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// Called before a user edit changes the graph; <see cref="PushRedo"/> follows once it's done.
	/// Hosts with undo override both.
	/// </summary>
	public virtual void PushUndo( string name ) { }

	/// <summary>
	/// Called after the edit announced by <see cref="PushUndo"/>.
	/// </summary>
	public virtual void PushRedo() { }

	int _undoDepth;

	/// <summary>
	/// <see cref="PushUndo"/> now and <see cref="PushRedo"/> on dispose. Inside another scope it
	/// does neither, so one user edit is one undo step - hosts wrap their own multi-step edits in it too.
	/// </summary>
	public IDisposable UndoScope( string name )
	{
		if ( _undoDepth++ == 0 ) PushUndo( name );
		return new Sandbox.Utility.DisposeAction( EndUndoScope );
	}

	void EndUndoScope()
	{
		if ( --_undoDepth == 0 ) PushRedo();
	}
}
