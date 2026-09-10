using Sandbox.Helpers;
using System;
using System.Collections.Generic;

namespace EditorTests;

[TestClass]
public class UndoSystemTest
{
	/// <summary>
	/// Undo and redo on a brand new system should fail gracefully.
	/// </summary>
	[TestMethod]
	public void EmptySystemReturnsFalse()
	{
		var undo = new UndoSystem();

		Assert.IsFalse( undo.Undo() );
		Assert.IsFalse( undo.Redo() );
		Assert.AreEqual( 0, undo.Back.Count );
		Assert.AreEqual( 0, undo.Forward.Count );
	}

	/// <summary>
	/// Inserting an entry should push it onto the back stack and fill in the metadata.
	/// </summary>
	[TestMethod]
	public void InsertPushesOntoBackStack()
	{
		var undo = new UndoSystem();

		var entry = undo.Insert( "My Change", () => { } );

		Assert.IsNotNull( entry );
		Assert.AreEqual( "My Change", entry.Name );
		Assert.AreEqual( 1, undo.Back.Count );
		Assert.AreEqual( 0, undo.Forward.Count );
		Assert.AreSame( entry, undo.Back.Peek() );
	}

	/// <summary>
	/// Undo should invoke the undo action, move the entry to the forward stack and
	/// raise <see cref="UndoSystem.OnUndo"/>.
	/// </summary>
	[TestMethod]
	public void UndoInvokesActionAndMovesEntryForward()
	{
		var undo = new UndoSystem();
		var value = 1;

		UndoSystem.Entry undoneEntry = null;
		undo.OnUndo = e => undoneEntry = e;

		var entry = undo.Insert( "Set Value", () => value = 0, () => value = 1 );

		Assert.IsTrue( undo.Undo() );
		Assert.AreEqual( 0, value );
		Assert.AreEqual( 0, undo.Back.Count );
		Assert.AreEqual( 1, undo.Forward.Count );
		Assert.AreSame( entry, undoneEntry );
	}

	/// <summary>
	/// Redo should invoke the redo action, move the entry back onto the back stack and
	/// raise <see cref="UndoSystem.OnRedo"/>.
	/// </summary>
	[TestMethod]
	public void RedoInvokesActionAndMovesEntryBack()
	{
		var undo = new UndoSystem();
		var value = 1;

		UndoSystem.Entry redoneEntry = null;
		undo.OnRedo = e => redoneEntry = e;

		var entry = undo.Insert( "Set Value", () => value = 0, () => value = 1 );

		Assert.IsTrue( undo.Undo() );
		Assert.IsTrue( undo.Redo() );

		Assert.AreEqual( 1, value );
		Assert.AreEqual( 1, undo.Back.Count );
		Assert.AreEqual( 0, undo.Forward.Count );
		Assert.AreSame( entry, redoneEntry );
	}

	/// <summary>
	/// Entries should undo in reverse insertion order and redo in insertion order.
	/// </summary>
	[TestMethod]
	public void UndoRedoRoundTripRunsInOrder()
	{
		var undo = new UndoSystem();
		var order = new List<string>();

		undo.Insert( "First", () => order.Add( "undo first" ), () => order.Add( "redo first" ) );
		undo.Insert( "Second", () => order.Add( "undo second" ), () => order.Add( "redo second" ) );

		Assert.IsTrue( undo.Undo() );
		Assert.IsTrue( undo.Undo() );
		Assert.IsFalse( undo.Undo() );

		Assert.IsTrue( undo.Redo() );
		Assert.IsTrue( undo.Redo() );
		Assert.IsFalse( undo.Redo() );

		CollectionAssert.AreEqual( new[] { "undo second", "undo first", "redo first", "redo second" }, order );
	}

	/// <summary>
	/// Inserting a new entry should wipe the forward stack, so you can't redo into a
	/// state that no longer makes sense.
	/// </summary>
	[TestMethod]
	public void InsertClearsForwardStack()
	{
		var undo = new UndoSystem();

		undo.Insert( "First", () => { } );
		Assert.IsTrue( undo.Undo() );
		Assert.AreEqual( 1, undo.Forward.Count );

		undo.Insert( "Second", () => { } );

		Assert.AreEqual( 0, undo.Forward.Count );
		Assert.IsFalse( undo.Redo() );
	}

	/// <summary>
	/// An undo action that throws shouldn't propagate - the entry still moves to the
	/// forward stack and the undo counts as handled.
	/// </summary>
	[TestMethod]
	public void UndoSwallowsExceptionFromAction()
	{
		var undo = new UndoSystem();

		undo.Insert( "Broken", () => throw new InvalidOperationException( "boom" ) );

		Assert.IsTrue( undo.Undo() );
		Assert.AreEqual( 0, undo.Back.Count );
		Assert.AreEqual( 1, undo.Forward.Count );
	}

	/// <summary>
	/// A locked entry acts as a barrier - undoing it reports failure, doesn't raise
	/// <see cref="UndoSystem.OnUndo"/> and the entry stays on the back stack. Note the
	/// undo action itself still runs each attempt.
	/// </summary>
	[TestMethod]
	public void LockedEntryStaysOnBackStack()
	{
		var undo = new UndoSystem();
		var invoked = 0;
		var undoEvents = 0;

		undo.OnUndo = _ => undoEvents++;

		var entry = undo.Insert( "Barrier", () => invoked++ );
		entry.Locked = true;

		Assert.IsFalse( undo.Undo() );
		Assert.IsFalse( undo.Undo() );

		Assert.AreEqual( 2, invoked );
		Assert.AreEqual( 0, undoEvents );
		Assert.AreEqual( 1, undo.Back.Count );
		Assert.AreEqual( 0, undo.Forward.Count );
	}

	/// <summary>
	/// Entries with null actions should still flow between the stacks without throwing.
	/// </summary>
	[TestMethod]
	public void NullActionsAreTolerated()
	{
		var undo = new UndoSystem();

		undo.Insert( "No Actions", null, null );

		Assert.IsTrue( undo.Undo() );
		Assert.IsTrue( undo.Redo() );
	}

	/// <summary>
	/// <see cref="UndoSystem.Initialize"/> should clear all history.
	/// </summary>
	[TestMethod]
	public void InitializeClearsHistory()
	{
		var undo = new UndoSystem();

		undo.Insert( "First", () => { } );
		undo.Insert( "Second", () => { } );
		Assert.IsTrue( undo.Undo() );

		undo.Initialize();

		Assert.AreEqual( 0, undo.Back.Count );
		Assert.AreEqual( 0, undo.Forward.Count );
		Assert.IsFalse( undo.Undo() );
		Assert.IsFalse( undo.Redo() );
	}
}
