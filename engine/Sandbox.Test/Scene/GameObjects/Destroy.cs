using Sandbox;
using System.Linq;

namespace GameObjects;

[TestClass]
[DoNotParallelize]
public class DestroyTests
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

	[TestMethod]
	public void Clear_Deep()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();

		Assert.AreEqual( 1, scene.Directory.Count );

		for ( int i = 0; i < 16; i++ )
		{
			var go = new GameObject();
			go.Parent = parent;

			go.Components.Create<ModelRenderer>();

			for ( int j = 0; j < 8; j++ )
			{
				var go2 = new GameObject();
				go2.Parent = go;
				go2.Components.Create<ModelRenderer>();

				for ( int k = 0; k < 8; k++ )
				{
					var go3 = new GameObject();
					go3.Parent = go;
					go3.Components.Create<ModelRenderer>();
				}
			}
		}

		Assert.IsTrue( parent.Enabled );
		Assert.IsTrue( parent.Active );
		Assert.IsNotNull( parent.Scene );
		Assert.AreEqual( parent.Scene, scene );
		Assert.IsNotNull( parent.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 16, parent.Children.Count() );
		Assert.AreEqual( 1168, scene.SceneWorld.SceneObjects.Count() );
		Assert.AreEqual( 1169, scene.Directory.Count );

		parent.Clear();

		Assert.IsTrue( parent.Enabled );
		Assert.IsTrue( parent.Active );
		Assert.IsNotNull( parent.Scene );
		Assert.IsNotNull( parent.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 0, scene.SceneWorld.SceneObjects.Count() );
		Assert.AreEqual( 0, parent.Children.Count() );
		Assert.AreEqual( 1, scene.Directory.Count );
	}

	[TestMethod]
	public void Clear_Scene()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();

		Assert.AreEqual( 1, scene.Directory.Count );

		for ( int i = 0; i < 16; i++ )
		{
			var go = new GameObject();
			go.Parent = parent;

			go.Components.Create<ModelRenderer>();

			for ( int j = 0; j < 8; j++ )
			{
				var go2 = new GameObject();
				go2.Parent = go;
				go2.Components.Create<ModelRenderer>();

				for ( int k = 0; k < 8; k++ )
				{
					var go3 = new GameObject();
					go3.Parent = go;
					go3.Components.Create<ModelRenderer>();
				}
			}
		}

		Assert.IsTrue( parent.Enabled );
		Assert.IsTrue( parent.Active );
		Assert.IsNotNull( parent.Scene );
		Assert.AreEqual( parent.Scene, scene );
		Assert.IsNotNull( parent.Parent );
		Assert.AreEqual( 1, scene.Children.Count() );
		Assert.AreEqual( 16, parent.Children.Count() );
		Assert.AreEqual( 1168, scene.SceneWorld.SceneObjects.Count() );
		Assert.AreEqual( 1169, scene.Directory.Count );

		scene.Clear();

		Assert.IsFalse( parent.Enabled );
		Assert.IsFalse( parent.Active );
		Assert.IsNull( parent.Scene );
		Assert.IsNull( parent.Parent );
		Assert.AreEqual( 0, scene.Children.Count() );
		Assert.AreEqual( 0, scene.SceneWorld.SceneObjects.Count() );
		Assert.AreEqual( 0, parent.Children.Count() );
		Assert.AreEqual( 0, scene.Directory.Count );
	}
}
