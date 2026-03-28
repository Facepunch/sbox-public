using Sandbox.Helpers;

namespace Editor.MeshEditor;

public abstract class PrimitiveEditor
{
	private readonly TypeDescription _type;

	protected PrimitiveTool Tool { get; private init; }

	public string Title => _type.Title;
	public string Icon => _type.Icon;

	public virtual bool CanBuild => false;
	public virtual bool InProgress => false;

	protected PrimitiveEditor( PrimitiveTool tool )
	{
		Tool = tool;
		_type = EditorTypeLibrary.GetType( GetType() );
	}

	public abstract void OnUpdate( SceneTrace trace );
	public abstract void OnCancel();
	public abstract PolygonMesh Build();

	public virtual void OnCreated( MeshComponent component )
	{
	}

	public virtual Widget CreateWidget() => null;

	readonly HashSet<UndoSystem.Entry> _undoActions = [];

	void CleanUndoStack( Stack<UndoSystem.Entry> stack )
	{
		var kept = new Stack<UndoSystem.Entry>();

		while ( stack.Count > 0 )
		{
			var entry = stack.Pop();

			if ( !_undoActions.Remove( entry ) )
				kept.Push( entry );
		}

		while ( kept.Count > 0 )
			stack.Push( kept.Pop() );
	}

	protected void PushUndo( string title, Action undo, Action redo = null )
	{
		_undoActions.Add( SceneEditorSession.Active.UndoSystem.Insert( title, undo, redo ) );
	}

	protected void PopUndo()
	{
		if ( _undoActions.Count == 0 )
			return;

		var undo = SceneEditorSession.Active.UndoSystem;

		CleanUndoStack( undo.Back );
		CleanUndoStack( undo.Forward );

		_undoActions.Clear();
	}
}
