using System;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins ComponentList behaviour not covered by ComponentLists.cs: creating by
/// TypeDescription, the FirstOrDefault helper, the ChildrenOrSelf/ParentOrSelf
/// search helpers, the IComponentLister default methods, ForEach robustness and
/// queries against destroyed objects.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentListCoverageTest : SceneTest
{
	/// <summary>
	/// Create(TypeDescription) instantiates the component type, honouring the
	/// startEnabled flag.
	/// </summary>
	[TestMethod]
	public void CreateByTypeDescription()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var type = Game.TypeLibrary.GetType( typeof( ListCoverageProbe ) );
		Assert.IsNotNull( type, "the test assembly must be registered in the global TypeLibrary" );

		var enabled = go.Components.Create( type );
		Assert.IsInstanceOfType( enabled, typeof( ListCoverageProbe ) );
		Assert.IsTrue( enabled.Enabled );

		var disabled = go.Components.Create( type, false );
		Assert.IsFalse( disabled.Enabled );

		Assert.AreEqual( 2, go.Components.Count );
	}

	/// <summary>
	/// Create(TypeDescription) refuses non-component types by returning null
	/// without adding anything to the list.
	/// </summary>
	[TestMethod]
	public void CreateByTypeDescriptionRejectsNonComponents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var type = Game.TypeLibrary.GetType( typeof( GameObject ) );
		Assert.IsNotNull( type );

		var created = go.Components.Create( type );

		Assert.IsNull( created );
		Assert.AreEqual( 0, go.Components.Count );
	}

	/// <summary>
	/// FirstOrDefault runs the predicate over the raw component list, returning
	/// null on an object that never had components.
	/// </summary>
	[TestMethod]
	public void FirstOrDefaultPredicate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var empty = scene.CreateObject();
		Assert.IsNull( empty.Components.FirstOrDefault( x => true ) );

		var go = scene.CreateObject();
		var a = go.Components.Create<ListCoverageProbe>();
		var b = go.Components.Create<ListCoverageOther>();

		Assert.AreEqual( b, go.Components.FirstOrDefault( x => x is ListCoverageOther ) );
		Assert.IsNull( go.Components.FirstOrDefault( x => false ) );
	}

	/// <summary>
	/// GetInChildrenOrSelf prefers a component on the object itself and falls
	/// back to immediate children - but never grandchildren.
	/// </summary>
	[TestMethod]
	public void GetInChildrenOrSelf()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var child = new GameObject( go );
		var grandchild = new GameObject( child );

		var gcComp = grandchild.Components.Create<ListCoverageProbe>();
		Assert.IsNull( go.Components.GetInChildrenOrSelf<ListCoverageProbe>(), "grandchildren are out of range for InChildren" );

		var childComp = child.Components.Create<ListCoverageProbe>();
		Assert.AreEqual( childComp, go.Components.GetInChildrenOrSelf<ListCoverageProbe>() );

		var selfComp = go.Components.Create<ListCoverageProbe>();
		Assert.AreEqual( selfComp, go.Components.GetInChildrenOrSelf<ListCoverageProbe>() );
	}

	/// <summary>
	/// GetInParentOrSelf prefers a component on the object itself and falls
	/// back to the direct parent - but never grandparents.
	/// </summary>
	[TestMethod]
	public void GetInParentOrSelf()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var grandparent = scene.CreateObject();
		var parent = new GameObject( grandparent );
		var go = new GameObject( parent );

		var gpComp = grandparent.Components.Create<ListCoverageProbe>();
		Assert.IsNull( go.Components.GetInParentOrSelf<ListCoverageProbe>(), "grandparents are out of range for InParent" );

		var parentComp = parent.Components.Create<ListCoverageProbe>();
		Assert.AreEqual( parentComp, go.Components.GetInParentOrSelf<ListCoverageProbe>() );

		var selfComp = go.Components.Create<ListCoverageProbe>();
		Assert.AreEqual( selfComp, go.Components.GetInParentOrSelf<ListCoverageProbe>() );
	}

	/// <summary>
	/// GameObject implements IComponentLister, whose default interface methods
	/// (Create, Get, TryGet, GetAll, GetOrCreate) all route to the component list.
	/// </summary>
	[TestMethod]
	public void ComponentListerDefaultMethods()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		IComponentLister lister = scene.CreateObject();

		var created = lister.Create<ListCoverageProbe>();
		Assert.IsNotNull( created );

		Assert.AreEqual( created, lister.Get<ListCoverageProbe>() );

		Assert.IsTrue( lister.TryGet<ListCoverageProbe>( out var found ) );
		Assert.AreEqual( created, found );

		Assert.IsFalse( lister.TryGet<ListCoverageOther>( out var missing ) );
		Assert.IsNull( missing );

		Assert.AreEqual( 1, lister.GetAll<ListCoverageProbe>().Count() );

		Assert.AreEqual( created, lister.GetOrCreate<ListCoverageProbe>() );
		Assert.AreEqual( 1, lister.Components.Count, "GetOrCreate must not duplicate an existing component" );
	}

	/// <summary>
	/// ForEach swallows exceptions thrown by the action per-component and keeps
	/// going, so one broken component can't starve the rest of a callback.
	/// </summary>
	[TestMethod]
	public void ForEachSwallowsExceptions()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<ListCoverageProbe>();
		go.Components.Create<ListCoverageOther>();

		int calls = 0;

		go.Components.ForEach( "Test", true, c =>
		{
			calls++;
			throw new InvalidOperationException( "intentional test failure" );
		} );

		Assert.AreEqual( 2, calls, "every component must still be visited" );
	}

	/// <summary>
	/// The generic ForEach only invokes the action for components assignable to
	/// the type argument.
	/// </summary>
	[TestMethod]
	public void ForEachGenericFiltersByType()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<ListCoverageProbe>();
		go.Components.Create<ListCoverageOther>();

		int calls = 0;
		go.Components.ForEach<ListCoverageOther>( "Test", true, c => calls++ );

		Assert.AreEqual( 1, calls );
	}

	/// <summary>
	/// RunEvent with the default find mode takes the enabled-in-self-and-descendants
	/// fast path: it reaches the object and enabled descendants, skipping
	/// components under disabled children.
	/// </summary>
	[TestMethod]
	public void RunEventDefaultReachesEnabledDescendants()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var rootReceiver = root.Components.Create<PokeReceiverComponent>();

		var child = new GameObject( root );
		var childReceiver = child.Components.Create<PokeReceiverComponent>();

		var disabledChild = new GameObject( root, false );
		var disabledReceiver = disabledChild.Components.Create<PokeReceiverComponent>();

		root.RunEvent<IPokeEvent>( x => x.Poke() );

		Assert.AreEqual( 1, rootReceiver.Pokes );
		Assert.AreEqual( 1, childReceiver.Pokes );
		Assert.AreEqual( 0, disabledReceiver.Pokes );
	}

	/// <summary>
	/// RunEvent with EverythingInDescendants reaches disabled descendants too,
	/// but not the object itself (InSelf is not part of the mode).
	/// </summary>
	[TestMethod]
	public void RunEventEverythingInDescendantsIncludesDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var rootReceiver = root.Components.Create<PokeReceiverComponent>();

		var disabledChild = new GameObject( root, false );
		var disabledReceiver = disabledChild.Components.Create<PokeReceiverComponent>();

		root.RunEvent<IPokeEvent>( x => x.Poke(), FindMode.EverythingInDescendants );

		Assert.AreEqual( 0, rootReceiver.Pokes, "self is not part of EverythingInDescendants" );
		Assert.AreEqual( 1, disabledReceiver.Pokes );
	}

	/// <summary>
	/// Once an object is marked destroyed - even before the deferred delete is
	/// flushed - component searches return nothing.
	/// </summary>
	[TestMethod]
	public void QueriesOnDestroyedObjectAreEmpty()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<ListCoverageProbe>();

		go.Destroy();

		Assert.IsTrue( go.IsDestroyed );
		Assert.AreEqual( 0, go.Components.GetAll<ListCoverageProbe>( FindMode.EverythingInSelf ).Count() );
		Assert.IsNull( go.Components.Get<ListCoverageProbe>( FindMode.EverythingInSelf ) );

		scene.ProcessDeletes();

		Assert.AreEqual( 0, go.Components.GetAll<ListCoverageProbe>( FindMode.EverythingInSelf ).Count() );
	}

	/// <summary>
	/// Event interface used by the RunEvent tests.
	/// </summary>
	public interface IPokeEvent
	{
		/// <summary>
		/// Receives the test event.
		/// </summary>
		void Poke();
	}

	/// <summary>
	/// Component that counts how many times it received the poke event.
	/// </summary>
	public class PokeReceiverComponent : Component, IPokeEvent
	{
		public int Pokes;

		/// <summary>
		/// Counts the received event.
		/// </summary>
		public void Poke() => Pokes++;
	}

	/// <summary>
	/// Bare component used for list queries.
	/// </summary>
	public class ListCoverageOther : Component
	{
	}
}

/// <summary>
/// Bare component used for TypeDescription creation - top level so the global
/// TypeLibrary can resolve it cleanly.
/// </summary>
public class ListCoverageProbe : Component
{
}
