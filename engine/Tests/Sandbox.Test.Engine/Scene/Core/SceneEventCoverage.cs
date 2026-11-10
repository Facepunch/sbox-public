using System;

namespace SceneTests.Core;

/// <summary>
/// Interface used to test scene event dispatch through Scene.RunEvent and the
/// ISceneEvent syntax sugar.
/// </summary>
public interface ICoreTestEvent : ISceneEvent<ICoreTestEvent>
{
	void OnPing();
}

/// <summary>
/// Component receiving <see cref="ICoreTestEvent"/>, counting how often it was hit and
/// optionally throwing to prove dispatch isolation.
/// </summary>
public class EventProbeComponent : Component, ICoreTestEvent
{
	public int Pings;
	public bool ThrowOnPing;

	public void OnPing()
	{
		Pings++;

		if ( ThrowOnPing )
			throw new InvalidOperationException( "event probe explosion" );
	}
}

/// <summary>
/// Pins scene-wide event dispatch: RunEvent reaches every enabled component
/// implementing the interface, exceptions are isolated per receiver, and the
/// ISceneEvent Post/PostToGameObject sugar routes correctly.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneEventCoverageTest : SceneTest
{
	/// <summary>
	/// RunEvent on the scene reaches all enabled components implementing the event
	/// interface, and skips disabled ones.
	/// </summary>
	[TestMethod]
	public void RunEventReachesEnabledComponents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject().Components.Create<EventProbeComponent>();
		var b = scene.CreateObject().Components.Create<EventProbeComponent>();

		b.Enabled = false;

		scene.RunEvent<ICoreTestEvent>( x => x.OnPing() );

		Assert.AreEqual( 1, a.Pings );
		Assert.AreEqual( 0, b.Pings );

		scene.Destroy();
	}

	/// <summary>
	/// An exception thrown by one event receiver is swallowed and doesn't stop the
	/// event reaching the other receivers - every receiver still gets the event.
	/// </summary>
	[TestMethod]
	public void RunEventIsolatesExceptions()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject().Components.Create<EventProbeComponent>();
		var b = scene.CreateObject().Components.Create<EventProbeComponent>();

		a.ThrowOnPing = true;
		b.ThrowOnPing = true;

		scene.RunEvent<ICoreTestEvent>( x => x.OnPing() );

		Assert.AreEqual( 1, a.Pings );
		Assert.AreEqual( 1, b.Pings );

		scene.Destroy();
	}

	/// <summary>
	/// The ISceneEvent.Post sugar dispatches to the active scene - the same receivers
	/// as calling Scene.RunEvent directly.
	/// </summary>
	[TestMethod]
	public void PostDispatchesToActiveScene()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var probe = scene.CreateObject().Components.Create<EventProbeComponent>();

		ICoreTestEvent.Post( x => x.OnPing() );

		Assert.AreEqual( 1, probe.Pings );

		scene.Destroy();
	}

	/// <summary>
	/// PostToGameObject scopes the event to the target object and its descendants -
	/// siblings elsewhere in the scene are not hit.
	/// </summary>
	[TestMethod]
	public void PostToGameObjectScopesToDescendants()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var onParent = parent.Components.Create<EventProbeComponent>();

		var child = new GameObject( parent );
		var onChild = child.Components.Create<EventProbeComponent>();

		var onSibling = scene.CreateObject().Components.Create<EventProbeComponent>();

		ICoreTestEvent.PostToGameObject( parent, x => x.OnPing() );

		Assert.AreEqual( 1, onParent.Pings );
		Assert.AreEqual( 1, onChild.Pings );
		Assert.AreEqual( 0, onSibling.Pings );

		scene.Destroy();
	}
}
