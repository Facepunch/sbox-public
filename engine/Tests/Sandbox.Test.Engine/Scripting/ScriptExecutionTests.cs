using Sandbox.Engine;
using Sandbox.Internal;

namespace ScriptingTests;

[TestClass, DoNotParallelize]
public class ScriptExecutionTests
{
	[TestMethod, Timeout( 10000 )]
	public void LargeFiniteLoopSucceedsAndInfiniteLoopIsStopped()
	{
		var previous = GlobalContext.Current;
		try
		{
			var types = new TypeLibrary();
			types.AddIntrinsicTypes();
			GlobalContext.Current = new GlobalContext { TypeLibrary = types };
			var finite = Game.Scripting.CreateScript( "var count = 0; for (var i = 0; i < 100000; i++) count++; return count;" );
			Assert.AreEqual( 100000, finite.RunAndReturn<int>() );
			Assert.IsNull( finite.LastError );
			var infinite = Game.Scripting.CreateScript( "while (true) { }" );
			Assert.IsFalse( infinite.Run() );
			StringAssert.Contains( infinite.LastError.Message.ToLowerInvariant(), "instruction limit" );
		}
		finally
		{
			GlobalContext.Current = previous;
		}
	}
}
