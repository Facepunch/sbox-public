/// <summary>
/// Data passed to <see cref="Global.IUndoEvents"/>. In <see cref="Global.IUndoEvents.OnUndo"/>, set
/// <see cref="Cancelled"/> to true to keep the entry and its objects.
/// </summary>
public class UndoEvent
{
	/// <summary>
	/// The client whose undo history this is.
	/// </summary>
	public Connection Client { get; init; }

	/// <summary>
	/// The action's title, possibly a phrase like "#undo.spawn".
	/// </summary>
	public string Name { get; init; }

	/// <summary>
	/// The objects the undo removes.
	/// </summary>
	public IReadOnlyList<GameObject> Objects { get; init; }

	public bool Cancelled { get; set; }
}

/// <summary>
/// Allows listening to and stopping undos, for example to lock props during a round. Runs on the host.
/// </summary>
public static partial class Global
{
	public interface IUndoEvents : ISceneEvent<IUndoEvents>
	{
		void OnUndo( UndoEvent e ) { }
		void OnUndone( UndoEvent e ) { }
	}
}
