using Sandbox.Internal;

namespace SceneTests.Components;

/// <summary>
/// Tests for the update dispatch in Component.Update.cs. OnUpdate/OnFixedUpdate
/// overrides only run when the component also implements the matching
/// IUpdateSubscriber / IFixedUpdateSubscriber marker (codegen adds these
/// automatically in game code; here we implement them by hand). Plain
/// components can still take part in the update loop through the
/// OnComponentUpdate / OnComponentFixedUpdate action properties.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentUpdateTest : SceneTest
{
	/// <summary>
	/// A component implementing IUpdateSubscriber gets exactly one OnUpdate call
	/// per game tick.
	/// </summary>
	[TestMethod]
	public void OnUpdateRunsOncePerGameTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<UpdateProbeComponent>();

		Assert.AreEqual( 0, comp.UpdateCalls );

		scene.GameTick();
		Assert.AreEqual( 1, comp.UpdateCalls );

		scene.GameTick();
		Assert.AreEqual( 2, comp.UpdateCalls );
	}

	/// <summary>
	/// OnStart runs exactly once, and always before the first OnUpdate.
	/// </summary>
	[TestMethod]
	public void OnStartRunsOnceBeforeFirstUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<UpdateProbeComponent>();

		scene.GameTick();
		scene.GameTick();

		Assert.AreEqual( 1, comp.StartCalls );
		Assert.IsTrue( comp.StartedBeforeFirstUpdate );
	}

	/// <summary>
	/// Disabling a component stops its OnUpdate from being called; re-enabling
	/// it resumes updates.
	/// </summary>
	[TestMethod]
	public void OnUpdateStopsWhenDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<UpdateProbeComponent>();

		scene.GameTick();
		Assert.AreEqual( 1, comp.UpdateCalls );

		comp.Enabled = false;
		scene.GameTick();
		Assert.AreEqual( 1, comp.UpdateCalls );

		comp.Enabled = true;
		scene.GameTick();
		Assert.AreEqual( 2, comp.UpdateCalls );
	}

	/// <summary>
	/// A component implementing IFixedUpdateSubscriber gets OnFixedUpdate calls
	/// during the game tick, and stops getting them while disabled.
	/// </summary>
	[TestMethod]
	public void OnFixedUpdateRunsDuringGameTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<FixedUpdateProbeComponent>();

		Assert.AreEqual( 0, comp.FixedUpdateCalls );

		scene.GameTick();
		Assert.IsTrue( comp.FixedUpdateCalls > 0 );

		var countWhenDisabled = comp.FixedUpdateCalls;
		comp.Enabled = false;

		scene.GameTick();
		Assert.AreEqual( countWhenDisabled, comp.FixedUpdateCalls );
	}

	/// <summary>
	/// Assigning OnComponentUpdate on a plain component (no IUpdateSubscriber)
	/// subscribes it to the update loop; clearing the action unsubscribes it.
	/// </summary>
	[TestMethod]
	public void OnComponentUpdateActionSubscribesPlainComponent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>();

		var calls = 0;
		comp.OnComponentUpdate = () => calls++;

		scene.GameTick();
		Assert.AreEqual( 1, calls );

		comp.OnComponentUpdate = null;

		scene.GameTick();
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// An OnComponentUpdate action assigned while the component is disabled
	/// doesn't run, but starts running once the component is enabled.
	/// </summary>
	[TestMethod]
	public void OnComponentUpdateActionSetWhileDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>( false );

		var calls = 0;
		comp.OnComponentUpdate = () => calls++;

		scene.GameTick();
		Assert.AreEqual( 0, calls );

		comp.Enabled = true;

		scene.GameTick();
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// The OnComponentStart action runs once on the first tick after the
	/// component becomes active, and never again.
	/// </summary>
	[TestMethod]
	public void OnComponentStartActionRunsOnce()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>();

		var calls = 0;
		comp.OnComponentStart = () => calls++;

		scene.GameTick();
		Assert.AreEqual( 1, calls );

		scene.GameTick();
		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// Assigning OnComponentFixedUpdate on a plain component subscribes it to
	/// the fixed update loop; clearing the action unsubscribes it.
	/// </summary>
	[TestMethod]
	public void OnComponentFixedUpdateActionSubscribesPlainComponent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>();

		var calls = 0;
		comp.OnComponentFixedUpdate = () => calls++;

		scene.GameTick();
		Assert.IsTrue( calls > 0 );

		var countWhenCleared = calls;
		comp.OnComponentFixedUpdate = null;

		scene.GameTick();
		Assert.AreEqual( countWhenCleared, calls );
	}
}

/// <summary>
/// Component subscribed to per-frame updates via the IUpdateSubscriber marker,
/// recording OnStart/OnUpdate calls and their relative order.
/// </summary>
public class UpdateProbeComponent : Component, IUpdateSubscriber
{
	public int StartCalls;
	public int UpdateCalls;
	public bool StartedBeforeFirstUpdate;

	/// <summary>
	/// Counts OnStart invocations.
	/// </summary>
	protected override void OnStart()
	{
		StartCalls++;
	}

	/// <summary>
	/// Counts OnUpdate invocations and records whether OnStart ran first.
	/// </summary>
	protected override void OnUpdate()
	{
		if ( UpdateCalls == 0 )
		{
			StartedBeforeFirstUpdate = StartCalls == 1;
		}

		UpdateCalls++;
	}
}

/// <summary>
/// Component subscribed to fixed updates via the IFixedUpdateSubscriber marker.
/// </summary>
public class FixedUpdateProbeComponent : Component, IFixedUpdateSubscriber
{
	public int FixedUpdateCalls;

	/// <summary>
	/// Counts OnFixedUpdate invocations.
	/// </summary>
	protected override void OnFixedUpdate()
	{
		FixedUpdateCalls++;
	}
}

/// <summary>
/// Component with no update overrides and no subscriber markers - it only
/// takes part in the update loop via the OnComponent* action properties.
/// </summary>
public class PlainTickComponent : Component
{
}
