namespace SceneTests.GameObjects;

/// <summary>
/// Pins the GameObject-level component convenience API (AddComponent,
/// GetOrAddComponent, GetComponents, the InChildren/InParent variants) which
/// wraps ComponentList with specific FindMode combinations.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GetComponentFamilyTest : SceneTest
{
	/// <summary>
	/// AddComponent creates the component, honouring the startEnabled flag.
	/// </summary>
	[TestMethod]
	public void AddComponentRespectsStartEnabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var enabled = go.AddComponent<FamilyProbeComponent>();
		Assert.IsTrue( enabled.Enabled );

		var disabled = go.AddComponent<FamilyProbeComponent>( false );
		Assert.IsFalse( disabled.Enabled );

		Assert.AreEqual( 2, go.Components.Count );
	}

	/// <summary>
	/// GetOrAddComponent returns an existing component - even a disabled one -
	/// instead of creating a duplicate, and creates one when none exists.
	/// </summary>
	[TestMethod]
	public void GetOrAddComponentReturnsExistingIncludingDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var created = go.GetOrAddComponent<FamilyProbeComponent>();
		Assert.IsNotNull( created );
		Assert.AreEqual( 1, go.Components.Count );

		Assert.AreEqual( created, go.GetOrAddComponent<FamilyProbeComponent>() );
		Assert.AreEqual( 1, go.Components.Count );

		created.Enabled = false;

		Assert.AreEqual( created, go.GetOrAddComponent<FamilyProbeComponent>(), "a disabled component still counts as existing" );
		Assert.AreEqual( 1, go.Components.Count );
	}

	/// <summary>
	/// GetComponent skips disabled components unless includeDisabled is passed.
	/// </summary>
	[TestMethod]
	public void GetComponentIncludeDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.AddComponent<FamilyProbeComponent>( false );

		Assert.IsNull( go.GetComponent<FamilyProbeComponent>() );
		Assert.AreEqual( comp, go.GetComponent<FamilyProbeComponent>( true ) );
	}

	/// <summary>
	/// GetComponents enumerates only this object's components, filtering
	/// disabled ones by default.
	/// </summary>
	[TestMethod]
	public void GetComponentsFiltersDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.AddComponent<FamilyProbeComponent>();
		go.AddComponent<FamilyProbeComponent>( false );

		var child = new GameObject( go );
		child.AddComponent<FamilyProbeComponent>();

		Assert.AreEqual( 1, go.GetComponents<FamilyProbeComponent>().Count() );
		Assert.AreEqual( 2, go.GetComponents<FamilyProbeComponent>( true ).Count() );
	}

	/// <summary>
	/// GetComponentsInChildren spans self plus all descendants, with includeSelf
	/// and includeDisabled narrowing/widening the search.
	/// </summary>
	[TestMethod]
	public void GetComponentsInChildrenFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var selfComp = go.AddComponent<FamilyProbeComponent>();

		var child = new GameObject( go );
		var childComp = child.AddComponent<FamilyProbeComponent>();

		var grandchild = new GameObject( child );
		var grandchildComp = grandchild.AddComponent<FamilyProbeComponent>();

		var disabledChild = new GameObject( go, false );
		var disabledComp = disabledChild.AddComponent<FamilyProbeComponent>();

		Assert.AreEqual( 3, go.GetComponentsInChildren<FamilyProbeComponent>().Count() );
		Assert.AreEqual( 2, go.GetComponentsInChildren<FamilyProbeComponent>( includeSelf: false ).Count() );
		Assert.AreEqual( 4, go.GetComponentsInChildren<FamilyProbeComponent>( includeDisabled: true ).Count() );

		Assert.AreEqual( selfComp, go.GetComponentInChildren<FamilyProbeComponent>(), "self is searched first" );
		Assert.AreEqual( childComp, go.GetComponentInChildren<FamilyProbeComponent>( includeSelf: false ) );
	}

	/// <summary>
	/// Despite the name, GetComponentsInParent searches all ancestors - a
	/// grandparent's component is found from a grandchild.
	/// </summary>
	[TestMethod]
	public void GetComponentsInParentSearchesAllAncestors()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var grandparent = scene.CreateObject();
		var gpComp = grandparent.AddComponent<FamilyProbeComponent>();

		var parent = new GameObject( grandparent );
		var parentComp = parent.AddComponent<FamilyProbeComponent>();

		var go = new GameObject( parent );
		var selfComp = go.AddComponent<FamilyProbeComponent>();

		Assert.AreEqual( 3, go.GetComponentsInParent<FamilyProbeComponent>().Count() );
		Assert.AreEqual( 2, go.GetComponentsInParent<FamilyProbeComponent>( includeSelf: false ).Count() );

		Assert.AreEqual( selfComp, go.GetComponentInParent<FamilyProbeComponent>() );
		Assert.AreEqual( parentComp, go.GetComponentInParent<FamilyProbeComponent>( includeSelf: false ) );

		var all = go.GetComponentsInParent<FamilyProbeComponent>( includeSelf: false ).ToList();
		CollectionAssert.Contains( all, gpComp );
	}

	/// <summary>
	/// The includeDisabled flag on the ancestor search picks up disabled
	/// components on the way up.
	/// </summary>
	[TestMethod]
	public void GetComponentInParentIncludeDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var disabledComp = parent.AddComponent<FamilyProbeComponent>( false );

		var go = new GameObject( parent );

		Assert.IsNull( go.GetComponentInParent<FamilyProbeComponent>() );
		Assert.AreEqual( disabledComp, go.GetComponentInParent<FamilyProbeComponent>( includeDisabled: true ) );
	}
}

/// <summary>
/// Bare component used by the GetComponent family tests.
/// </summary>
public class FamilyProbeComponent : Component
{
}
