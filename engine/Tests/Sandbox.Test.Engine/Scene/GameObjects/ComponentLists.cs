namespace SceneTests.GameObjects;

/// <summary>
/// Pins the ComponentList contract: enumeration order, reordering via Move, and the
/// FindMode search matrix across the hierarchy.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentListTest : SceneTest
{
	/// <summary>
	/// GetAll must enumerate components in creation order, stably.
	/// </summary>
	[TestMethod]
	public void GetAllPreservesCreationOrder()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var a = go.Components.Create<OrderedA>();
		var b = go.Components.Create<OrderedB>();
		var c = go.Components.Create<OrderedC>();

		CollectionAssert.AreEqual( new Component[] { a, b, c }, go.Components.GetAll().ToArray() );
	}

	/// <summary>
	/// Move shifts a component up or down the list by the given delta, and is a no-op
	/// for components that aren't in the list.
	/// </summary>
	[TestMethod]
	public void MoveReorders()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var a = go.Components.Create<OrderedA>();
		var b = go.Components.Create<OrderedB>();
		var c = go.Components.Create<OrderedC>();

		go.Components.Move( c, -2 );
		CollectionAssert.AreEqual( new Component[] { c, a, b }, go.Components.GetAll().ToArray() );

		go.Components.Move( c, 1 );
		CollectionAssert.AreEqual( new Component[] { a, c, b }, go.Components.GetAll().ToArray() );

		// Moving a foreign component is ignored
		var other = scene.CreateObject().Components.Create<OrderedA>();
		go.Components.Move( other, 1 );
		Assert.AreEqual( 3, go.Components.Count );
	}

	/// <summary>
	/// The default Get skips disabled components; FindMode.Disabled finds only them,
	/// and EverythingInSelf finds both.
	/// </summary>
	[TestMethod]
	public void EnabledDisabledFilters()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var enabled = go.Components.Create<OrderedA>();
		var disabled = go.Components.Create<OrderedB>();
		disabled.Enabled = false;

		Assert.AreEqual( enabled, go.Components.Get<OrderedA>() );
		Assert.IsNull( go.Components.Get<OrderedB>() );
		Assert.AreEqual( disabled, go.Components.Get<OrderedB>( includeDisabled: true ) );

		Assert.AreEqual( disabled, go.Components.Get<Component>( FindMode.DisabledInSelf ) );
		Assert.AreEqual( 2, go.Components.GetAll<Component>( FindMode.EverythingInSelf ).Count() );
	}

	/// <summary>
	/// The hierarchy search directions must look exactly where they say: parent vs
	/// ancestors, children vs descendants, with and without self.
	/// </summary>
	[TestMethod]
	public void HierarchySearchDirections()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var grandparent = scene.CreateObject();
		var gpComp = grandparent.Components.Create<OrderedA>();

		var parent = new GameObject( grandparent );
		var parentComp = parent.Components.Create<OrderedA>();

		var child = new GameObject( parent );
		var childComp = child.Components.Create<OrderedA>();

		var grandchild = new GameObject( child );
		var gcComp = grandchild.Components.Create<OrderedA>();

		// InParent looks one level up only
		Assert.AreEqual( parentComp, child.Components.GetInParent<OrderedA>() );

		// InAncestors finds the nearest ancestor, not self
		Assert.AreEqual( childComp, grandchild.Components.GetInAncestors<OrderedA>() );

		// InChildren looks one level down only
		Assert.AreEqual( childComp, parent.Components.GetInChildren<OrderedA>() );

		// InDescendants searches the whole subtree, not self
		Assert.AreEqual( parentComp, grandparent.Components.GetInDescendants<OrderedA>() );

		// OrSelf variants prefer self
		Assert.AreEqual( childComp, child.Components.GetInAncestorsOrSelf<OrderedA>() );
		Assert.AreEqual( childComp, child.Components.GetInDescendantsOrSelf<OrderedA>() );

		// GetAll over descendants returns every component in the subtree
		var all = grandparent.Components.GetAll<OrderedA>( FindMode.EnabledInSelfAndDescendants ).ToArray();
		Assert.AreEqual( 4, all.Length );
	}

	/// <summary>
	/// Hierarchy searches must respect enabled state: a disabled object's components
	/// aren't found unless disabled finds are requested.
	/// </summary>
	[TestMethod]
	public void HierarchySearchRespectsEnabledState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var child = new GameObject( parent );
		var childComp = child.Components.Create<OrderedA>();

		child.Enabled = false;

		Assert.IsNull( parent.Components.GetInDescendants<OrderedA>() );
		Assert.AreEqual( childComp, parent.Components.GetInDescendants<OrderedA>( includeDisabled: true ) );
	}

	/// <summary>
	/// TryGet returns whether the component was found and outputs it.
	/// </summary>
	[TestMethod]
	public void TryGet()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var a = go.Components.Create<OrderedA>();

		Assert.IsTrue( go.Components.TryGet<OrderedA>( out var found ) );
		Assert.AreEqual( a, found );

		Assert.IsFalse( go.Components.TryGet<OrderedB>( out var missing ) );
		Assert.IsNull( missing );
	}

	/// <summary>
	/// GetOrCreate returns the existing component when present and creates one when not.
	/// </summary>
	[TestMethod]
	public void GetOrCreate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var created = go.Components.GetOrCreate<OrderedA>();
		Assert.IsNotNull( created );
		Assert.AreEqual( 1, go.Components.Count );

		var found = go.Components.GetOrCreate<OrderedA>();
		Assert.AreEqual( created, found );
		Assert.AreEqual( 1, go.Components.Count );
	}

	/// <summary>
	/// Components are also addressable by their Guid.
	/// </summary>
	[TestMethod]
	public void GetById()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var a = go.Components.Create<OrderedA>();

		Assert.AreEqual( a, go.Components.Get( a.Id ) );
		Assert.IsNull( go.Components.Get( System.Guid.NewGuid() ) );
	}

	/// <summary>
	/// Move clamps at the list boundaries instead of wrapping or throwing.
	/// </summary>
	[TestMethod]
	public void MoveClampsAtBounds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var a = go.Components.Create<OrderedA>();
		var b = go.Components.Create<OrderedB>();
		var c = go.Components.Create<OrderedC>();

		go.Components.Move( a, -10 );
		CollectionAssert.AreEqual( new Component[] { a, b, c }, go.Components.GetAll().ToArray() );

		go.Components.Move( a, 10 );
		CollectionAssert.AreEqual( new Component[] { b, c, a }, go.Components.GetAll().ToArray() );
	}

	/// <summary>
	/// MoveToIndex swaps the component with whatever occupies the target index - it
	/// does not shift the elements between. Pinned as the editor reorder relies on it.
	/// </summary>
	[TestMethod]
	public void MoveToIndexSwaps()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var a = go.Components.Create<OrderedA>();
		var b = go.Components.Create<OrderedB>();
		var c = go.Components.Create<OrderedC>();

		go.Components.MoveToIndex( a, 2 );

		CollectionAssert.AreEqual( new Component[] { c, b, a }, go.Components.GetAll().ToArray() );
	}

	/// <summary>
	/// Event execution through non-default FindModes must reach the right relatives:
	/// the parent for InParent, the whole chain for InAncestors, and disabled
	/// components only when asked.
	/// </summary>
	[TestMethod]
	public void RunEventThroughHierarchyModes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var grandparent = scene.CreateObject();
		var gpReceiver = grandparent.Components.Create<BumpReceiver>();

		var parent = new GameObject( grandparent );
		var parentReceiver = parent.Components.Create<BumpReceiver>();

		var child = new GameObject( parent );
		var childReceiver = child.Components.Create<BumpReceiver>();

		// InParent reaches the direct parent only
		child.RunEvent<IBumpEvent>( x => x.Bump(), FindMode.Enabled | FindMode.InParent );
		Assert.AreEqual( 1, parentReceiver.Bumps );
		Assert.AreEqual( 0, gpReceiver.Bumps );
		Assert.AreEqual( 0, childReceiver.Bumps );

		// InAncestors reaches the whole chain upwards
		child.RunEvent<IBumpEvent>( x => x.Bump(), FindMode.Enabled | FindMode.InAncestors );
		Assert.AreEqual( 2, parentReceiver.Bumps );
		Assert.AreEqual( 1, gpReceiver.Bumps );
		Assert.AreEqual( 0, childReceiver.Bumps );

		// Disabled components are reached only when the find includes them
		childReceiver.Enabled = false;
		child.RunEvent<IBumpEvent>( x => x.Bump(), FindMode.Enabled | FindMode.InSelf );
		Assert.AreEqual( 0, childReceiver.Bumps );

		child.RunEvent<IBumpEvent>( x => x.Bump(), FindMode.Enabled | FindMode.Disabled | FindMode.InSelf );
		Assert.AreEqual( 1, childReceiver.Bumps );
	}

	public interface IBumpEvent
	{
		void Bump();
	}

	public class BumpReceiver : Component, IBumpEvent
	{
		public int Bumps;
		public void Bump() => Bumps++;
	}

	public class OrderedA : Component { }
	public class OrderedB : Component { }
	public class OrderedC : Component { }
}
