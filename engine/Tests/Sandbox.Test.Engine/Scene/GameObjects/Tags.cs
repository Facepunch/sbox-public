namespace SceneTests.GameObjects;

[TestClass]
[DoNotParallelize]
public class TagsTest : SceneTest
{
	[TestMethod]
	public void TagsGetAddedAmdRemoved()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.AreEqual( 0, go.Tags.TryGetAll().Count() );

		go.Tags.Add( "poop" );

		Assert.AreEqual( 1, go.Tags.TryGetAll().Count() );
		Assert.IsTrue( go.Tags.Has( "poop" ) );
		Assert.IsTrue( go.Tags.Has( "POOP" ) );
		Assert.IsTrue( go.Tags.Has( "pOop" ) );
		Assert.IsTrue( go.Tags.Has( "pooP" ) );
		Assert.IsFalse( go.Tags.Has( "sock" ) );

		go.Tags.Add( "sock" );

		Assert.AreEqual( 2, go.Tags.TryGetAll().Count() );
		Assert.IsTrue( go.Tags.Has( "sock" ) );
		Assert.IsTrue( go.Tags.Has( "POOP" ) );

		go.Tags.Remove( "poop" );

		Assert.AreEqual( 1, go.Tags.TryGetAll().Count() );
		Assert.IsTrue( go.Tags.Has( "sock" ) );
		Assert.IsFalse( go.Tags.Has( "POOP" ) );

		go.Tags.Remove( "sock" );

		Assert.AreEqual( 0, go.Tags.TryGetAll().Count() );
		Assert.IsFalse( go.Tags.Has( "sock" ) );
		Assert.IsFalse( go.Tags.Has( "POOP" ) );
	}

	[TestMethod]
	public void ChildrenTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var child = scene.CreateObject();
		child.Parent = go;

		Assert.AreEqual( 0, go.Tags.TryGetAll().Count() );

		go.Tags.Add( "poop" );

		Assert.AreEqual( 1, child.Tags.TryGetAll().Count() );
		Assert.IsTrue( child.Tags.Has( "poop" ) );
		Assert.IsTrue( child.Tags.Has( "POOP" ) );
		Assert.IsTrue( child.Tags.Has( "pOop" ) );
		Assert.IsTrue( child.Tags.Has( "pooP" ) );
		Assert.IsFalse( child.Tags.Has( "pooP", false ) );
		Assert.IsTrue( go.Tags.Has( "pooP", false ) );
		Assert.IsTrue( go.Tags.Has( "pooP", true ) );
	}

	[TestMethod]
	public void GrandChildrenTags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var child = scene.CreateObject();
		child.Parent = go;

		var grandchild = scene.CreateObject();
		grandchild.Parent = child;

		Assert.AreEqual( 0, go.Tags.TryGetAll().Count() );

		go.Tags.Add( "poop" );

		Assert.AreEqual( 1, grandchild.Tags.TryGetAll().Count() );
		Assert.IsTrue( grandchild.Tags.Has( "poop" ) );
		Assert.IsTrue( grandchild.Tags.Has( "POOP" ) );
		Assert.IsTrue( grandchild.Tags.Has( "pOop" ) );
		Assert.IsTrue( grandchild.Tags.Has( "pooP" ) );
		Assert.IsFalse( grandchild.Tags.Has( "pooP", false ) );
		Assert.IsTrue( go.Tags.Has( "pooP", false ) );
		Assert.IsTrue( go.Tags.Has( "pooP", true ) );
	}

	[TestMethod]
	public void ComponentCallback()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var tc = go.Components.Create<TagsTestComponent>();
		Assert.AreEqual( 0, tc.TagUpdateCalls );

		scene.GameTick();

		Assert.AreEqual( 0, tc.TagUpdateCalls );

		go.Tags.Add( "poop" );

		Assert.AreEqual( 1, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 1, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 1, tc.TagUpdateCalls );

		go.Tags.Remove( "sock" );
		Assert.AreEqual( 1, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 1, tc.TagUpdateCalls );

		go.Tags.Remove( "poop" );
		Assert.AreEqual( 2, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 2, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 2, tc.TagUpdateCalls );
	}

	/// <summary>
	/// The counter component is on a child, should act exactly the same
	/// </summary>
	[TestMethod]
	public void ComponentCallback_Children()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var child = scene.CreateObject();
		child.Parent = go;
		var tc = child.Components.Create<TagsTestComponent>();

		Assert.AreEqual( 0, tc.TagUpdateCalls );

		scene.GameTick();

		Assert.AreEqual( 0, tc.TagUpdateCalls );

		go.Tags.Add( "poop" );

		Assert.AreEqual( 1, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 1, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 1, tc.TagUpdateCalls );

		go.Tags.Remove( "sock" );
		Assert.AreEqual( 1, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 1, tc.TagUpdateCalls );

		go.Tags.Remove( "poop" );
		Assert.AreEqual( 2, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 2, tc.TagUpdateCalls );
		scene.GameTick();
		Assert.AreEqual( 2, tc.TagUpdateCalls );
	}

}

public class TagsTestComponent : Component
{
	public int TagUpdateCalls;

	protected override void OnTagsChanged()
	{
		TagUpdateCalls++;
	}
}
