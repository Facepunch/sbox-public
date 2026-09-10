using System;
using System.Collections.Generic;

namespace SceneTests.Core;

/// <summary>
/// Pins the TimedCallbackList contract that scene system stage signals are built on:
/// execution ordered by order value, cancellation by disposing the registration,
/// per-callback exception isolation, and the run metrics.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TimedCallbackCoverageTest : SceneTest
{
	/// <summary>
	/// Reads the "Count" member of the anonymous metric object returned by
	/// TimedCallbackList.GetMetrics.
	/// </summary>
	static int GetRunCount( object metric )
	{
		return (int)metric.GetType().GetProperty( "Count" ).GetValue( metric );
	}

	/// <summary>
	/// Callbacks execute ordered by their order value, not by registration order.
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
	/// Disposing the returned handle removes the callback from future runs without
	/// touching the other registrations.
	/// </summary>
	[TestMethod]
	public void DisposeCancelsRegistration()
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
	/// A callback that throws doesn't break the run: the exception is swallowed and
	/// the remaining callbacks still execute.
	/// </summary>
	[TestMethod]
	public void ExceptionDoesNotStopTheRun()
	{
		var list = new TimedCallbackList();
		var log = new List<int>();

		list.Add( 1, () => throw new InvalidOperationException( "callback explosion" ), "test", "thrower" );
		list.Add( 2, () => log.Add( 2 ), "test", "survivor" );

		list.Run();

		CollectionAssert.AreEqual( new[] { 2 }, log );
	}

	/// <summary>
	/// Metrics count successful runs per callback - a throwing callback doesn't count -
	/// and ClearMetrics resets the counters to zero.
	/// </summary>
	[TestMethod]
	public void MetricsCountRunsAndClear()
	{
		var list = new TimedCallbackList();

		list.Add( 1, () => { }, "test", "works" );
		list.Add( 2, () => throw new InvalidOperationException( "callback explosion" ), "test", "throws" );

		list.Run();
		list.Run();

		var metrics = list.GetMetrics();

		Assert.AreEqual( 2, metrics.Length );
		Assert.AreEqual( 2, GetRunCount( metrics[0] ) );
		Assert.AreEqual( 0, GetRunCount( metrics[1] ) );

		list.ClearMetrics();
		metrics = list.GetMetrics();

		Assert.AreEqual( 0, GetRunCount( metrics[0] ) );
		Assert.AreEqual( 0, GetRunCount( metrics[1] ) );
	}

	/// <summary>
	/// Scene.AddHook registers a callback on a stage that runs every game tick, in
	/// order-value order, until its handle is disposed - the public surface over
	/// TimedCallbackList.
	/// </summary>
	[TestMethod]
	public void SceneHookRunsEveryTickUntilDisposed()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var log = new List<string>();

		var second = scene.AddHook( GameObjectSystem.Stage.StartUpdate, 10, () => log.Add( "second" ), "test", "second" );
		var first = scene.AddHook( GameObjectSystem.Stage.StartUpdate, 5, () => log.Add( "first" ), "test", "first" );

		scene.GameTick();

		CollectionAssert.AreEqual( new[] { "first", "second" }, log );

		log.Clear();
		second.Dispose();

		scene.GameTick();

		CollectionAssert.AreEqual( new[] { "first" }, log );

		first.Dispose();
		log.Clear();

		scene.GameTick();

		Assert.AreEqual( 0, log.Count );

		scene.Destroy();
	}
}
