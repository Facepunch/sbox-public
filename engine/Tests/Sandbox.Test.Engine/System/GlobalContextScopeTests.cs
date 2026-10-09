using Sandbox.Engine;
using System;
using System.Threading;

namespace SystemTests;

/// <summary>
/// GlobalContextScope reuses the execution context it made last time instead of setting the
/// AsyncLocal again. It has to behave exactly like setting it.
/// </summary>
[TestClass]
public class GlobalContextScopeTests
{
	static readonly AsyncLocal<string> Other = new();

	[TestMethod]
	public void EntersAndRestoresTheContext()
	{
		var before = GlobalContext.Current;

		using ( new GlobalContext.GlobalContextScope( GlobalContext.Menu ) )
		{
			Assert.AreSame( GlobalContext.Menu, GlobalContext.Current );

			using ( new GlobalContext.GlobalContextScope( GlobalContext.Game ) )
			{
				Assert.AreSame( GlobalContext.Game, GlobalContext.Current );
			}

			Assert.AreSame( GlobalContext.Menu, GlobalContext.Current );
		}

		Assert.AreSame( before, GlobalContext.Current );
	}

	/// <summary>
	/// Entering the same context from the same place again lands on the same execution context,
	/// so a scope in a loop doesn't allocate.
	/// </summary>
	[TestMethod]
	public void RepeatedScopesReuseTheExecutionContext()
	{
		ExecutionContext first;
		using ( new GlobalContext.GlobalContextScope( GlobalContext.Menu ) )
			first = ExecutionContext.Capture();

		var outside = ExecutionContext.Capture();
		Assert.AreSame( first, first ); // warm up

		long before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
		{
			using var scope = new GlobalContext.GlobalContextScope( GlobalContext.Menu );
			Assert.AreSame( first, ExecutionContext.Capture() );
		}

		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before );
		Assert.AreSame( outside, ExecutionContext.Capture() );
	}

	/// <summary>
	/// Another AsyncLocal set inside the scope survives it, like it did when leaving the scope just
	/// set the context back - restoring the outer execution context would undo it.
	/// </summary>
	[TestMethod]
	public void OtherAsyncLocalsSetInsideTheScopeSurvive()
	{
		var before = GlobalContext.Current;
		Other.Value = "before";

		using ( new GlobalContext.GlobalContextScope( GlobalContext.Menu ) )
		{
			Other.Value = "inside";
		}

		Assert.AreSame( before, GlobalContext.Current );
		Assert.AreEqual( "inside", Other.Value );
	}

	/// <summary>
	/// The cache is keyed by the context object, so a replaced context isn't served a stale one.
	/// </summary>
	[TestMethod]
	public void DifferentContextObjectsDontShareCachedEntries()
	{
		var a = new GlobalContext();
		var b = new GlobalContext();

		using ( new GlobalContext.GlobalContextScope( a ) ) Assert.AreSame( a, GlobalContext.Current );
		using ( new GlobalContext.GlobalContextScope( b ) ) Assert.AreSame( b, GlobalContext.Current );
		using ( new GlobalContext.GlobalContextScope( a ) ) Assert.AreSame( a, GlobalContext.Current );
	}
}
