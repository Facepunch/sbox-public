using System;
using System.Collections.Generic;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins what happens when the scene graph is mutated while it's being iterated:
/// destroying or creating objects and components from inside update callbacks must
/// never break the frame or skip unrelated objects.
/// </summary>
[TestClass]
[DoNotParallelize]
public class IterationSafetyTest : SceneTest
{
	/// <summary>
	/// Pins deferred-destroy semantics: GameObject.Destroy only queues the object,
	/// so a component destroying its own object mid-update doesn't mutate the list
	/// being iterated - the other objects update this tick, and the destroyed one
	/// is gone by the next.
	/// </summary>
	[TestMethod]
	public void DestroySelfDuringUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var log = new List<string>();

		var a = MakeProbe( scene, "A", log );
		var b = MakeProbe( scene, "B", log );
		var c = MakeProbe( scene, "C", log );

		b.OnUpdateAction = probe => probe.GameObject.Destroy();

		scene.GameTick();

		CollectionAssert.Contains( log, "A" );
		CollectionAssert.Contains( log, "C" );

		log.Clear();
		scene.GameTick();

		CollectionAssert.Contains( log, "A" );
		CollectionAssert.Contains( log, "C" );
		CollectionAssert.DoesNotContain( log, "B" );
	}

	/// <summary>
	/// An exception thrown by one component's update is logged and contained - the
	/// remaining components and objects still update.
	/// </summary>
	[TestMethod]
	public void ExceptionInUpdateIsIsolated()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var log = new List<string>();

		var a = MakeProbe( scene, "A", log );
		a.OnUpdateAction = _ => throw new InvalidOperationException( "intentional test exception" );

		var b = MakeProbe( scene, "B", log );

		scene.GameTick();

		// A ran (and threw after logging), B must still have run
		CollectionAssert.Contains( log, "A" );
		CollectionAssert.Contains( log, "B" );

		// And the scene keeps ticking normally afterwards
		log.Clear();
		scene.GameTick();
		CollectionAssert.Contains( log, "B" );
	}

	/// <summary>
	/// Creating new objects and components from inside an update callback is legal;
	/// the new component updates on a following tick.
	/// </summary>
	[TestMethod]
	public void CreateDuringUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var log = new List<string>();

		var spawner = MakeProbe( scene, "Spawner", log );
		spawner.OnUpdateAction = probe =>
		{
			if ( probe.Scene.Directory.FindByName( "Spawned" ).Any() )
				return;

			var spawned = new GameObject( probe.GameObject );
			spawned.Name = "Spawned";
			var p = spawned.Components.Create<IterationProbe>();
			p.Log = log;
			p.LogName = "Spawned";
		};

		scene.GameTick();
		scene.GameTick();

		CollectionAssert.Contains( log, "Spawned" );
	}

	/// <summary>
	/// A component destroying a sibling component mid-update must not corrupt the
	/// component list - the destroyed sibling stops updating.
	/// </summary>
	[TestMethod]
	public void DestroySiblingComponentDuringUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var log = new List<string>();

		var go = scene.CreateObject();
		go.Name = "Host";

		var killer = go.Components.Create<IterationProbe>();
		killer.Log = log;
		killer.LogName = "Killer";

		var victim = go.Components.Create<IterationProbe>();
		victim.Log = log;
		victim.LogName = "Victim";

		killer.OnUpdateAction = _ => victim.Destroy();

		scene.GameTick();

		log.Clear();
		scene.GameTick();

		CollectionAssert.Contains( log, "Killer" );
		CollectionAssert.DoesNotContain( log, "Victim" );
		Assert.AreEqual( 1, go.Components.Count );
	}

	/// <summary>
	/// Pins deferred-destroy semantics for children: destroying a sibling object
	/// mid-update only queues it (no mid-iteration mutation of the child list), so
	/// the remaining children still update and the destroyed one is removed from
	/// the parent by the next tick.
	/// </summary>
	[TestMethod]
	public void DestroyChildDuringParentUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var log = new List<string>();

		var parent = scene.CreateObject();
		parent.Name = "Parent";

		var first = MakeProbe( scene, "First", log, parent );
		var second = MakeProbe( scene, "Second", log, parent );
		var third = MakeProbe( scene, "Third", log, parent );

		first.OnUpdateAction = _ => second.GameObject.Destroy();

		scene.GameTick();
		log.Clear();
		scene.GameTick();

		CollectionAssert.Contains( log, "First" );
		CollectionAssert.Contains( log, "Third" );
		CollectionAssert.DoesNotContain( log, "Second" );
		Assert.AreEqual( 2, parent.Children.Count );
	}

	static IterationProbe MakeProbe( Scene scene, string name, List<string> log, GameObject parent = null )
	{
		var go = parent is null ? scene.CreateObject() : new GameObject( parent );
		go.Name = name;

		var probe = go.Components.Create<IterationProbe>();
		probe.Log = log;
		probe.LogName = name;
		return probe;
	}
}

/// <summary>
/// Update probe used by <see cref="IterationSafetyTest"/>: records each update into a
/// log and optionally runs a mutation action. The IUpdateSubscriber marker is normally
/// added by the game code generator - test assemblies have to implement it by hand or
/// OnUpdate never runs.
/// </summary>
public class IterationProbe : Component, Sandbox.Internal.IUpdateSubscriber
{
	public List<string> Log;
	public string LogName;
	public Action<IterationProbe> OnUpdateAction;

	protected override void OnUpdate()
	{
		Log?.Add( LogName );
		OnUpdateAction?.Invoke( this );
	}
}
