using System;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace SceneTests.Components;

/// <summary>
/// MapInstance subscribes to the static SceneMap.OnMapUpdated so Hammer compiles can hot reload it.
/// That subscription has to stay balanced, or the static delegate keeps the component and its whole
/// scene alive after the scene is destroyed (editor play scenes leaked a full map every Play/Stop).
/// </summary>
[TestClass]
public class MapInstanceTests
{
	Action<string> _previousMapUpdated;

	[TestInitialize]
	public void Setup()
	{
		_previousMapUpdated = SceneMap.OnMapUpdated;
	}

	[TestCleanup]
	public void Cleanup()
	{
		SceneMap.OnMapUpdated = _previousMapUpdated;
	}

	static int SubscriptionCount( MapInstance instance )
	{
		return SceneMap.OnMapUpdated?.GetInvocationList().Count( x => ReferenceEquals( x.Target, instance ) ) ?? 0;
	}

	static bool Load( MapInstance instance, string mapName )
	{
		var method = typeof( MapInstance ).GetMethod( "LoadMapAsync", BindingFlags.NonPublic | BindingFlags.Instance, [typeof( string ), typeof( LoadingContext )] );
		var task = (Task<bool>)method.Invoke( instance, [mapName, null] );

		var deadline = System.Diagnostics.Stopwatch.StartNew();
		while ( !task.IsCompleted && deadline.ElapsedMilliseconds < 5000 )
		{
			Sandbox.Tasks.SyncContext.MainThread.ProcessQueue();
			Thread.Sleep( 1 );
		}

		Assert.IsTrue( task.IsCompleted, "map load should finish" );
		return task.Result;
	}

	/// <summary>
	/// Scene maps and failed loads never set a vpk SceneMap, so unloading has to unsubscribe regardless,
	/// and repeated loads mustn't stack handlers.
	/// </summary>
	[TestMethod]
	public void FailedSceneMapLoadsDontLeakSubscription()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var instance = go.Components.Create<MapInstance>();

		Assert.IsFalse( Load( instance, "maps/does_not_exist.scene" ) );
		Assert.AreEqual( 1, SubscriptionCount( instance ) );

		Assert.IsFalse( Load( instance, "maps/does_not_exist_either.scene" ) );
		Assert.AreEqual( 1, SubscriptionCount( instance ) );

		instance.UnloadMap();
		Assert.AreEqual( 0, SubscriptionCount( instance ) );

		Assert.IsFalse( Load( instance, "maps/does_not_exist.scene" ) );
		Assert.AreEqual( 1, SubscriptionCount( instance ) );

		scene.Destroy();
		Assert.AreEqual( 0, SubscriptionCount( instance ) );
	}

	/// <summary>
	/// A component destroyed while it's disabled still unsubscribes.
	/// </summary>
	[TestMethod]
	public void DestroyedWhileDisabledUnsubscribes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var instance = go.Components.Create<MapInstance>();

		Load( instance, "maps/does_not_exist.scene" );
		Assert.AreEqual( 1, SubscriptionCount( instance ) );

		go.Enabled = false;
		Load( instance, "maps/does_not_exist_either.scene" );

		go.Destroy();
		scene.ProcessDeletes();
		Assert.AreEqual( 0, SubscriptionCount( instance ) );

		scene.Destroy();
	}

	/// <summary>
	/// Touching PhysicsWorld on a destroyed scene (e.g. from an in-flight map load after Stop) used to
	/// create a new native world that nothing ever deleted.
	/// </summary>
	[TestMethod]
	public void DestroyedSceneDoesntCreatePhysicsWorld()
	{
		var scene = new Scene();
		Assert.IsNotNull( scene.PhysicsWorld );

		scene.Destroy();

		var count = PhysicsWorldInternal.All.Count;
		Assert.IsNull( scene.PhysicsWorld );
		Assert.AreEqual( count, PhysicsWorldInternal.All.Count );
	}
}
