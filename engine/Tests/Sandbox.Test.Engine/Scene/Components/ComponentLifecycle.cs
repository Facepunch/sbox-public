using System.Collections.Generic;

namespace SceneTests.Components;

/// <summary>
/// Tests for the core Component behaviour in Component.cs: shortcut accessors,
/// destruction, the OnComponent* action callbacks, the Enabled/Active split,
/// tag change notifications and Reset().
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentLifecycleTest : SceneTest
{
	/// <summary>
	/// Scene, Transform, Components and Tags on a component are shortcuts to the
	/// owning GameObject's members - the very same instances, not copies.
	/// </summary>
	[TestMethod]
	public void ShortcutAccessorsPointAtGameObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<LifecycleProbeComponent>();

		Assert.AreSame( go, comp.GameObject );
		Assert.AreSame( scene, comp.Scene );
		Assert.AreSame( go.Transform, comp.Transform );
		Assert.AreSame( go.Components, comp.Components );

		go.Tags.Add( "probe" );
		Assert.IsTrue( comp.Tags.Has( "probe" ) );
	}

	/// <summary>
	/// OnTagsChanged is dispatched to components when a tag is added to or
	/// removed from the owning GameObject.
	/// </summary>
	[TestMethod]
	public void OnTagsChangedFiresOnTagChange()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<LifecycleProbeComponent>();

		var baseline = comp.TagsChangedCalls;

		go.Tags.Add( "alpha" );
		Assert.AreEqual( baseline + 1, comp.TagsChangedCalls );

		go.Tags.Remove( "alpha" );
		Assert.AreEqual( baseline + 2, comp.TagsChangedCalls );
	}

	/// <summary>
	/// Destroying a component takes effect immediately (unlike GameObject
	/// destruction): it is disabled then destroyed in that order, unlinked from
	/// its GameObject, removed from the component list and becomes invalid.
	/// The GameObject itself survives.
	/// </summary>
	[TestMethod]
	public void DestroyComponentTakesEffectImmediately()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<LifecycleProbeComponent>();

		Assert.IsTrue( comp.IsValid );

		comp.Destroy();

		Assert.IsFalse( comp.IsValid );
		Assert.IsNull( comp.GameObject );
		Assert.IsNull( comp.Scene );
		Assert.IsNull( comp.Components );

		Assert.AreEqual( 1, comp.DisabledCalls );
		Assert.AreEqual( 1, comp.DestroyCalls );
		CollectionAssert.AreEqual( new List<string> { "Enabled", "Disabled", "Destroy" }, comp.Order );

		Assert.AreEqual( 0, go.Components.GetAll().Count() );
		Assert.IsTrue( go.IsValid );
	}

	/// <summary>
	/// Destroying an already destroyed component is a safe no-op - the lifecycle
	/// callbacks don't run a second time.
	/// </summary>
	[TestMethod]
	public void DestroyTwiceIsNoOp()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<LifecycleProbeComponent>();

		comp.Destroy();
		comp.Destroy();

		Assert.AreEqual( 1, comp.DisabledCalls );
		Assert.AreEqual( 1, comp.DestroyCalls );
	}

	/// <summary>
	/// DestroyGameObject destroys the whole owning GameObject, not just the
	/// component. The object goes through the usual deferred delete, so it dies
	/// on the next tick along with the component.
	/// </summary>
	[TestMethod]
	public void DestroyGameObjectDestroysOwner()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<LifecycleProbeComponent>();

		comp.DestroyGameObject();

		scene.GameTick();

		Assert.IsFalse( go.IsValid );
		Assert.IsFalse( comp.IsValid );
		Assert.AreEqual( 1, comp.DestroyCalls );
	}

	/// <summary>
	/// The OnComponentEnabled / OnComponentDisabled / OnComponentDestroy action
	/// properties are invoked alongside the corresponding lifecycle callbacks.
	/// </summary>
	[TestMethod]
	public void ActionCallbacksAreInvoked()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<LifecycleProbeComponent>( false );

		int enabled = 0, disabled = 0, destroyed = 0;
		comp.OnComponentEnabled = () => enabled++;
		comp.OnComponentDisabled = () => disabled++;
		comp.OnComponentDestroy = () => destroyed++;

		comp.Enabled = true;
		Assert.AreEqual( 1, enabled );

		comp.Enabled = false;
		Assert.AreEqual( 1, disabled );

		comp.Destroy();
		Assert.AreEqual( 1, destroyed );

		Assert.AreEqual( 1, enabled );
		Assert.AreEqual( 1, disabled );
	}

	/// <summary>
	/// Enabled is what the component wants; Active is what it actually is. A
	/// component that is enabled on a disabled GameObject is not active, and
	/// becomes active once the whole ancestor chain is enabled.
	/// </summary>
	[TestMethod]
	public void ActiveReflectsGameObjectHierarchy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject( false );
		var comp = go.Components.Create<LifecycleProbeComponent>();

		Assert.IsTrue( comp.Enabled );
		Assert.IsFalse( comp.Active );

		go.Enabled = true;

		Assert.IsTrue( comp.Active );

		comp.Enabled = false;

		Assert.IsFalse( comp.Active );
		Assert.IsTrue( go.Active );
	}

	/// <summary>
	/// Reset restores every [Property] member to its default: the value from
	/// [DefaultValue] when present, otherwise the type default
	/// (SerializedProperty.GetDefault). Initializer values without a
	/// [DefaultValue] attribute are not preserved - the fixture's initializers
	/// deliberately differ from both the attribute value and the type default
	/// so each outcome is distinguishable.
	/// </summary>
	[TestMethod]
	public void ResetRestoresPropertyDefaults()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<ResetDefaultsComponent>();

		comp.Number = 99;
		comp.Plain = 99;

		comp.Reset();

		// [DefaultValue( 7 )] wins - not the initializer (3), not the type default (0)
		Assert.AreEqual( 7, comp.Number );

		// No attribute - type default (0) wins, the initializer (5) is not preserved
		Assert.AreEqual( 0, comp.Plain );
	}
}

/// <summary>
/// Component that records its lifecycle callbacks into counters and an ordered
/// log. Never asserts inside the callbacks - they run inside CallbackBatch
/// which swallows exceptions.
/// </summary>
public class LifecycleProbeComponent : Component
{
	public int EnabledCalls;
	public int DisabledCalls;
	public int DestroyCalls;
	public int TagsChangedCalls;

	public List<string> Order = new();

	/// <summary>
	/// Records the enable callback.
	/// </summary>
	protected override void OnEnabled()
	{
		EnabledCalls++;
		Order.Add( "Enabled" );
	}

	/// <summary>
	/// Records the disable callback.
	/// </summary>
	protected override void OnDisabled()
	{
		DisabledCalls++;
		Order.Add( "Disabled" );
	}

	/// <summary>
	/// Records the destroy callback.
	/// </summary>
	protected override void OnDestroy()
	{
		DestroyCalls++;
		Order.Add( "Destroy" );
	}

	/// <summary>
	/// Records tag change notifications.
	/// </summary>
	protected override void OnTagsChanged()
	{
		TagsChangedCalls++;
	}
}

/// <summary>
/// Component with one property carrying a [DefaultValue] and one without, to
/// pin what Component.Reset() restores. The initializers intentionally differ
/// from the attribute value and the type default so the test can tell apart
/// "restored from [DefaultValue]", "restored from initializer" and "type default".
/// </summary>
public class ResetDefaultsComponent : Component
{
	[Property, DefaultValue( 7 )]
	public int Number { get; set; } = 3;

	[Property]
	public int Plain { get; set; } = 5;
}
