using SceneTests.GameObjects;

namespace SceneTests.Core;

/// <summary>
/// Pins CallbackBatch semantics: state changes inside a batch flush when the
/// outermost batch disposes, including nested batches.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CallbackBatchTest : SceneTest
{
	/// <summary>
	/// Component callbacks from state changes inside a batch only fire when the batch
	/// is disposed.
	/// </summary>
	[TestMethod]
	public void CallbacksDeferUntilBatchEnds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var o = go.Components.Create<OrderTestComponent>();

		// Tick once so OnStart has run. The fixture's lifecycle-order asserts are
		// swallowed by CallbackBatch's catch, but they throw before the counters
		// increment - so a violated invariant surfaces indirectly as a wrong
		// counter in the assertions below.
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );

		using ( CallbackBatch.Batch() )
		{
			o.Enabled = false;

			// The disable callback hasn't run yet - it's queued in the batch
			Assert.AreEqual( 0, o.DisabledCalls );
		}

		Assert.AreEqual( 1, o.DisabledCalls );
	}

	/// <summary>
	/// CallbackBatch.Isolated executes its callbacks at its own dispose even while an
	/// outer batch is still open - the mechanism Clone relies on so cloned objects are
	/// fully initialized immediately.
	/// </summary>
	[TestMethod]
	public void IsolatedFlushesImmediately()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var o = go.Components.Create<OrderTestComponent>();

		// Tick once so OnStart has run. The fixture's lifecycle-order asserts are
		// swallowed by CallbackBatch's catch, but they throw before the counters
		// increment - so a violated invariant surfaces indirectly as a wrong
		// counter in the assertions below.
		scene.GameTick();

		using ( CallbackBatch.Batch() )
		{
			using ( CallbackBatch.Isolated() )
			{
				o.Enabled = false;
			}

			// The isolated scope flushed on its own dispose, outer batch still open
			Assert.AreEqual( 1, o.DisabledCalls );
		}
	}

	/// <summary>
	/// A nested batch doesn't flush on its own dispose - everything flushes when the
	/// outermost batch completes.
	/// </summary>
	[TestMethod]
	public void NestedBatchFlushesWithOutermost()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var o = go.Components.Create<OrderTestComponent>();

		// Tick once so OnStart has run. The fixture's lifecycle-order asserts are
		// swallowed by CallbackBatch's catch, but they throw before the counters
		// increment - so a violated invariant surfaces indirectly as a wrong
		// counter in the assertions below.
		scene.GameTick();

		using ( CallbackBatch.Batch() )
		{
			using ( CallbackBatch.Batch() )
			{
				o.Enabled = false;
			}

			// Inner batch disposed, but the outer batch is still open
			Assert.AreEqual( 0, o.DisabledCalls );
		}

		Assert.AreEqual( 1, o.DisabledCalls );
	}
}
