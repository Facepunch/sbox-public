namespace SceneTests.Core;

/// <summary>
/// Pins core Scene behavior: editor scene creation, the push scope's active-scene and
/// time bookkeeping, BatchGroup callback deferral, the scene clock, and teardown.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SceneCoreTest : SceneTest
{
	/// <summary>
	/// CreateEditorScene produces a scene flagged as an editor scene, while the normal
	/// constructor produces a game scene. Both are valid until destroyed.
	/// </summary>
	[TestMethod]
	public void CreateEditorScene()
	{
		var editorScene = Scene.CreateEditorScene();
		var gameScene = new Scene();

		Assert.IsTrue( editorScene.IsEditor );
		Assert.IsTrue( editorScene.IsValid );
		Assert.IsFalse( gameScene.IsEditor );

		editorScene.Destroy();
		gameScene.Destroy();

		Assert.IsFalse( editorScene.IsValid );
		Assert.IsFalse( gameScene.IsValid );
	}

	/// <summary>
	/// WantsSystemScene defaults to true - scenes opt in to the additive system scene
	/// unless they explicitly turn it off.
	/// </summary>
	[TestMethod]
	public void WantsSystemSceneDefaultsTrue()
	{
		var scene = new Scene();

		Assert.IsTrue( scene.WantsSystemScene );

		scene.Destroy();
	}

	/// <summary>
	/// Push makes the scene the active scene for the scope and restores the previous
	/// active scene when the scope is disposed, including when scopes nest.
	/// </summary>
	[TestMethod]
	public void PushSetsAndRestoresActiveScene()
	{
		var sceneA = new Scene();
		var sceneB = new Scene();
		var previous = Game.ActiveScene;

		using ( sceneA.Push() )
		{
			Assert.AreEqual( sceneA, Game.ActiveScene );

			using ( sceneB.Push() )
			{
				Assert.AreEqual( sceneB, Game.ActiveScene );
			}

			Assert.AreEqual( sceneA, Game.ActiveScene );
		}

		Assert.AreEqual( previous, Game.ActiveScene );

		sceneA.Destroy();
		sceneB.Destroy();
	}

	/// <summary>
	/// Each scene keeps its own clock, advanced by GameTick. Pushing the scene exposes
	/// that clock through the global Time, and disposing the scope restores the previous
	/// global time values.
	/// </summary>
	[TestMethod]
	public void SceneClockAdvancesWithGameTick()
	{
		var scene = new Scene();

		var previousNow = Time.Now;
		var previousDelta = Time.Delta;

		using ( scene.Push() )
		{
			scene.GameTick( 0.5 );
		}

		using ( scene.Push() )
		{
			Assert.AreEqual( 0.5f, Time.Now, 0.001f );
			Assert.AreEqual( 0.5f, Time.Delta, 0.001f );
		}

		Assert.AreEqual( previousNow, Time.Now, 0.001f );
		Assert.AreEqual( previousDelta, Time.Delta, 0.001f );

		scene.Destroy();
	}

	/// <summary>
	/// TimeScale scales the scene clock - a half-speed scene advances half as far per
	/// tick as the wall-clock delta it was given.
	/// </summary>
	[TestMethod]
	public void TimeScaleScalesSceneClock()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		scene.TimeScale = 0.5f;
		scene.GameTick( 1.0 );

		using ( scene.Push() )
		{
			Assert.AreEqual( 0.5f, Time.Now, 0.001f );
		}

		scene.Destroy();
	}

	/// <summary>
	/// BatchGroup collects component lifecycle callbacks and flushes them when the group
	/// is disposed - inside the scope a freshly created component hasn't been awoken or
	/// enabled yet.
	/// </summary>
	[TestMethod]
	public void BatchGroupDefersLifecycleCallbacks()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		SceneCoreProbe probe;

		using ( scene.BatchGroup() )
		{
			probe = go.Components.Create<SceneCoreProbe>();

			Assert.AreEqual( 0, probe.AwakeCalls );
			Assert.AreEqual( 0, probe.EnabledCalls );
		}

		Assert.AreEqual( 1, probe.AwakeCalls );
		Assert.AreEqual( 1, probe.EnabledCalls );

		scene.Destroy();
	}

	/// <summary>
	/// Destroying the scene tears down everything in it immediately - the scene and all
	/// of its objects and components become invalid without needing another tick.
	/// </summary>
	[TestMethod]
	public void DestroyTearsDownObjects()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var probe = go.Components.Create<SceneCoreProbe>();

		scene.Destroy();

		Assert.IsFalse( scene.IsValid );
		Assert.IsFalse( go.IsValid );
		Assert.IsFalse( probe.IsValid );
		Assert.AreEqual( 1, probe.DestroyCalls );
	}
}

/// <summary>
/// Plain component recording its lifecycle callbacks, with no assertions of its own,
/// so tests can observe when the callbacks ran.
/// </summary>
public class SceneCoreProbe : Component
{
	public int AwakeCalls;
	public int EnabledCalls;
	public int DisabledCalls;
	public int DestroyCalls;

	protected override void OnAwake() => AwakeCalls++;
	protected override void OnEnabled() => EnabledCalls++;
	protected override void OnDisabled() => DisabledCalls++;
	protected override void OnDestroy() => DestroyCalls++;
}
