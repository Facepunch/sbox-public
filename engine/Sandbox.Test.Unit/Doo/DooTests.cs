using System.Collections.Generic;

namespace Sandbox.Tests;

[TestClass]
public class DooTests
{
	// ──────────────────────────────────────────────
	// Edit: DeleteBlock
	// ──────────────────────────────────────────────

	[TestMethod]
	public void DeleteBlock_TopLevel_RemovesBlock()
	{
		var block = new Doo.ReturnBlock();
		var doo = new Doo { Body = [new Doo.ReturnBlock(), block, new Doo.ReturnBlock()] };

		Assert.IsTrue( doo.DeleteBlock( block ) );
		Assert.AreEqual( 2, doo.Body.Count );
		Assert.IsFalse( doo.Body.Contains( block ) );
	}

	[TestMethod]
	public void DeleteBlock_Nested_RemovesBlock()
	{
		var nested = new Doo.ReturnBlock();
		var parent = new Doo.ForBlock { Body = [nested] };
		var doo = new Doo { Body = [parent] };

		Assert.IsTrue( doo.DeleteBlock( nested ) );
		Assert.AreEqual( 0, parent.Body.Count );
	}

	[TestMethod]
	public void DeleteBlock_NotFound_ReturnsFalse()
	{
		var doo = new Doo { Body = [new Doo.ReturnBlock()] };
		Assert.IsFalse( doo.DeleteBlock( new Doo.ReturnBlock() ) );
	}

	[TestMethod]
	public void DeleteBlock_NullBody_ReturnsFalse()
	{
		var doo = new Doo { Body = null };
		Assert.IsFalse( doo.DeleteBlock( new Doo.ReturnBlock() ) );
	}

	// ──────────────────────────────────────────────
	// Edit: InsertBefore / InsertAfter
	// ──────────────────────────────────────────────

	[TestMethod]
	public void InsertBefore_PlacesBlockBeforeTarget()
	{
		var target = new Doo.ReturnBlock();
		var toInsert = new Doo.ReturnBlock();
		var doo = new Doo { Body = [target] };

		Assert.IsTrue( doo.InsertBefore( target, toInsert ) );
		Assert.AreEqual( 2, doo.Body.Count );
		Assert.AreSame( toInsert, doo.Body[0] );
		Assert.AreSame( target, doo.Body[1] );
	}

	[TestMethod]
	public void InsertAfter_PlacesBlockAfterTarget()
	{
		var target = new Doo.ReturnBlock();
		var toInsert = new Doo.ReturnBlock();
		var doo = new Doo { Body = [target] };

		Assert.IsTrue( doo.InsertAfter( target, toInsert ) );
		Assert.AreEqual( 2, doo.Body.Count );
		Assert.AreSame( target, doo.Body[0] );
		Assert.AreSame( toInsert, doo.Body[1] );
	}

	[TestMethod]
	public void InsertBefore_Nested_PlacesCorrectly()
	{
		var nested = new Doo.ReturnBlock();
		var parent = new Doo.ForBlock { Body = [nested] };
		var toInsert = new Doo.ReturnBlock();
		var doo = new Doo { Body = [parent] };

		Assert.IsTrue( doo.InsertBefore( nested, toInsert ) );
		Assert.AreEqual( 2, parent.Body.Count );
		Assert.AreSame( toInsert, parent.Body[0] );
		Assert.AreSame( nested, parent.Body[1] );
	}

	[TestMethod]
	public void InsertAfter_RemovesFromOldLocationFirst()
	{
		var block = new Doo.ReturnBlock();
		var target = new Doo.ReturnBlock();
		var doo = new Doo { Body = [block, target] };

		Assert.IsTrue( doo.InsertAfter( target, block ) );
		Assert.AreEqual( 2, doo.Body.Count );
		Assert.AreSame( target, doo.Body[0] );
		Assert.AreSame( block, doo.Body[1] );
	}

	// ──────────────────────────────────────────────
	// Edit: AddChild
	// ──────────────────────────────────────────────

	[TestMethod]
	public void AddChild_AddsToParentBody()
	{
		var parent = new Doo.ForBlock { Body = [] };
		var child = new Doo.ReturnBlock();
		var doo = new Doo { Body = [parent] };

		doo.AddChild( parent, child );

		Assert.AreEqual( 1, parent.Body.Count );
		Assert.AreSame( child, parent.Body[0] );
	}

	[TestMethod]
	public void AddChild_CreatesBodyIfNull()
	{
		var parent = new Doo.ForBlock { Body = null };
		var child = new Doo.ReturnBlock();
		var doo = new Doo { Body = [parent] };

		doo.AddChild( parent, child );

		Assert.IsNotNull( parent.Body );
		Assert.AreEqual( 1, parent.Body.Count );
	}

	[TestMethod]
	public void AddChild_RemovesFromOldLocation()
	{
		var child = new Doo.ReturnBlock();
		var parent = new Doo.ForBlock { Body = [] };
		var doo = new Doo { Body = [child, parent] };

		doo.AddChild( parent, child );

		Assert.AreEqual( 1, doo.Body.Count );
		Assert.AreSame( parent, doo.Body[0] );
		Assert.AreEqual( 1, parent.Body.Count );
		Assert.AreSame( child, parent.Body[0] );
	}

	// ──────────────────────────────────────────────
	// CollectArguments: recursive tree traversal
	// ──────────────────────────────────────────────

	[TestMethod]
	public void CollectArguments_RecursesIntoNestedBlocks()
	{
		var inner = new Doo.SetBlock
		{
			VariableName = "innerVar",
			Value = new Doo.VariableExpression { VariableName = "refVar" }
		};

		var outer = new Doo.ForBlock
		{
			VariableName = "i",
			StartValue = new Doo.VariableExpression { VariableName = "start" },
			EndValue = new Doo.VariableExpression { VariableName = "end" },
			JumpValue = new Doo.VariableExpression { VariableName = "step" },
			Body = [inner]
		};

		var args = new HashSet<string>();
		outer.CollectArguments( args );

		Assert.IsTrue( args.Contains( "i" ) );
		Assert.IsTrue( args.Contains( "start" ) );
		Assert.IsTrue( args.Contains( "end" ) );
		Assert.IsTrue( args.Contains( "step" ) );
		Assert.IsTrue( args.Contains( "innerVar" ) );
		Assert.IsTrue( args.Contains( "refVar" ) );
	}

	[TestMethod]
	public void CollectArguments_InvokeBlock_IncludesArgumentsAndReturn()
	{
		var block = new Doo.InvokeBlock
		{
			Arguments =
			[
				new Doo.VariableExpression { VariableName = "arg1" },
				new Doo.VariableExpression { VariableName = "arg2" }
			],
			ReturnVariable = "result"
		};

		var args = new HashSet<string>();
		block.CollectArguments( args );

		Assert.IsTrue( args.Contains( "arg1" ) );
		Assert.IsTrue( args.Contains( "arg2" ) );
		Assert.IsTrue( args.Contains( "result" ) );
	}

	[TestMethod]
	public void CollectArguments_SetBlock_IncludesVariableAndValueExpression()
	{
		var block = new Doo.SetBlock
		{
			VariableName = "target",
			Value = new Doo.VariableExpression { VariableName = "source" }
		};

		var args = new HashSet<string>();
		block.CollectArguments( args );

		Assert.IsTrue( args.Contains( "target" ) );
		Assert.IsTrue( args.Contains( "source" ) );
	}
}
