using Editor.AnimGraph.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace Editor.AnimGraph.Test;

[TestClass]
public class StateMachineTests
{
	[TestMethod]
	public void RenamingThroughAnotherStateNamePreservesTransitions()
	{
		var hook = new StateMachineNode.State { Name = "Left Hook" };
		var block = new StateMachineNode.State { Name = "Block" };
		var machine = new StateMachineNode { States = [hook, block], DefaultState = hook.Name };
		var conduit = new StateMachineNode.Conduit { From = hook.Name, To = block.Name };
		machine.Conduits.Add( conduit );
		machine.SyncStates();

		var hookGlobal = machine.GlobalTransitions.Single( g => g.StateName == hook.Name );
		var blockGlobal = machine.GlobalTransitions.Single( g => g.StateName == block.Name );

		// The inspector writes the draft before notifying the state machine on each edit.
		block.Name = "Left Hook";
		machine.RenameState( "Block", block );
		Assert.AreEqual( "Left Hook 2", block.Name );

		var previousName = block.Name;
		block.Name = "Left Hook Block";
		machine.RenameState( previousName, block );
		machine.SyncStates();

		Assert.AreEqual( hook.Name, conduit.From );
		Assert.AreEqual( block.Name, conduit.To );
		Assert.AreEqual( hook.Name, machine.DefaultState );
		Assert.AreSame( hookGlobal, machine.GlobalTransitions.Single( g => g.StateName == hook.Name ) );
		Assert.AreSame( blockGlobal, machine.GlobalTransitions.Single( g => g.StateName == block.Name ) );
	}
}
