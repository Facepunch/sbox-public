namespace SceneTests.Core;

/// <summary>
/// Pins RunEvent dispatch: events reach enabled components implementing the
/// interface, respect the FindMode, and GameObject.RunEvent scopes to the subtree.
/// </summary>
[TestClass]
public class SceneEventTest
{
	public interface IPokeEvent
	{
		void Poke();
	}

	public class PokeReceiver : Component, IPokeEvent
	{
		public int Pokes;
		public void Poke() => Pokes++;
	}

	/// <summary>
	/// Scene.RunEvent reaches every enabled component implementing the interface,
	/// and skips disabled ones by default.
	/// </summary>
	[TestMethod]
	public void SceneWideDispatch()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var enabled = scene.CreateObject().Components.Create<PokeReceiver>();
		var disabled = scene.CreateObject().Components.Create<PokeReceiver>();
		disabled.Enabled = false;

		scene.RunEvent<IPokeEvent>( x => x.Poke() );

		Assert.AreEqual( 1, enabled.Pokes );
		Assert.AreEqual( 0, disabled.Pokes );
	}

	/// <summary>
	/// Scene.RunEvent documents that the FindMode argument is unused - disabled
	/// components are never reached scene-wide. GameObject.RunEvent honors the
	/// FindMode, so a subtree dispatch can include disabled components.
	/// </summary>
	[TestMethod]
	public void DispatchWithDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var disabled = new GameObject( root ).Components.Create<PokeReceiver>();
		disabled.Enabled = false;

		// Scene-wide: the find argument is unused, disabled stays unreached
		scene.RunEvent<IPokeEvent>( x => x.Poke(),
			FindMode.Enabled | FindMode.Disabled | FindMode.InSelf | FindMode.InDescendants );
		Assert.AreEqual( 0, disabled.Pokes );

		// Subtree dispatch honors the FindMode
		root.RunEvent<IPokeEvent>( x => x.Poke(),
			FindMode.Enabled | FindMode.Disabled | FindMode.InSelf | FindMode.InDescendants );
		Assert.AreEqual( 1, disabled.Pokes );
	}

	/// <summary>
	/// GameObject.RunEvent only dispatches within that object's subtree.
	/// </summary>
	[TestMethod]
	public void SubtreeDispatch()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var inside = new GameObject( root ).Components.Create<PokeReceiver>();
		var outside = scene.CreateObject().Components.Create<PokeReceiver>();

		root.RunEvent<IPokeEvent>( x => x.Poke() );

		Assert.AreEqual( 1, inside.Pokes );
		Assert.AreEqual( 0, outside.Pokes );
	}
}
