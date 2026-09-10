namespace SceneTests.Components;

/// <summary>
/// Tests for the dirty-marking machinery in Component.Dirty.cs: OnPropertyDirty
/// schedules a single OnDirty callback through the CallbackBatch, deduplicating
/// repeat marks until the dirty state has been flushed.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentDirtyTest : SceneTest
{
	/// <summary>
	/// Marking a component dirty outside of any callback batch invokes OnDirty
	/// synchronously, exactly once.
	/// </summary>
	[TestMethod]
	public void MarkDirtyInvokesOnDirty()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DirtyProbeComponent>();

		Assert.AreEqual( 0, comp.DirtyCalls );

		comp.MarkDirty();

		Assert.AreEqual( 1, comp.DirtyCalls );
	}

	/// <summary>
	/// Inside a callback batch the dirty callback is deferred to the end of the
	/// batch, and marking the component dirty multiple times still results in a
	/// single OnDirty call.
	/// </summary>
	[TestMethod]
	public void DirtyIsDeduplicatedWithinBatch()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DirtyProbeComponent>();

		using ( CallbackBatch.Batch() )
		{
			comp.MarkDirty();
			comp.MarkDirty();
			comp.MarkDirty();

			Assert.AreEqual( 0, comp.DirtyCalls );
		}

		Assert.AreEqual( 1, comp.DirtyCalls );
	}

	/// <summary>
	/// Once the dirty state has been flushed the component can become dirty
	/// again - a later mark fires OnDirty a second time.
	/// </summary>
	[TestMethod]
	public void DirtyFiresAgainAfterFlush()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DirtyProbeComponent>();

		comp.MarkDirty();
		comp.MarkDirty();

		Assert.AreEqual( 2, comp.DirtyCalls );
	}

	/// <summary>
	/// A destroyed (invalid) component ignores dirty marks completely - OnDirty
	/// is never invoked for it.
	/// </summary>
	[TestMethod]
	public void DirtyIgnoredOnDestroyedComponent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<DirtyProbeComponent>();

		comp.Destroy();
		comp.MarkDirty();

		Assert.AreEqual( 0, comp.DirtyCalls );
	}
}

/// <summary>
/// Component exposing the protected, obsolete OnPropertyDirty mechanism so the
/// tests can drive it, counting OnDirty invocations.
/// </summary>
public class DirtyProbeComponent : Component
{
	public int DirtyCalls;

	/// <summary>
	/// Marks this component dirty via the protected OnPropertyDirty helper.
	/// </summary>
	public void MarkDirty() => OnPropertyDirty();

#pragma warning disable CS0672 // overrides an obsolete member - that's the point of the test
	/// <summary>
	/// Counts how many times the dirty callback has run.
	/// </summary>
	protected override void OnDirty()
	{
		DirtyCalls++;
	}
#pragma warning restore CS0672
}
