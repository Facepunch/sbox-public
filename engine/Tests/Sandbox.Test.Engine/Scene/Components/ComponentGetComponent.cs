namespace SceneTests.Components;

/// <summary>
/// Tests for the GetComponent family of helpers on <see cref="Component"/>
/// (Component.GetComponent.cs). These are shortcuts that search the component's
/// own GameObject and, depending on the call, its ancestors or descendants.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentGetComponentTest : SceneTest
{
	/// <summary>
	/// AddComponent on a component attaches the new component to the same
	/// GameObject, and honours the startEnabled argument.
	/// </summary>
	[TestMethod]
	public void AddComponentCreatesOnOwnGameObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var alpha = go.Components.Create<GetAlphaComponent>();

		var beta = alpha.AddComponent<GetBetaComponent>();

		Assert.IsNotNull( beta );
		Assert.AreSame( go, beta.GameObject );
		Assert.IsTrue( beta.Enabled );

		var disabledBeta = alpha.AddComponent<GetBetaComponent>( false );

		Assert.AreSame( go, disabledBeta.GameObject );
		Assert.IsFalse( disabledBeta.Enabled );
	}

	/// <summary>
	/// GetOrAddComponent returns an existing component instead of creating a
	/// duplicate - even when the existing component is disabled.
	/// </summary>
	[TestMethod]
	public void GetOrAddComponentReturnsExistingEvenWhenDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var alpha = go.Components.Create<GetAlphaComponent>();
		var beta = go.Components.Create<GetBetaComponent>( false );

		var fetched = alpha.GetOrAddComponent<GetBetaComponent>();

		Assert.AreSame( beta, fetched );
		Assert.AreEqual( 1, go.Components.GetAll<GetBetaComponent>( FindMode.EverythingInSelf ).Count() );
	}

	/// <summary>
	/// GetOrAddComponent creates the component when none exists, and a second
	/// call returns that same instance.
	/// </summary>
	[TestMethod]
	public void GetOrAddComponentCreatesWhenMissing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var alpha = go.Components.Create<GetAlphaComponent>();

		var created = alpha.GetOrAddComponent<GetBetaComponent>();

		Assert.IsNotNull( created );
		Assert.AreSame( go, created.GameObject );

		Assert.AreSame( created, alpha.GetOrAddComponent<GetBetaComponent>() );
	}

	/// <summary>
	/// GetComponent skips disabled components by default, and finds them when
	/// includeDisabled is passed.
	/// </summary>
	[TestMethod]
	public void GetComponentHonoursIncludeDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var alpha = go.Components.Create<GetAlphaComponent>();
		var beta = go.Components.Create<GetBetaComponent>( false );

		Assert.IsNull( alpha.GetComponent<GetBetaComponent>() );
		Assert.AreSame( beta, alpha.GetComponent<GetBetaComponent>( true ) );
	}

	/// <summary>
	/// GetComponents only searches the component's own GameObject - children are
	/// never included - and only includes disabled components when asked to.
	/// </summary>
	[TestMethod]
	public void GetComponentsSearchesSelfOnly()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var alpha = go.Components.Create<GetAlphaComponent>();
		go.Components.Create<GetBetaComponent>();
		go.Components.Create<GetBetaComponent>( false );

		var child = new GameObject( go, name: "Child" );
		child.Components.Create<GetBetaComponent>();

		Assert.AreEqual( 1, alpha.GetComponents<GetBetaComponent>().Count() );
		Assert.AreEqual( 2, alpha.GetComponents<GetBetaComponent>( true ).Count() );
	}

	/// <summary>
	/// GetComponentsInChildren walks all descendants, optionally including the
	/// component's own GameObject and disabled components.
	/// </summary>
	[TestMethod]
	public void GetComponentsInChildrenHonoursFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var alpha = go.Components.Create<GetAlphaComponent>();
		go.Components.Create<GetBetaComponent>();

		var child = new GameObject( go, name: "Child" );
		child.Components.Create<GetBetaComponent>();

		var grandChild = new GameObject( child, name: "GrandChild" );
		grandChild.Components.Create<GetBetaComponent>( false );

		// self + child, the disabled grandchild component is skipped
		Assert.AreEqual( 2, alpha.GetComponentsInChildren<GetBetaComponent>().Count() );

		// child only
		Assert.AreEqual( 1, alpha.GetComponentsInChildren<GetBetaComponent>( includeSelf: false ).Count() );

		// self + child + disabled grandchild
		Assert.AreEqual( 3, alpha.GetComponentsInChildren<GetBetaComponent>( includeDisabled: true ).Count() );

		// the single version finds one too
		Assert.IsNotNull( alpha.GetComponentInChildren<GetBetaComponent>() );
		Assert.IsNull( alpha.GetComponentInChildren<GetAlphaComponent>( includeSelf: false ) );
	}

	/// <summary>
	/// GetComponentInParent prefers a component on the own GameObject when
	/// includeSelf is true, and skips to the ancestors when it's false.
	/// GetComponentsInParent collects from the whole ancestor chain.
	/// </summary>
	[TestMethod]
	public void GetComponentInParentHonoursIncludeSelf()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var parentAlpha = parent.Components.Create<GetAlphaComponent>();
		parent.Components.Create<GetAlphaComponent>( false );

		var self = new GameObject( parent, name: "Self" );
		var selfAlpha = self.Components.Create<GetAlphaComponent>();
		var anchor = self.Components.Create<GetBetaComponent>();

		Assert.AreSame( selfAlpha, anchor.GetComponentInParent<GetAlphaComponent>() );
		Assert.AreSame( parentAlpha, anchor.GetComponentInParent<GetAlphaComponent>( includeSelf: false ) );

		// self enabled + parent enabled
		Assert.AreEqual( 2, anchor.GetComponentsInParent<GetAlphaComponent>().Count() );

		// parent enabled + parent disabled
		Assert.AreEqual( 2, anchor.GetComponentsInParent<GetAlphaComponent>( includeDisabled: true, includeSelf: false ).Count() );
	}
}

/// <summary>
/// Plain component used as a search target in GetComponent tests.
/// </summary>
public class GetAlphaComponent : Component
{
}

/// <summary>
/// Second plain component type used as a search target in GetComponent tests.
/// </summary>
public class GetBetaComponent : Component
{
}
