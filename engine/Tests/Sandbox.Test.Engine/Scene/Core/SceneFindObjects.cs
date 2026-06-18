using System.Collections.Generic;

namespace SceneTests.Core;

/// <summary>
/// Marker interface so tests can prove components are findable through the typed
/// object index by interface as well as by class.
/// </summary>
public interface ICountableProbe
{
}

/// <summary>
/// Component indexed by the scene's typed object index in these tests.
/// </summary>
public class FindProbeComponent : Component, ICountableProbe
{
}

/// <summary>
/// Derived component, used to prove base-class lookups find subclasses.
/// </summary>
public class FindProbeDerivedComponent : FindProbeComponent
{
}

/// <summary>
/// Pins the scene's typed object index: GetAll/Get/GetAllComponents lookups by class,
/// base class and interface, and how enable/disable/destroy move components in and out
/// of the index.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneFindObjectsTest : SceneTest
{
	/// <summary>
	/// GetAll finds components by their exact type, their base type and their
	/// interfaces - the index covers the whole type hierarchy.
	/// </summary>
	[TestMethod]
	public void GetAllFindsByClassBaseAndInterface()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var baseComp = scene.CreateObject().Components.Create<FindProbeComponent>();
		var derivedComp = scene.CreateObject().Components.Create<FindProbeDerivedComponent>();

		Assert.AreEqual( 2, scene.GetAll<FindProbeComponent>().Count() );
		Assert.AreEqual( 1, scene.GetAll<FindProbeDerivedComponent>().Count() );
		Assert.AreEqual( 2, scene.GetAll<ICountableProbe>().Count() );

		CollectionAssert.Contains( scene.GetAll<FindProbeComponent>().ToList(), derivedComp );
		CollectionAssert.Contains( scene.GetAll<ICountableProbe>().ToList(), baseComp );

		scene.Destroy();
	}

	/// <summary>
	/// The list-filling GetAll overload appends the same results that the enumerable
	/// version yields.
	/// </summary>
	[TestMethod]
	public void GetAllListOverloadFillsTarget()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		scene.CreateObject().Components.Create<FindProbeComponent>();
		scene.CreateObject().Components.Create<FindProbeComponent>();

		var target = new List<FindProbeComponent>();
		scene.GetAll( target );

		Assert.AreEqual( 2, target.Count );

		// An empty index leaves the target untouched
		var none = new List<FindProbeDerivedComponent>();
		scene.GetAll( none );
		Assert.AreEqual( 0, none.Count );

		scene.Destroy();
	}

	/// <summary>
	/// Get returns a single instance when one exists and default when the index has
	/// nothing of that type.
	/// </summary>
	[TestMethod]
	public void GetReturnsFirstOrDefault()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.IsNull( scene.Get<FindProbeComponent>() );

		var comp = scene.CreateObject().Components.Create<FindProbeComponent>();

		Assert.AreEqual( comp, scene.Get<FindProbeComponent>() );
		Assert.AreEqual( comp, scene.Get<ICountableProbe>() as FindProbeComponent );

		scene.Destroy();
	}

	/// <summary>
	/// GetAllComponents - both the generic and the Type-argument overload - only
	/// returns enabled components.
	/// </summary>
	[TestMethod]
	public void GetAllComponentsHonorsTypeArgument()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var comp = scene.CreateObject().Components.Create<FindProbeComponent>();

		Assert.AreEqual( 1, scene.GetAllComponents<FindProbeComponent>().Count() );
		Assert.AreEqual( 1, scene.GetAllComponents( typeof( FindProbeComponent ) ).Count() );
		Assert.AreEqual( 1, scene.GetAllComponents( typeof( ICountableProbe ) ).Count() );
		Assert.AreEqual( comp, scene.GetAllComponents( typeof( FindProbeComponent ) ).First() );

		scene.Destroy();
	}

	/// <summary>
	/// Disabling a component removes it from the index; re-enabling adds it back. The
	/// index only ever returns active components.
	/// </summary>
	[TestMethod]
	public void DisabledComponentsLeaveTheIndex()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var comp = scene.CreateObject().Components.Create<FindProbeComponent>();

		Assert.AreEqual( 1, scene.GetAll<FindProbeComponent>().Count() );

		comp.Enabled = false;
		Assert.AreEqual( 0, scene.GetAll<FindProbeComponent>().Count() );
		Assert.AreEqual( 0, scene.GetAll<ICountableProbe>().Count() );

		comp.Enabled = true;
		Assert.AreEqual( 1, scene.GetAll<FindProbeComponent>().Count() );

		scene.Destroy();
	}

	/// <summary>
	/// A destroyed component disappears from the index immediately - callers never see
	/// half-dead components, even before the deferred deletes are processed.
	/// </summary>
	[TestMethod]
	public void DestroyedComponentsAreNotReturned()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var comp = scene.CreateObject().Components.Create<FindProbeComponent>();
		comp.Destroy();

		Assert.AreEqual( 0, scene.GetAll<FindProbeComponent>().Count() );

		scene.ProcessDeletes();

		Assert.AreEqual( 0, scene.GetAll<FindProbeComponent>().Count() );

		scene.Destroy();
	}

	/// <summary>
	/// Disabling the owning GameObject removes its components from the index too -
	/// component activity follows the object hierarchy.
	/// </summary>
	[TestMethod]
	public void DisablingGameObjectRemovesComponents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<FindProbeComponent>();

		go.Enabled = false;
		Assert.AreEqual( 0, scene.GetAll<FindProbeComponent>().Count() );

		go.Enabled = true;
		Assert.AreEqual( 1, scene.GetAll<FindProbeComponent>().Count() );

		scene.Destroy();
	}
}
