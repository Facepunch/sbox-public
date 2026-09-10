using System;

namespace Sandbox.Helpers;

/// <summary>
/// Implemented by <see cref="Widget"/>s that contain an <see cref="IUndoSystem"/>.
/// When an undo / redo shortcut is pressed with the editor window in focus, it'll look for
/// a descendant widget implementing this interface with the most recent timestamp to undo / redo.
/// </summary>
public interface IUndoSystemProvider
{
	/// <summary>
	/// Undo system that this widget is responsible for. Can be null.
	/// </summary>
	IUndoSystem UndoSystem { get; }
}

/// <summary>
/// Interface for an undo/redo edit history system.
/// </summary>
public interface IUndoSystem
{
	/// <summary>
	/// Finds all undo systems exposed by <see cref="IUndoSystemProvider"/>-implementing
	/// descendants of <paramref name="root"/>. Only considers visible, enabled widgets.
	/// </summary>
	private static IEnumerable<IUndoSystem> FindAll( Widget root )
	{
		// Note that DockWidgets are considered descendants of their DockManager,
		// even when floating in a child window.

		foreach ( var widget in root.GetDescendants<Widget>() )
		{
			if ( widget is not IUndoSystemProvider { UndoSystem: { } system } ) continue;
			if ( widget is not { IsValid: true, Visible: true, Enabled: true } ) continue;

			yield return system;
		}
	}

	/// <summary>
	/// Calls <see cref="Undo()"/> on the undo system with the most recent change found through
	/// descendants of <paramref name="root"/>. Returns <see langword="true"/> if a change was undone.
	/// </summary>
	internal static bool Undo( Widget root ) =>
		FindAll( root ).MaxBy( x => x.UndoTimestamp ?? DateTime.MinValue )?.Undo() ?? false;

	/// <summary>
	/// Calls <see cref="Redo()"/> on the undo system with the most recently undone change found through
	/// descendants of <paramref name="root"/>. Returns <see langword="true"/> if a change was redone.
	/// </summary>
	internal static bool Redo( Widget root ) =>
		FindAll( root ).MinBy( x => x.RedoTimestamp ?? DateTime.MaxValue )?.Redo() ?? false;

	/// <summary>
	/// Timestamp of the last change made, or null if there's nothing to undo.
	/// </summary>
	DateTime? UndoTimestamp { get; }

	/// <summary>
	/// Timestamp of the last undone change, or null if there's nothing to redo.
	/// </summary>
	DateTime? RedoTimestamp { get; }

	/// <summary>
	/// Undoes the last change made. Calling <see cref="Redo()"/> should now re-apply that change.
	/// Returns false if there was no change to undo.
	/// </summary>
	bool Undo();

	/// <summary>
	/// Redoes the last undone change, or returns false if there was no undone change to redo.
	/// </summary>
	bool Redo();
}

/// <summary>
/// A system that aims to wrap the main reusable functionality of an <see cref="IUndoSystem"/>.
/// </summary>
public partial class UndoSystem : IUndoSystem
{
	public class Entry
	{
		public string Name { get; set; }
		public Action Undo { get; set; }
		public Action Redo { get; set; }
		/// [Obsolete]?
		public Object Image { get; set; }
		public DateTime Timestamp { get; set; }
		public bool Locked { get; set; }
	}

	/// <summary>
	/// Called when an undo is run
	/// </summary>
	public Action<Entry> OnUndo;

	/// <summary>
	/// Called when a redo is run
	/// </summary>
	public Action<Entry> OnRedo;

	/// <summary>
	/// Backwards stack
	/// </summary>
	public Stack<Entry> Back { get; } = new();

	/// <summary>
	/// Forwards stack, gets cleared when a new undo is added
	/// </summary>
	public Stack<Entry> Forward { get; } = new();

	DateTime? IUndoSystem.UndoTimestamp => Back.TryPeek( out var prev ) ? prev.Timestamp : null;

	DateTime? IUndoSystem.RedoTimestamp => Forward.TryPeek( out var next ) ? next.Timestamp : null;

	/// <summary>
	/// Instigate an undo. Return true if we found a successful undo
	/// </summary>
	public bool Undo()
	{
		if ( !Back.TryPop( out var entry ) )
		{
			next = initial;
			return false;
		}

		next = entry.Undo;
		try
		{
			entry.Undo?.Invoke();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error when undoing '{entry.Name}': {e.Message}" );
		}

		if ( entry.Locked )
		{
			Back.Push( entry );
			return false;
		}

		Forward.Push( entry );
		OnUndo?.Invoke( entry );

		return true;
	}

	/// <summary>
	/// Instigate a redo, returns true if we found a successful undo
	/// </summary>
	public bool Redo()
	{
		if ( !Forward.TryPop( out var entry ) )
			return false;

		next = entry.Redo;
		Back.Push( entry );
		entry.Redo?.Invoke();
		OnRedo?.Invoke( entry );

		return true;
	}

	/// <summary>
	/// Insert a new undo entry
	/// </summary>
	public Entry Insert( string title, Action undo, Action redo = null )
	{
		var e = new Entry
		{
			Name = title,
			Undo = undo,
			Redo = redo,
			Timestamp = DateTime.UtcNow,
		};

		Back.Push( e );

		Forward.Clear();

		return e;
	}

	/// <summary>
	/// Provide a function that returns an action to call on undo/redo.
	/// This generally is a function that saves and restores the entire state
	/// of a project.
	/// </summary>
	[Obsolete( "Auto Snapshotting is obsolete and no longer working. If you really want to use snapshotting for Undo, create/restore the snapshots manually in the undo/redo actions provided to UndoSystem.Insert" )]
	public void SetSnapshotFunction( Func<Action> snapshot )
	{
	}

	/// <code>
	///  func getsnapshot()
	///  {
	///		var state = currentstate();
	///
	///		return () => restorestate( state );
	///  }
	///
	///  startup()
	///  {
	///     -- give a function that creates undo functions
	///     UndoSystem.SetSnapshotter( getsnapshot )
	///
	///     -- store current snapshot in `next`
	///     UndoSystem.Initialize();
	///  }
	///
	///  mainloop()
	///  {
	///     deleteobject();
	///
	///     -- store 'next' snapshot as "object deleted" undo
	///     -- take a new snapshot and store it in next
	///     UndoSystem.Snapshot( "object deleted" );
	///  }
	/// </code>
	Action next;
	Action initial;

	/// <summary>
	/// Should be called after you make a change to your project. The snapshot system
	/// is good for self contained projects that can be serialized and deserialized quickly.
	/// </summary>
	[Obsolete( "Auto Snapshotting is obsolete and no longer working. If you really want to use snapshotting for Undo, create/restore the snapshots manually in the undo/redo actions provided to UndoSystem.Insert" )]
	public void Snapshot( string changeTitle )
	{
	}

	/// <summary>
	/// Clear the history and take an initial snapshot.
	/// You should call this right after a load, or a new project.
	/// </summary>
	public void Initialize()
	{
		Back.Clear();
		Forward.Clear();

		initial = next;
	}
}
