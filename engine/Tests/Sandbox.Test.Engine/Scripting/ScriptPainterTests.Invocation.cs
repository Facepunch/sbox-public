using System;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Rendering;

namespace ScriptingTests;

public partial class ScriptPainterTests
{
	[TestMethod]
	public void FluentInvocationDoesNotAllocateAfterWarmup()
	{
		var script = Game.Scripting.CreateScript( "return amount;" );
		for ( var i = 0; i < 100; i++ )
			script.With( "amount", 1 ).With( "amount", 2 ).RunAndReturn<int>();

		var before = GC.GetAllocatedBytesForCurrentThread();
		var total = 0;
		for ( var i = 0; i < 100; i++ )
			total += script.With( "amount", 1 ).With( "amount", 2 ).RunAndReturn<int>();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( 200, total );
		Assert.AreEqual( 0L, allocated );
	}

	[TestMethod]
	public void FluentInvocationReportsInvalidSourceAndNames()
	{
		var invalid = Game.Scripting.CreateScript( "return (;" );
		Assert.IsFalse( invalid.With( "amount", 1 ).Compile() );
		Assert.IsNotNull( invalid.LastError );
		var script = Game.Scripting.CreateScript( "return amount;" );
		Assert.ThrowsException<ArgumentNullException>( () => { script.With( null, 1 ); } );
		Assert.ThrowsException<ArgumentException>( () => { script.With( "amount", 1 ).With( "missing", 2 ); } );
	}

	[TestMethod]
	public void FluentInvocationCombinesPaintersAndOrdinaryArguments()
	{
		var first = new Painter.Context( new CommandList() );
		var second = new Painter.Context( new CommandList() );
		using var writer = first.Begin( default );
		using var reader = second.Begin( default );
		reader.Opacity = 0.25f;
		var script = Game.Scripting.CreateScript( "writer.Opacity = reader.Opacity * amount; return writer.Opacity;" );
		var invocation = script.With( "writer", writer ).With( "reader", reader ).With( "amount", 2 );
		Assert.IsTrue( invocation.Compile(), script.LastError?.Message );
		Assert.AreEqual( 1f, writer.Opacity, "Compilation must not execute the script." );
		Assert.AreEqual( 0.5f, invocation.RunAndReturn<float>( instructionLimit: 1000 ), script.LastError?.Message );
		Assert.AreEqual( 0.5f, writer.Opacity );
		Assert.IsFalse( script.Run(), "Temporary arguments must not remain on the script." );
		Assert.IsTrue( invocation.Run(), script.LastError?.Message );
	}

	[TestMethod]
	public void FluentChainsAreIndependentAndLastArgumentWins()
	{
		var script = Game.Scripting.CreateScript( "return amount;" );
		var first = script.With( "amount", 10 );
		var second = first.With( "amount", 20 );
		Assert.AreEqual( 20, second.RunAndReturn<int>() );
		Assert.AreEqual( 10, first.RunAndReturn<int>() );
		Assert.IsFalse( script.Run() );
		Assert.ThrowsException<ArgumentException>( () => { script.With( "missing", 1 ); } );
	}

	[TestMethod, Timeout( 10000 )]
	public void FluentInvocationHonorsInstructionLimitAndCanRecover()
	{
		var script = Game.Scripting.CreateScript( "for (var i = 0; i < amount; i++) { } return amount;" );
		var invocation = script.With( "amount", 100 );
		Assert.IsFalse( invocation.Run( instructionLimit: 10 ) );
		StringAssert.Contains( script.LastError.Message.ToLowerInvariant(), "instruction limit" );
		Assert.AreEqual( 100, invocation.RunAndReturn<int>(), script.LastError?.Message );
		Assert.IsNull( script.LastError );
	}

	[TestMethod]
	public void FluentInvocationChecksItsOwningContextAtExecution()
	{
		var script = Game.Scripting.CreateScript( "return amount;" );
		var invocation = script.With( "amount", 1 );
		var owner = GlobalContext.Current;
		try
		{
			GlobalContext.Current = new GlobalContext { TypeLibrary = new TypeLibrary() };
			Assert.IsFalse( invocation.Run() );
			StringAssert.Contains( script.LastError.Message, "owning" );
			try
			{
				invocation.Compile();
				Assert.Fail( "Compilation should reject the foreign context." );
			}
			catch ( InvalidOperationException ) { }
		}
		finally { GlobalContext.Current = owner; }
		Assert.AreEqual( 1, invocation.RunAndReturn<int>() );
	}
}
