using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using Sandbox.Utility;
using SceneTests.Trace;
using System;

namespace NpcTests;

[TestClass]
public class NpcSensesSnapshotTests
{
	public sealed class RelationshipProbe : BaseNpc
	{
		internal Func<GameObject, Relationship> Resolve;

		public override Relationship GetRelationship( GameObject other ) => Resolve?.Invoke( other ) ?? base.GetRelationship( other );
	}

	public sealed class SetupProbe : BaseNpc
	{
		internal int SetupCalls;

		protected override void SetupRelationships()
		{
			SetupCalls++;
			base.SetupRelationships();
		}
	}

	public sealed class FactionProbe : BaseNpc
	{
		internal Action OnRead;

		public override string Faction
		{
			get
			{
				OnRead?.Invoke();
				return base.Faction;
			}
			set => base.Faction = value;
		}
	}

	internal static T SpawnNpc<T>( Scene scene, Vector3 position, float? fieldOfView = 360f ) where T : BaseNpc, new()
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		var animation = go.Components.Create<AnimationLayer>();
		var senses = go.Components.Create<SensesLayer>();
		senses.ScanInterval = 1_000_000f;
		senses.Targets = SensesLayer.SenseTargets.Npcs;
		if ( fieldOfView.HasValue ) senses.FieldOfView = fieldOfView.Value;
		var npc = go.Components.Create<T>();
		npc.Animation = animation;
		return npc;
	}

	internal static GameObject AddBox( GameObject parent, Vector3 position, Vector3 scale )
	{
		var box = new GameObject( parent );
		box.WorldPosition = position;
		box.Components.Create<BoxCollider>().Scale = scale;
		return box;
	}

	internal static DisposeAction EnablePlayerTargets()
	{
		var noTarget = NpcConVars.NoTarget;
		NpcConVars.NoTarget = false;
		return new DisposeAction( () => NpcConVars.NoTarget = noTarget );
	}

	internal static GameObject SpawnPlayer( Scene scene, Vector3 position, out PlayerController controller )
	{
		var player = scene.CreateObject();
		player.WorldPosition = position;
		controller = player.Components.Create<PlayerController>();
		player.Components.Create<NpcLayerTests.Hurtable>();
		return player;
	}

	[TestMethod]
	public void ScanPublishesSightHearingPushingAndLazyNeutralSight()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		using var playerTargets = EnablePlayerTargets();

		var observer = SpawnNpc<BaseNpc>( scene, Vector3.Zero, fieldOfView: 180 );
		var hostile = SpawnNpc<BaseNpc>( scene, new Vector3( 200, -100, 0 ) );
		var quietHostile = SpawnNpc<BaseNpc>( scene, new Vector3( -140, 0, 0 ) );
		var threat = SpawnNpc<BaseNpc>( scene, new Vector3( 200, 100, 0 ) );
		var neutral = SpawnNpc<BaseNpc>( scene, new Vector3( 220, 0, 0 ) );
		var player = SpawnPlayer( scene, Vector3.Left * 55, out var controller );
		scene.GameTick();

		// Inject velocity after the player's physics tick.
		typeof( PlayerController ).GetProperty( nameof( PlayerController.Velocity ) ).SetValue( controller, Vector3.Right * 150 );
		hostile.Animation.SetMove( Vector3.Forward * 200 );
		var senses = observer.Senses;
		senses.Targets |= SensesLayer.SenseTargets.Players;
		observer.SetRelationship( hostile.GameObject, Disposition.Hostile );
		observer.SetRelationship( quietHostile.GameObject, Disposition.Hostile );
		observer.SetRelationship( threat.GameObject, Disposition.Fearful );

		var system = scene.GetSystem<NpcSystem>();
		system.EmitStimulus( "noise", hostile.WorldPosition, hostile.GameObject, 500, 100 );
		var initialTraces = SceneMetrics.RayTrace;
		system.ScanSenses( force: true );
		CollectionAssert.AreEqual( new[] { hostile.GameObject }, senses.VisibleHostiles.ToArray() );
		CollectionAssert.AreEqual( new[] { threat.GameObject }, senses.VisibleThreats.ToArray() );
		CollectionAssert.AreEquivalent( new[] { hostile.GameObject, quietHostile.GameObject }, senses.AudibleTargets.ToArray() );
		Assert.AreEqual( hostile.GameObject, senses.GetHeardHostile() );
		Assert.AreEqual( player, senses.GetPushingPlayer() );
		Assert.AreEqual( "noise", senses.Disturbance?.Kind );
		Assert.AreEqual( 3L, SceneMetrics.RayTrace - initialTraces, "Traces before the lazy sight query" );

		neutral.Tags.Add( "snapshot-neutral" );
		Assert.AreEqual( neutral.GameObject, senses.FindNearestVisible( "snapshot-neutral" ) );
		Assert.AreEqual( 4L, SceneMetrics.RayTrace - initialTraces );
	}

	[TestMethod]
	public void SightTracesHonorBlockersTriggersAndHierarchies()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var observer = SpawnNpc<RelationshipProbe>( scene, Vector3.Zero );
		var target = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 200 );
		var mainThread = Environment.CurrentManagedThreadId;
		var callbackThread = 0;
		var calls = 0;
		var eyes = observer.EyePosition;
		var scale = new Vector3( 20, 40, 40 );
		AddBox( observer.GameObject, eyes + Vector3.Forward * 40, scale );
		var wall = AddBox( scene, eyes + Vector3.Forward * 100, scale );
		AddBox( target.GameObject, eyes + Vector3.Forward * 190, scale );
		scene.GameTick();
		observer.Resolve = _ =>
		{
			callbackThread = Environment.CurrentManagedThreadId;
			calls++;
			return new Relationship( Disposition.Hostile );
		};

		var system = scene.GetSystem<NpcSystem>();
		system.ScanSenses( force: true );
		Assert.AreEqual( 1, calls );
		Assert.AreEqual( mainThread, callbackThread );
		Assert.AreEqual( 0, observer.Senses.VisibleHostiles.Count, "A solid wall blocks sight" );

		wall.Tags.Add( "trigger" );
		scene.GameTick();
		system.ScanSenses( force: true );
		Assert.AreEqual( target.GameObject, observer.Senses.VisibleHostiles.Single() );
	}

	[TestMethod]
	[DataRow( "disable-layer" )]
	[DataRow( "disable-npc" )]
	[DataRow( "destroy-observer" )]
	[DataRow( "destroy-target" )]
	public void RelationshipCallbackSkipsInvalidObserversAndTargets( string change )
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var observer = SpawnNpc<RelationshipProbe>( scene, Vector3.Zero );
		var first = SpawnNpc<BaseNpc>( scene, new Vector3( 100, 40, 0 ) ).GameObject;
		var second = SpawnNpc<BaseNpc>( scene, new Vector3( 100, -40, 0 ) ).GameObject;
		scene.GameTick();
		var senses = observer.Senses;
		var system = scene.GetSystem<NpcSystem>();
		system.EmitStimulus( "noise", second.WorldPosition, second, 500, 100 );
		observer.Resolve = _ => new Relationship( Disposition.Hostile );
		system.ScanSenses( force: true );
		Assert.AreEqual( 2, senses.VisibleHostiles.Count );
		Assert.IsNotNull( senses.Disturbance );

		GameObject firstResolved = null;
		GameObject secondResolved = null;
		observer.Resolve = target =>
		{
			if ( firstResolved is null )
			{
				firstResolved = target;
				return new Relationship( Disposition.Hostile );
			}

			secondResolved = target;
			switch ( change )
			{
				case "disable-layer": senses.Enabled = false; break;
				case "disable-npc": observer.Enabled = false; break;
				case "destroy-observer": observer.GameObject.DestroyImmediate(); break;
				case "destroy-target": target.DestroyImmediate(); break;
			}
			return new Relationship( Disposition.Hostile );
		};
		system.ScanSenses( force: true );
		CollectionAssert.AreEquivalent( new[] { first, second }, new[] { firstResolved, secondResolved } );
		if ( change == "destroy-target" )
		{
			CollectionAssert.AreEqual( new[] { firstResolved }, senses.VisibleHostiles.ToArray() );
			CollectionAssert.AreEqual( new[] { firstResolved }, senses.AudibleTargets.ToArray() );
		}
		else
		{
			Assert.AreEqual( 0, senses.VisibleHostiles.Count );
			Assert.AreEqual( 0, senses.AudibleTargets.Count );
			Assert.IsNull( senses.Disturbance );
		}
	}

	[TestMethod]
	public void RelationshipCallbackCanChangeTheFieldOfViewForTheCurrentTarget()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var observer = SpawnNpc<RelationshipProbe>( scene, Vector3.Zero );
		var target = SpawnNpc<BaseNpc>( scene, new Vector3( 100, 173, 0 ) );
		target.Senses.Enabled = false;
		scene.GameTick();

		var senses = observer.Senses;
		senses.FieldOfView = 90;
		var callbackFieldOfView = 180f;
		observer.Resolve = _ =>
		{
			senses.FieldOfView = callbackFieldOfView;
			return new Relationship( Disposition.Hostile );
		};

		var system = scene.GetSystem<NpcSystem>();
		system.ScanSenses( force: true );
		CollectionAssert.AreEqual( new[] { target.GameObject }, senses.VisibleHostiles.ToArray() );

		callbackFieldOfView = 90;
		system.ScanSenses( force: true );
		Assert.AreEqual( 0, senses.VisibleHostiles.Count );
	}

	[TestMethod]
	public void LaterObserversSkipADisabledNpcEvenWithAnActiveDamageableChild()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var first = SpawnNpc<RelationshipProbe>( scene, new Vector3( 0, -40, 0 ) );
		var second = SpawnNpc<RelationshipProbe>( scene, new Vector3( 0, 40, 0 ) );
		var target = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 200 );
		target.Senses.Enabled = false;
		new GameObject( target.GameObject ).Components.Create<NpcLayerTests.Hurtable>();
		scene.GameTick();

		var targetResolutions = 0;
		first.Resolve = second.Resolve = other =>
		{
			if ( other != target.GameObject )
				return new Relationship( Disposition.Neutral );

			targetResolutions++;
			target.Enabled = false;
			return new Relationship( Disposition.Hostile );
		};

		scene.GetSystem<NpcSystem>().ScanSenses( force: true );
		Assert.AreEqual( 1, targetResolutions );
		Assert.IsFalse( target.Active );
		Assert.IsTrue( BaseNpc.IsLiveTarget( target.GameObject ), "The active child is still damageable" );
		Assert.AreEqual( 0, first.Senses.VisibleHostiles.Count );
		Assert.AreEqual( 0, second.Senses.VisibleHostiles.Count );
	}

	[TestMethod]
	public void SightIncludesCustomTraceProviders()
	{
		var oldTypeLibrary = Game.TypeLibrary;
		var oldProvideHits = TestTraceProviderSystem.ProvideHits;
		var oldHitFraction = TestTraceProviderSystem.HitFraction;
		using var restore = new DisposeAction( () =>
		{
			Game.TypeLibrary = oldTypeLibrary;
			TestTraceProviderSystem.ProvideHits = oldProvideHits;
			TestTraceProviderSystem.HitFraction = oldHitFraction;
		} );
		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var observer = SpawnNpc<BaseNpc>( scene, Vector3.Zero );
		var target = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 200 );
		observer.SetRelationship( target.GameObject, Disposition.Hostile );
		scene.GameTick();
		var system = scene.GetSystem<NpcSystem>();

		TestTraceProviderSystem.ProvideHits = false;
		system.ScanSenses( force: true );
		Assert.AreEqual( target.GameObject, observer.Senses.VisibleHostiles.Single() );
		TestTraceProviderSystem.ProvideHits = true;
		TestTraceProviderSystem.HitFraction = 0.1f;
		system.ScanSenses( force: true );
		Assert.AreEqual( 0, observer.Senses.VisibleHostiles.Count );
	}

	[TestMethod]
	public void RelationshipSetupWaitsForAnAdmittedCandidateWithoutAnEntityOverride()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var observer = SpawnNpc<SetupProbe>( scene, Vector3.Zero );
		observer.Faction = Factions.Ally;
		var target = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 2000 );
		target.Faction = Factions.Enemy;
		target.Senses.Enabled = false;
		scene.GameTick();

		var system = scene.GetSystem<NpcSystem>();
		system.ScanSenses( force: true );
		Assert.AreEqual( 0, observer.SetupCalls );

		target.WorldPosition = Vector3.Forward * 200;
		observer.SetRelationship( target.GameObject, Disposition.Fearful );
		system.ScanSenses( force: true );
		Assert.AreEqual( 0, observer.SetupCalls );
		CollectionAssert.AreEqual( new[] { target.GameObject }, observer.Senses.VisibleThreats.ToArray() );

		observer.ClearRelationship( target.GameObject );
		system.ScanSenses( force: true );
		Assert.AreEqual( 1, observer.SetupCalls );
		CollectionAssert.AreEqual( new[] { target.GameObject }, observer.Senses.VisibleHostiles.ToArray() );
	}

	[TestMethod]
	public void VirtualFactionGetterRunsOnTheSceneThreadForAdmittedTargets()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var observer = SpawnNpc<BaseNpc>( scene, Vector3.Zero );
		observer.Faction = Factions.Ally;
		var near = SpawnNpc<FactionProbe>( scene, Vector3.Forward * 200 );
		var far = SpawnNpc<FactionProbe>( scene, Vector3.Forward * 2000 );
		near.Faction = far.Faction = Factions.Enemy;
		near.Senses.Enabled = far.Senses.Enabled = false;
		scene.GameTick();
		var mainThread = Environment.CurrentManagedThreadId;
		var nearThread = 0;
		var farReads = 0;
		near.OnRead = () => nearThread = Environment.CurrentManagedThreadId;
		far.OnRead = () => farReads++;
		scene.GetSystem<NpcSystem>().ScanSenses( force: true );

		Assert.AreEqual( mainThread, nearThread );
		Assert.AreEqual( 0, farReads );
		CollectionAssert.AreEqual( new[] { near.GameObject }, observer.Senses.VisibleHostiles.ToArray() );
	}

	[TestMethod]
	public void LaterObserverSeesEarlierResultsAndRevalidatesAReparentedTarget()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		using var playerTargets = EnablePlayerTargets();
		var first = SpawnNpc<RelationshipProbe>( scene, Vector3.Zero );
		var second = SpawnNpc<RelationshipProbe>( scene, new Vector3( 0, 100, 0 ) );
		first.Senses.Targets = second.Senses.Targets = SensesLayer.SenseTargets.Players;
		var player = scene.CreateObject();
		player.WorldPosition = Vector3.Forward * 200;
		player.Components.Create<PlayerController>();
		var damageable = new GameObject( player );
		damageable.Components.Create<NpcLayerTests.Hurtable>();
		scene.GameTick();

		var system = scene.GetSystem<NpcSystem>();
		SensesLayer completed = null;
		SensesLayer current = null;
		var sawCompletedResults = false;

		Relationship Resolve( SensesLayer senses )
		{
			if ( completed is null )
			{
				completed = senses;
			}
			else
			{
				current = senses;
				sawCompletedResults = completed.VisibleHostiles.Contains( player ) && completed.AudibleTargets.Contains( player );
				damageable.SetParent( scene );
			}
			return new Relationship( Disposition.Hostile );
		}

		first.Resolve = _ => Resolve( first.Senses );
		second.Resolve = _ => Resolve( second.Senses );
		system.ScanSenses( force: true );

		Assert.IsTrue( sawCompletedResults );
		Assert.IsFalse( BaseNpc.IsLiveTarget( player ) );
		CollectionAssert.AreEqual( new[] { player }, completed.VisibleHostiles.ToArray() );
		Assert.IsNull( completed.FindBestTarget() );
		Assert.AreEqual( 0, current.VisibleHostiles.Count );
		Assert.AreEqual( 0, current.AudibleTargets.Count );
	}

	[TestMethod]
	public void WarmScansUseCurrentFactionsAndEntityOverrides()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var observer = SpawnNpc<BaseNpc>( scene, Vector3.Zero );
		observer.Faction = Factions.Ally;
		var target = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 200 );
		target.Faction = Factions.Enemy;
		target.Senses.Enabled = false;
		scene.GameTick();
		var system = scene.GetSystem<NpcSystem>();
		system.ScanSenses( force: true );
		CollectionAssert.AreEqual( new[] { target.GameObject }, observer.Senses.VisibleHostiles.ToArray() );

		target.Faction = Factions.Citizen;
		system.ScanSenses( force: true );
		Assert.AreEqual( 0, observer.Senses.VisibleHostiles.Count );

		observer.Faction = Factions.Enemy;
		system.ScanSenses( force: true );
		CollectionAssert.AreEqual( new[] { target.GameObject }, observer.Senses.VisibleHostiles.ToArray() );

		observer.SetRelationship( target.GameObject, Disposition.Fearful );
		system.ScanSenses( force: true );
		Assert.AreEqual( 0, observer.Senses.VisibleHostiles.Count );
		CollectionAssert.AreEqual( new[] { target.GameObject }, observer.Senses.VisibleThreats.ToArray() );

		observer.ClearRelationship( target.GameObject );
		system.ScanSenses( force: true );
		Assert.AreEqual( 0, observer.Senses.VisibleThreats.Count );
		CollectionAssert.AreEqual( new[] { target.GameObject }, observer.Senses.VisibleHostiles.ToArray() );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void WarmStaggeredScansWithMixedFactionsAllocateZeroBytes()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		using var playerTargets = EnablePlayerTargets();
		var player = SpawnPlayer( scene, new Vector3( 0, -240, 0 ), out _ );
		var npcs = new BaseNpc[512];
		for ( int i = 0; i < npcs.Length; ++i )
		{
			var npc = npcs[i] = SpawnNpc<BaseNpc>( scene, new Vector3( (i % 32) * 300, (i / 32) * 300, 0 ) );
			npc.Faction = i % 2 == 0 ? Factions.Ally : Factions.Enemy;
			npc.Senses.Targets |= SensesLayer.SenseTargets.Players;
		}
		scene.GameTick();
		var system = scene.GetSystem<NpcSystem>();
		for ( int i = 0; i < 64; ++i ) npcs[i].Senses.ScanInterval = -1;

		Allocations.AssertSettles( "64 due scans with 512 NPCs and a player", () => system.ScanSenses() );
		Assert.IsTrue( npcs[1].Senses.VisibleHostiles.Contains( player ) );
		Assert.IsTrue( npcs[63].Senses.VisibleHostiles.Contains( npcs[94].GameObject ), "A due observer sees a target that isn't due" );
		Assert.AreEqual( 0, npcs[94].Senses.VisibleHostiles.Count, "A target doesn't need to run its own scan to be seen" );
	}
}
