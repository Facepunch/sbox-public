using Sandbox;
using System;
using System.Linq;

namespace SceneTests.GameObjects;

[TestClass]
[DoNotParallelize]
public class ComponentGetTest : SceneTest
{
	GameObject CreateTestObject( GameObject parent, string name )
	{
		var go = new GameObject( true, name );
		go.Components.Create<OrderTestComponent>();
		go.Components.Create<OrderTestComponent>( false );
		go.Parent = parent;
		return go;
	}

	Scene GetTestScene()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateTestObject( scene, "One" );

		var two = CreateTestObject( scene, "Two" );
		{
			CreateTestObject( two, "Two.One" );
			var twotwo = CreateTestObject( two, "Two.Two" );
			{
				CreateTestObject( twotwo, "Two.Two.One" );
				CreateTestObject( twotwo, "Two.Two.Two" );
				CreateTestObject( twotwo, "Two.Two.Three" );
				CreateTestObject( twotwo, "Two.Two.Four" );
			}
			CreateTestObject( two, "Two.Three" );
			CreateTestObject( two, "Two.Four" );
		}

		CreateTestObject( scene, "Three" );
		CreateTestObject( scene, "Four" );

		return scene;
	}

	void CheckSelf( GameObject go )
	{
		// enabled only
		Assert.AreEqual( 1, go.Components.GetAll<Component>( FindMode.EnabledInSelf ).Count() );
		Assert.IsTrue( go.Components.GetAll<Component>( FindMode.EnabledInSelf ).All( x => x.Enabled ) );
		Assert.IsTrue( go.Components.GetAll<Component>( FindMode.EnabledInSelf ).All( x => x.GameObject == go ) );

		// disabled only
		Assert.AreEqual( 1, go.Components.GetAll<Component>( FindMode.DisabledInSelf ).Count() );
		Assert.IsTrue( go.Components.GetAll<Component>( FindMode.DisabledInSelf ).All( x => !x.Enabled ) );
		Assert.IsTrue( go.Components.GetAll<Component>( FindMode.DisabledInSelf ).All( x => x.GameObject == go ) );

		// enabled or disabled
		Assert.AreEqual( 2, go.Components.GetAll<Component>( FindMode.Enabled | FindMode.InSelf | FindMode.Disabled ).Count() );
		Assert.IsTrue( go.Components.GetAll<Component>( FindMode.Enabled | FindMode.InSelf | FindMode.Disabled ).All( x => x.GameObject == go ) );

		// no enabled or disabled means the same as both
		Assert.AreEqual( 2, go.Components.GetAll<Component>( FindMode.InSelf ).Count() );
		Assert.IsTrue( go.Components.GetAll<Component>( FindMode.InSelf ).All( x => x.GameObject == go ) );
	}

	[TestMethod]
	public void EnabledDisabled()
	{
		var scene = GetTestScene();

		var Two = scene.Children.First( x => x.Name == "Two" );

		CheckSelf( Two );
	}

	[TestMethod]
	public void InParent()
	{
		var scene = GetTestScene();

		var Two = scene.Children.First( x => x.Name == "Two" );
		var TwoTwo = Two.Children.First( x => x.Name == "Two.Two" );

		CheckSelf( TwoTwo );

		// In parent only
		Assert.AreEqual( 2, TwoTwo.Components.GetAll( FindMode.InParent ).Count() );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InParent ).All( x => x.GameObject == Two ) );

		// In parent enabled only
		Assert.AreEqual( 1, TwoTwo.Components.GetAll( FindMode.InParent | FindMode.Enabled ).Count() );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InParent | FindMode.Enabled ).All( x => x.Enabled ) );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InParent | FindMode.Enabled ).All( x => x.GameObject == Two ) );

		// In parent disabled only
		Assert.AreEqual( 1, TwoTwo.Components.GetAll( FindMode.InParent | FindMode.Disabled ).Count() );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InParent | FindMode.Disabled ).All( x => !x.Enabled ) );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InParent | FindMode.Disabled ).All( x => x.GameObject == Two ) );
	}

	[TestMethod]
	public void InChildren()
	{
		var scene = GetTestScene();

		var Two = scene.Children.First( x => x.Name == "Two" );
		var TwoTwo = Two.Children.First( x => x.Name == "Two.Two" );

		CheckSelf( TwoTwo );

		// In children only
		Assert.AreEqual( 8, TwoTwo.Components.GetAll( FindMode.InChildren ).Count() );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InChildren ).All( x => x.GameObject.Parent == TwoTwo ) );

		// In children enabled only
		Assert.AreEqual( 4, TwoTwo.Components.GetAll( FindMode.InChildren | FindMode.Enabled ).Count() );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InChildren | FindMode.Enabled ).All( x => x.Enabled ) );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InChildren | FindMode.Enabled ).All( x => x.GameObject.Parent == TwoTwo ) );

		// In children disabled only
		Assert.AreEqual( 4, TwoTwo.Components.GetAll( FindMode.InChildren | FindMode.Disabled ).Count() );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InChildren | FindMode.Disabled ).All( x => !x.Enabled ) );
		Assert.IsTrue( TwoTwo.Components.GetAll( FindMode.InChildren | FindMode.Disabled ).All( x => x.GameObject.Parent == TwoTwo ) );
	}

	[TestMethod]
	public void InDescendants()
	{
		var scene = GetTestScene();

		var Two = scene.Children.First( x => x.Name == "Two" );
		var TwoTwo = Two.Children.First( x => x.Name == "Two.Two" );

		CheckSelf( Two );
		CheckSelf( TwoTwo );

		// In descendants only
		Assert.AreEqual( 16, Two.Components.GetAll( FindMode.InDescendants ).Count() );
		Assert.IsTrue( Two.Components.GetAll( FindMode.InDescendants ).All( x => x.GameObject.Parent == Two || x.GameObject.Parent.Parent == Two ) );

		// In descendants enabled only
		Assert.AreEqual( 8, Two.Components.GetAll( FindMode.InDescendants | FindMode.Enabled ).Count() );
		Assert.IsTrue( Two.Components.GetAll( FindMode.InDescendants | FindMode.Enabled ).All( x => x.Enabled ) );
		Assert.IsTrue( Two.Components.GetAll( FindMode.InDescendants | FindMode.Enabled ).All( x => x.GameObject.Parent == Two || x.GameObject.Parent.Parent == Two ) );

		// In descendants disabled only
		Assert.AreEqual( 8, Two.Components.GetAll( FindMode.InDescendants | FindMode.Disabled ).Count() );
		Assert.IsTrue( Two.Components.GetAll( FindMode.InDescendants | FindMode.Disabled ).All( x => !x.Enabled ) );
		Assert.IsTrue( Two.Components.GetAll( FindMode.InDescendants | FindMode.Disabled ).All( x => x.GameObject.Parent == Two || x.GameObject.Parent.Parent == Two ) );
	}

	[TestMethod]
	public void InAncestors()
	{
		var scene = GetTestScene();

		var Two = scene.Children.First( x => x.Name == "Two" );
		var TwoTwo = Two.Children.First( x => x.Name == "Two.Two" );
		var TwoTwoTwo = TwoTwo.Children.First( x => x.Name == "Two.Two.Two" );

		CheckSelf( Two );
		CheckSelf( TwoTwo );
		CheckSelf( TwoTwoTwo );

		// In descendants only
		Assert.AreEqual( 4, TwoTwoTwo.Components.GetAll( FindMode.InAncestors ).Count() );
		Assert.IsTrue( TwoTwoTwo.Components.GetAll( FindMode.InAncestors ).All( x => x.GameObject == Two || x.GameObject == TwoTwo ) );

		// In descendants enabled only
		Assert.AreEqual( 2, TwoTwoTwo.Components.GetAll( FindMode.InAncestors | FindMode.Enabled ).Count() );
		Assert.IsTrue( TwoTwoTwo.Components.GetAll( FindMode.InAncestors | FindMode.Enabled ).All( x => x.Enabled ) );
		Assert.IsTrue( TwoTwoTwo.Components.GetAll( FindMode.InAncestors | FindMode.Enabled ).All( x => x.GameObject == Two || x.GameObject == TwoTwo ) );

		// In descendants disabled only
		Assert.AreEqual( 2, TwoTwoTwo.Components.GetAll( FindMode.InAncestors | FindMode.Disabled ).Count() );
		Assert.IsTrue( TwoTwoTwo.Components.GetAll( FindMode.InAncestors | FindMode.Disabled ).All( x => !x.Enabled ) );
		Assert.IsTrue( TwoTwoTwo.Components.GetAll( FindMode.InAncestors | FindMode.Disabled ).All( x => x.GameObject == Two || x.GameObject == TwoTwo ) );
	}
}
