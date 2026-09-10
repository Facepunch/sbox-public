using System.Collections.Generic;
using Sandbox.Internal;

namespace SceneTests.Core;

/// <summary>
/// Pins the TimedCallbackList contract used by scene system stage signals: callbacks
/// run ordered by their order value, and disposing the registration cancels it.
/// </summary>
[TestClass]
public class TimedCallbackListTest
{
	/// <summary>
	/// Callbacks execute ordered by their order value, not registration order.
	/// </summary>
	[TestMethod]
	public void RunsInOrder()
	{
		var list = new TimedCallbackList();
		var log = new List<int>();

		list.Add( 30, () => log.Add( 30 ), "test", "third" );
		list.Add( 10, () => log.Add( 10 ), "test", "first" );
		list.Add( 20, () => log.Add( 20 ), "test", "second" );

		list.Run();

		CollectionAssert.AreEqual( new[] { 10, 20, 30 }, log );
	}

	/// <summary>
	/// Disposing the returned handle removes the callback from future runs.
	/// </summary>
	[TestMethod]
	public void DisposeCancels()
	{
		var list = new TimedCallbackList();
		var log = new List<int>();

		list.Add( 1, () => log.Add( 1 ), "test", "keep" );
		var handle = list.Add( 2, () => log.Add( 2 ), "test", "cancel" );

		list.Run();
		CollectionAssert.AreEqual( new[] { 1, 2 }, log );

		log.Clear();
		handle.Dispose();

		list.Run();
		CollectionAssert.AreEqual( new[] { 1 }, log );
	}

	/// <summary>
	/// Callbacks sharing the same order value all run, but their relative sequence is
	/// unspecified (binary-search insertion) - only the set is guaranteed.
	/// </summary>
	[TestMethod]
	public void SameOrderAllRun()
	{
		var list = new TimedCallbackList();
		var log = new List<int>();

		list.Add( 5, () => log.Add( 1 ), "test", "first" );
		list.Add( 5, () => log.Add( 2 ), "test", "second" );
		list.Add( 5, () => log.Add( 3 ), "test", "third" );

		list.Run();

		CollectionAssert.AreEquivalent( new[] { 1, 2, 3 }, log );
	}
}
