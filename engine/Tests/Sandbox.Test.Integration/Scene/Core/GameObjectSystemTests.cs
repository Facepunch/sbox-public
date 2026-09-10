using System;
using System.Collections.Generic;

namespace SceneTests.Core;

/// <summary>
/// Pins the GameObjectSystem base class contract: Listen() hooks fire in stage order
/// within a tick, the order parameter sorts listeners within a stage, systems are
/// disposed when their scene is destroyed, and GetSystem/GameObjectSystem&lt;T&gt;
/// lookups resolve registered systems. Complements SceneSystems.cs, which only covers
/// the per-scene property override path.
/// </summary>
[TestClass]
public class GameObjectSystemCoverageTest
{
	/// <summary>
	/// Runs the body with a TypeLibrary containing the test assembly, so the test
	/// systems below are instantiated for the scene - the same swap pattern as
	/// SceneSystems.cs. The scene is destroyed afterwards so its hooks don't leak.
	/// </summary>
	static void WithTestSystems( System.Action<Scene> body )
	{
		var oldTypeLibrary = Game.TypeLibrary;
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( GameObjectSystemCoverageTest ).Assembly, false );
		Game.TypeLibrary = typeLibrary;

		Scene scene = null;

		try
		{
			scene = new Scene();
			using var sceneScope = scene.Push();
			body( scene );
		}
		finally
		{
			scene?.Destroy();
			Game.TypeLibrary = oldTypeLibrary;
		}
	}

	/// <summary>
	/// One GameTick signals the stages in the documented order: the fixed update
	/// block (StartFixedUpdate, PhysicsStep, FinishFixedUpdate) runs first, then the
	/// frame block (StartUpdate, Interpolation, UpdateBones, FinishUpdate) exactly
	/// once each. SceneLoaded never fires during a normal tick.
	/// </summary>
	[TestMethod]
	public void StagesSignalInTickOrder()
	{
		WithTestSystems( scene =>
		{
			var recorder = scene.GetSystem<StageRecorderSystem>();
			Assert.IsNotNull( recorder, "the test system should be instantiated for the scene" );

			// warm-up tick so component starts etc don't pollute the recording
			scene.GameTick();

			recorder.Calls.Clear();

			try
			{
				StageRecorderSystem.Recording = true;
				scene.GameTick();
			}
			finally
			{
				StageRecorderSystem.Recording = false;
			}

			var calls = recorder.Calls;
			var log = string.Join( ",", calls );

			// the frame stages fire exactly once per tick
			Assert.AreEqual( 1, calls.Count( x => x == "StartUpdate.early" ), log );
			Assert.AreEqual( 1, calls.Count( x => x == "Interpolation" ), log );
			Assert.AreEqual( 1, calls.Count( x => x == "UpdateBones" ), log );
			Assert.AreEqual( 1, calls.Count( x => x == "FinishUpdate" ), log );

			// every fixed update stage entry comes before the frame update block
			var firstFrameStage = calls.IndexOf( "StartUpdate.early" );
			Assert.IsTrue( firstFrameStage >= 0, log );

			var lastFixedStage = calls.LastIndexOf( "FinishFixedUpdate" );
			Assert.IsTrue( lastFixedStage >= 0, "at least one fixed update should have run: " + log );
			Assert.IsTrue( lastFixedStage < firstFrameStage, "fixed update stages must finish before StartUpdate: " + log );

			// each fixed update cycles StartFixedUpdate -> PhysicsStep -> FinishFixedUpdate
			var fixedStages = new[] { "StartFixedUpdate", "PhysicsStep", "FinishFixedUpdate" };
			var fixedCalls = calls.Where( fixedStages.Contains ).ToList();

			Assert.AreEqual( 0, fixedCalls.Count % 3, "fixed stages should come in complete groups: " + log );

			for ( int i = 0; i < fixedCalls.Count; i++ )
			{
				Assert.AreEqual( fixedStages[i % 3], fixedCalls[i], "fixed stage cycle broken: " + log );
			}

			// the frame stages run in the documented order
			var frameOrder = new[] { "StartUpdate.early", "Interpolation", "UpdateBones", "FinishUpdate" };
			for ( int i = 0; i < frameOrder.Length - 1; i++ )
			{
				Assert.IsTrue( calls.IndexOf( frameOrder[i] ) < calls.IndexOf( frameOrder[i + 1] ),
					$"{frameOrder[i]} should run before {frameOrder[i + 1]}: {log}" );
			}

			// SceneLoaded is only signalled by scene loading, never by ticking
			Assert.IsFalse( calls.Contains( "SceneLoaded" ), log );
		} );
	}

	/// <summary>
	/// Listeners on the same stage run sorted by their order value, not their
	/// registration order - the recorder registers late (1) before early (-1) and
	/// mid (0), but they execute -1, 0, 1, contiguously within the stage.
	/// </summary>
	[TestMethod]
	public void SameStageListenersRunInOrderValue()
	{
		WithTestSystems( scene =>
		{
			var recorder = scene.GetSystem<StageRecorderSystem>();
			scene.GameTick();
			recorder.Calls.Clear();

			try
			{
				StageRecorderSystem.Recording = true;
				scene.GameTick();
			}
			finally
			{
				StageRecorderSystem.Recording = false;
			}

			var calls = recorder.Calls;
			var log = string.Join( ",", calls );

			var early = calls.IndexOf( "StartUpdate.early" );
			Assert.IsTrue( early >= 0, log );
			Assert.AreEqual( "StartUpdate.mid", calls[early + 1], "order 0 should run right after order -1: " + log );
			Assert.AreEqual( "StartUpdate.late", calls[early + 2], "order 1 should run right after order 0: " + log );
		} );
	}

	/// <summary>
	/// A listener on Stage.SceneLoaded fires when the stage is signalled - this is
	/// the stage Scene.Load signals after deserializing a scene file - and fires
	/// once per signal.
	/// </summary>
	[TestMethod]
	public void SceneLoadedStageReachesListeners()
	{
		WithTestSystems( scene =>
		{
			var recorder = scene.GetSystem<StageRecorderSystem>();
			recorder.Calls.Clear();

			try
			{
				StageRecorderSystem.Recording = true;
				scene.Signal( GameObjectSystem.Stage.SceneLoaded );
			}
			finally
			{
				StageRecorderSystem.Recording = false;
			}

			Assert.AreEqual( 1, recorder.Calls.Count( x => x == "SceneLoaded" ) );
			Assert.AreEqual( 1, recorder.Calls.Count, "no other stage should have fired" );
		} );
	}

	/// <summary>
	/// Destroying the scene disposes every system: Dispose() is called, the base
	/// implementation nulls the Scene reference, and the system registry is cleared
	/// so GetSystem returns null afterwards.
	/// </summary>
	[TestMethod]
	public void SystemsDisposeWhenSceneDestroyed()
	{
		var oldTypeLibrary = Game.TypeLibrary;
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( GameObjectSystemCoverageTest ).Assembly, false );
		Game.TypeLibrary = typeLibrary;

		try
		{
			var scene = new Scene();

			var probe = scene.GetSystem<DisposalProbeSystem>();
			Assert.IsNotNull( probe );
			Assert.AreEqual( scene, probe.Scene, "a live system holds its scene" );
			Assert.IsFalse( probe.WasDisposed );

			scene.Destroy();

			Assert.IsTrue( probe.WasDisposed, "scene destruction should dispose its systems" );
			Assert.IsNull( probe.Scene, "the base Dispose nulls the Scene reference" );
			Assert.IsNull( scene.GetSystem<DisposalProbeSystem>(), "the system registry is cleared on destroy" );
		}
		finally
		{
			Game.TypeLibrary = oldTypeLibrary;
		}
	}

	/// <summary>
	/// GetSystem lookups: the generic and out-parameter overloads return the same
	/// per-scene singleton, every system gets a non-empty unique Id, two scenes get
	/// independent instances, and a scene whose TypeLibrary doesn't contain the type
	/// returns null.
	/// </summary>
	[TestMethod]
	public void GetSystemLookups()
	{
		WithTestSystems( scene =>
		{
			var a = scene.GetSystem<StageRecorderSystem>();
			var b = scene.GetSystem<StageRecorderSystem>();
			Assert.IsNotNull( a );
			Assert.AreSame( a, b, "GetSystem should return the same instance every call" );

			scene.GetSystem<StageRecorderSystem>( out var c );
			Assert.AreSame( a, c, "the out overload should resolve the same instance" );

			var other = scene.GetSystem<DisposalProbeSystem>();
			Assert.IsNotNull( other );
			Assert.AreNotEqual( Guid.Empty, a.Id, "systems are given an Id on construction" );
			Assert.AreNotEqual( Guid.Empty, other.Id );
			Assert.AreNotEqual( a.Id, other.Id, "system ids are unique" );

			// a second scene under the same TypeLibrary gets its own instances
			var second = new Scene();
			try
			{
				var d = second.GetSystem<StageRecorderSystem>();
				Assert.IsNotNull( d );
				Assert.AreNotSame( a, d, "systems are per-scene singletons" );
			}
			finally
			{
				second.Destroy();
			}
		} );

		// a scene built from a TypeLibrary without the test assembly doesn't have the system
		var oldTypeLibrary = Game.TypeLibrary;
		var engineOnly = new Sandbox.Internal.TypeLibrary();
		engineOnly.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		Game.TypeLibrary = engineOnly;

		Scene bare = null;

		try
		{
			bare = new Scene();
			Assert.IsNull( bare.GetSystem<StageRecorderSystem>(), "unregistered system types resolve to null" );
		}
		finally
		{
			bare?.Destroy();
			Game.TypeLibrary = oldTypeLibrary;
		}
	}

	/// <summary>
	/// The GameObjectSystem&lt;T&gt; sugar: Get(scene) resolves through GetSystem,
	/// Get(null) returns null instead of throwing, and Current resolves against the
	/// pushed active scene.
	/// </summary>
	[TestMethod]
	public void GenericSystemStaticAccessors()
	{
		WithTestSystems( scene =>
		{
			var system = scene.GetSystem<StaticAccessSystem>();
			Assert.IsNotNull( system );

			Assert.AreSame( system, StaticAccessSystem.Get( scene ) );
			Assert.IsNull( StaticAccessSystem.Get( null ), "Get(null) should be null, not throw" );
			Assert.AreSame( system, StaticAccessSystem.Current, "Current resolves via the pushed active scene" );
		} );
	}
}

/// <summary>
/// Registers a Listen hook on every stage, plus three hooks on StartUpdate with
/// orders deliberately registered out of order (1, -1, 0) to prove the order value
/// sorts execution. Inert unless the static Recording flag is set, so its presence
/// in other tests' scenes is harmless.
/// </summary>
public class StageRecorderSystem : GameObjectSystem
{
	/// <summary>
	/// When false (the default) the hooks record nothing, keeping this system inert
	/// in scenes created by unrelated tests.
	/// </summary>
	public static bool Recording;

	/// <summary>
	/// The stage names in the order their hooks ran.
	/// </summary>
	public List<string> Calls { get; } = new();

	public StageRecorderSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.PhysicsStep, 0, () => Record( "PhysicsStep" ), "record" );
		Listen( Stage.FinishUpdate, 0, () => Record( "FinishUpdate" ), "record" );
		Listen( Stage.StartUpdate, 1, () => Record( "StartUpdate.late" ), "record-late" );
		Listen( Stage.StartUpdate, -1, () => Record( "StartUpdate.early" ), "record-early" );
		Listen( Stage.StartUpdate, 0, () => Record( "StartUpdate.mid" ), "record-mid" );
		Listen( Stage.StartFixedUpdate, 0, () => Record( "StartFixedUpdate" ), "record" );
		Listen( Stage.FinishFixedUpdate, 0, () => Record( "FinishFixedUpdate" ), "record" );
		Listen( Stage.Interpolation, 0, () => Record( "Interpolation" ), "record" );
		Listen( Stage.UpdateBones, 0, () => Record( "UpdateBones" ), "record" );
		Listen( Stage.SceneLoaded, 0, () => Record( "SceneLoaded" ), "record" );
	}

	/// <summary>
	/// Appends a stage name to the call log while recording is enabled.
	/// </summary>
	void Record( string stage )
	{
		if ( !Recording ) return;
		Calls.Add( stage );
	}
}

/// <summary>
/// Records whether Dispose was called, to pin system teardown on scene destroy.
/// Inert - it registers no hooks.
/// </summary>
public class DisposalProbeSystem : GameObjectSystem
{
	/// <summary>
	/// True once the scene disposed this system.
	/// </summary>
	public bool WasDisposed { get; private set; }

	public DisposalProbeSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Marks the probe before running the base teardown (which nulls Scene).
	/// </summary>
	public override void Dispose()
	{
		WasDisposed = true;
		base.Dispose();
	}
}

/// <summary>
/// A system using the GameObjectSystem&lt;T&gt; convenience base, to pin the static
/// Current/Get accessors. Inert - it registers no hooks.
/// </summary>
public class StaticAccessSystem : GameObjectSystem<StaticAccessSystem>
{
	public StaticAccessSystem( Scene scene ) : base( scene )
	{
	}
}
