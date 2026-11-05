using Sandbox;
using System.Linq;

namespace SceneTests.GameObjects;

[TestClass]
[DoNotParallelize]
public class DestroyTest : SceneTest
{
	[TestMethod]
	public void Destroy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, scene.Directory.Count );

		go.Destroy();

		// nothing should change until next tick
		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, scene.Directory.Count );

		scene.GameTick();

		Assert.IsFalse( go.Enabled );
		Assert.IsFalse( go.Active );
		Assert.IsNull( go.Scene );
		Assert.IsNull( go.Parent );
		Assert.AreEqual( 0, scene.Children.Count() );
		Assert.AreEqual( 0, scene.Directory.Count );
	}

	[TestMethod]
	public void Destroy_Child()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var go = new GameObject();
		go.Parent = parent;

		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, parent.Children.Count() );
		Assert.AreEqual( 2, scene.Directory.Count );

		go.Destroy();

		// nothing should change until next tick
		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, parent.Children.Count() );
		Assert.AreEqual( 2, scene.Directory.Count );

		scene.GameTick();

		Assert.IsFalse( go.Enabled );
		Assert.IsFalse( go.Active );
		Assert.IsNull( go.Scene );
		Assert.IsNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 0, parent.Children.Count() );
		Assert.AreEqual( 1, scene.Directory.Count );
	}

	[TestMethod]
	public void Destroy_With_Child()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var go = new GameObject();
		go.Parent = parent;

		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.IsTrue( parent.Enabled );
		Assert.IsTrue( parent.Active );
		Assert.IsNotNull( parent.Scene );
		Assert.AreEqual( parent.Scene, scene );
		Assert.IsNotNull( parent.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, parent.Children.Count() );
		Assert.AreEqual( 2, scene.Directory.Count );

		parent.Destroy();

		// nothing should change until next tick
		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, parent.Children.Count() );
		Assert.AreEqual( 2, scene.Directory.Count );

		scene.GameTick();

		Assert.IsFalse( parent.Enabled );
		Assert.IsFalse( parent.Active );
		Assert.IsNull( parent.Scene );
		Assert.IsNull( parent.Parent );
		Assert.IsFalse( go.Enabled );
		Assert.IsFalse( go.Active );
		Assert.IsNull( go.Scene );
		Assert.IsNull( go.Parent );
		Assert.AreEqual( 0, scene.Children.Count() );
		Assert.AreEqual( 0, parent.Children.Count() );
		Assert.AreEqual( 0, scene.Directory.Count );
	}

	[TestMethod]
	public void Destroy_With_Child_Immediate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var go = new GameObject();
		go.Parent = parent;

		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.IsTrue( parent.Enabled );
		Assert.IsTrue( parent.Active );
		Assert.IsNotNull( parent.Scene );
		Assert.AreEqual( parent.Scene, scene );
		Assert.IsNotNull( parent.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, parent.Children.Count() );
		Assert.AreEqual( 2, scene.Directory.Count );

		parent.DestroyImmediate();

		Assert.IsFalse( parent.Enabled );
		Assert.IsFalse( parent.Active );
		Assert.IsNull( parent.Scene );
		Assert.IsNull( parent.Parent );
		Assert.IsFalse( go.Enabled );
		Assert.IsFalse( go.Active );
		Assert.IsNull( go.Scene );
		Assert.IsNull( go.Parent );
		Assert.AreEqual( 0, scene.Children.Count() );
		Assert.AreEqual( 0, parent.Children.Count() );
		Assert.AreEqual( 0, scene.Directory.Count );
	}

	[TestMethod]
	public void Destroy_Child_immediate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var go = new GameObject();
		go.Parent = parent;

		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, parent.Children.Count() );
		Assert.AreEqual( 2, scene.Directory.Count );

		go.DestroyImmediate();

		Assert.IsFalse( go.Enabled );
		Assert.IsFalse( go.Active );
		Assert.IsNull( go.Scene );
		Assert.IsNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 0, parent.Children.Count() );
		Assert.AreEqual( 1, scene.Directory.Count );

	}

	[TestMethod]
	public void Destroy_Immediate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, scene.Directory.Count );

		go.DestroyImmediate();

		Assert.IsFalse( go.Enabled );
		Assert.IsFalse( go.Active );
		Assert.IsNull( go.Scene );
		Assert.IsNull( go.Parent );
		Assert.AreEqual( 0, scene.Children.Count() );
		Assert.AreEqual( 0, scene.Directory.Count );
	}

	[TestMethod]
	public void Clear_Single()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var go = new GameObject();
		go.Parent = parent;

		Assert.IsTrue( go.Enabled );
		Assert.IsTrue( go.Active );
		Assert.IsNotNull( go.Scene );
		Assert.AreEqual( go.Scene, scene );
		Assert.IsNotNull( go.Parent );
		Assert.IsTrue( parent.Enabled );
		Assert.IsTrue( parent.Active );
		Assert.IsNotNull( parent.Scene );
		Assert.AreEqual( parent.Scene, scene );
		Assert.IsNotNull( parent.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 1, parent.Children.Count() );
		Assert.AreEqual( 2, scene.Directory.Count );

		parent.Clear();

		Assert.IsTrue( parent.Enabled );
		Assert.IsTrue( parent.Active );
		Assert.IsNotNull( parent.Scene );
		Assert.IsNotNull( parent.Parent );
		Assert.IsFalse( go.Enabled );
		Assert.IsFalse( go.Active );
		Assert.IsNull( go.Scene );
		Assert.IsNull( go.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 0, parent.Children.Count() );
		Assert.AreEqual( 1, scene.Directory.Count );
	}

	/// <summary>
	/// Removing children from the front, back and middle (by destroying or reparenting them) takes
	/// out exactly that child and leaves the remaining siblings in their original order.
	/// </summary>
	[TestMethod]
	public void Remove_Child_Keeps_Sibling_Order()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var other = scene.CreateObject();
		var children = Enumerable.Range( 0, 6 ).Select( i => new GameObject( parent, true, $"child {i}" ) ).ToArray();

		children[0].DestroyImmediate();
		children[5].DestroyImmediate();
		children[3].Parent = other;
		children[2].DestroyImmediate();

		CollectionAssert.AreEqual( new[] { children[1], children[4] }, parent.Children.ToArray() );
		CollectionAssert.AreEqual( new[] { children[3] }, other.Children.ToArray() );
		Assert.AreSame( other, children[3].Parent );
		Assert.IsTrue( children[3].IsValid );
	}

}
