using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Checks that code stops allocating once it's warm.
/// <para>
/// Measuring a fixed window after a fixed warm-up was flaky: the runtime does one-off work of its own
/// on the test's thread (compiling, loading types) whenever it gets round to it, and under load that
/// sometimes lands in the window. So passes run until enough of them in a row come in at or under the
/// budget. One-off work runs out; something that allocates every pass, or every few passes, never
/// settles, and fails with every pass's bytes.
/// </para>
/// <para>
/// Tests using this belong in <see cref="Category"/>. CI runs that category in a process of its own,
/// one test at a time, so other tests' threads aren't allocating around them (see SboxBuild's Test step).
/// </para>
/// </summary>
public static class Allocations
{
	/// <summary>
	/// The test category for allocation tests: <c>[TestCategory( Allocations.Category )]</c>.
	/// </summary>
	public const string Category = "Allocations";

	private const int SettledPasses = 5;
	private const int MaxPasses = 100;

	/// <summary>
	/// Runs <paramref name="pass"/> until <see cref="SettledPasses"/> passes in a row allocate at most
	/// <paramref name="allowedPerPass"/> bytes, failing if that doesn't happen within <see cref="MaxPasses"/>.
	/// There's no need to warm up first. <paramref name="beforePass"/> runs before each pass, outside the
	/// measurement. <paramref name="everyThread"/> counts every thread's allocations, for work that fans
	/// out to the thread pool, instead of just this thread's.
	/// </summary>
	public static void AssertSettles( string what, Action pass, long allowedPerPass = 0, Action beforePass = null, bool everyThread = false )
	{
		var bytes = new List<long>( MaxPasses );
		var settled = 0;

		while ( bytes.Count < MaxPasses )
		{
			beforePass?.Invoke();

			var before = Allocated( everyThread );
			pass();
			var allocated = Allocated( everyThread ) - before;

			bytes.Add( allocated );
			settled = allocated <= allowedPerPass ? settled + 1 : 0;
			if ( settled == SettledPasses ) return;
		}

		var budget = allowedPerPass == 0 ? "allocated nothing" : $"allocated {allowedPerPass} bytes or less";
		Assert.Fail( $"{what} kept allocating: in {MaxPasses} passes, never {SettledPasses} in a row {budget}. Bytes per pass: {string.Join( ", ", bytes )}" );
	}

	private static long Allocated( bool everyThread ) => everyThread ? GC.GetTotalAllocatedBytes( true ) : GC.GetAllocatedBytesForCurrentThread();
}
