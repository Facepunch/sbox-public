using System;
using System.Collections.Generic;

namespace SceneTests.Core;

/// <summary>
/// Plain component counting its enable/disable callbacks, with no assertions of its
/// own, for observing when batched callbacks actually run.
/// </summary>
public class BatchProbeComponent : Component
{
	public int EnabledCalls;
	public int DisabledCalls;

	protected override void OnEnabled() => EnabledCalls++;
	protected override void OnDisabled() => DisabledCalls++;
}

/// <summary>
/// Component whose OnEnabled records the call and then throws, to prove batch
/// execution isolates exceptions per callback.
/// </summary>
public class ThrowingEnableProbe : Component
{
	public int EnabledCalls;

	protected override void OnEnabled()
	{
		EnabledCalls++;
		throw new InvalidOperationException( "enable explosion" );
	}
}

/// <summary>
/// Pins CallbackBatch mechanics beyond the basic deferral tests: the guard against
/// adding outside a batch, stage ordering of queued actions, exception isolation, and
/// callbacks generated while a batch is executing.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CallbackBatchCoverageTest : SceneTest
{
	/// <summary>
	/// Queuing a callback when no batch is open is a programming error and throws -
	/// for both the direct-dispatch and the action-based overloads.
	/// </summary>
	[TestMethod]
	public void AddOutsideBatchThrows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.ThrowsException<Exception>( () => CallbackBatch.Add( CommonCallback.Enable, go, "test" ) );
		Assert.ThrowsException<Exception>( () => CallbackBatch.Add( CommonCallback.Enable, () => { }, go, "test" ) );

		scene.Destroy();
	}

	/// <summary>
	/// Queued actions execute grouped by callback stage in enum order - an Awake
	/// callback runs before a Destroy callback even when queued after it - and nothing
	/// runs until the batch is disposed.
	/// </summary>
	[TestMethod]
	public void ActionsRunInStageOrderOnDispose()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var log = new List<string>();

		using ( CallbackBatch.Batch() )
		{
			CallbackBatch.Add( CommonCallback.Destroy, () => log.Add( "destroy" ), go, "test" );
			CallbackBatch.Add( CommonCallback.Awake, () => log.Add( "awake" ), go, "test" );

			Assert.AreEqual( 0, log.Count );
		}

		CollectionAssert.AreEqual( new[] { "awake", "destroy" }, log );

		scene.Destroy();
	}

	/// <summary>
	/// An action that throws during batch execution is swallowed - the remaining
	/// queued actions still run and nothing escapes the batch's dispose.
	/// </summary>
	[TestMethod]
	public void ThrowingActionDoesNotStopBatch()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var log = new List<string>();

		using ( CallbackBatch.Batch() )
		{
			CallbackBatch.Add( CommonCallback.Enable, () => throw new InvalidOperationException( "action explosion" ), go, "test" );
			CallbackBatch.Add( CommonCallback.Enable, () => log.Add( "survivor" ), go, "test" );
			CallbackBatch.Add( CommonCallback.Disable, () => log.Add( "later group" ), go, "test" );
		}

		CollectionAssert.AreEqual( new[] { "survivor", "later group" }, log );

		scene.Destroy();
	}

	/// <summary>
	/// A component whose OnEnabled throws doesn't poison the rest of the batch -
	/// components created in the same batch still receive their callbacks.
	/// </summary>
	[TestMethod]
	public void LifecycleExceptionsAreIsolated()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		ThrowingEnableProbe thrower;
		BatchProbeComponent recorder;

		using ( CallbackBatch.Batch() )
		{
			thrower = scene.CreateObject().Components.Create<ThrowingEnableProbe>();
			recorder = scene.CreateObject().Components.Create<BatchProbeComponent>();
		}

		Assert.AreEqual( 1, thrower.EnabledCalls );
		Assert.AreEqual( 1, recorder.EnabledCalls );

		scene.Destroy();
	}

	/// <summary>
	/// Callbacks generated while a batch executes - here, an action enabling a
	/// component - are collected into a follow-up batch and have all run by the time
	/// the outermost dispose returns.
	/// </summary>
	[TestMethod]
	public void CallbacksQueuedDuringExecutionRun()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var probe = go.Components.Create<BatchProbeComponent>( false );

		var enabledWhenActionRan = -1;

		using ( CallbackBatch.Batch() )
		{
			CallbackBatch.Add( CommonCallback.Enable, () =>
			{
				probe.Enabled = true;
				enabledWhenActionRan = probe.EnabledCalls;
			}, go, "test" );
		}

		// The enable was deferred while the action ran, then flushed before the
		// outer dispose returned
		Assert.AreEqual( 0, enabledWhenActionRan );
		Assert.AreEqual( 1, probe.EnabledCalls );

		scene.Destroy();
	}
}
