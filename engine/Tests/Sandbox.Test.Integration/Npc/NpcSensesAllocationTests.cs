using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using Sandbox.Utility;
using static NpcTests.NpcSensesSnapshotTests;

namespace NpcTests;

[TestClass]
public class NpcSensesAllocationTests
{
	[TestMethod]
	public void NearestStimulusExcludesSelfExpiredAndOutOfRangeAndKeepsFirstTie()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var system = scene.GetSystem<NpcSystem>();
		var self = scene.CreateObject();
		system.EmitStimulus( "self", Vector3.Forward, self, 100, 100 );
		system.EmitStimulus( "expired", Vector3.Forward * 2, radius: 100, lifetime: -1 );
		system.EmitStimulus( "out of range", Vector3.Forward * 3, radius: 1, lifetime: 100 );
		Assert.IsNull( system.FindNearestStimulus( Vector3.Zero, self ) );

		system.EmitStimulus( "far", Vector3.Forward * 30, radius: 100, lifetime: 100 );
		system.EmitStimulus( "first tie", Vector3.Forward * 10, radius: 100, lifetime: 100 );
		system.EmitStimulus( "second tie", Vector3.Backward * 10, radius: 100, lifetime: 100 );
		Assert.AreEqual( "first tie", system.FindNearestStimulus( Vector3.Zero, self )?.Kind );
	}

	[TestMethod]
	public void TagNearestKeepsTieOrderFiltersAndLazyVisibility()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = SpawnNpc<BaseNpc>( scene, Vector3.Zero );
		var first = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 100 );
		var second = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 100 );
		var untagged = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 50 );
		npc.Tags.Add( "test-visible" );
		first.Tags.Add( "test-visible" );
		second.Tags.Add( "test-visible" );
		npc.SetRelationship( first.GameObject, Disposition.Hostile );
		npc.SetRelationship( second.GameObject, Disposition.Hostile );
		scene.GameTick();
		var senses = npc.Senses;
		scene.GetSystem<NpcSystem>().ScanSenses( force: true );
		Assert.AreEqual( first.GameObject, senses.FindNearestVisible( "test-visible" ) );
		first.GameObject.Destroy();
		scene.GameTick(); // Flush deferred destruction while automatic scanning is paused.
		Assert.AreEqual( second.GameObject, senses.FindNearestVisible( "test-visible" ), "Destroyed objects are skipped without a new scan" );
		second.Tags.Remove( "test-visible" );
		Assert.IsNull( senses.FindNearestVisible( "test-visible" ) );
		untagged.Tags.Add( "test-visible" );
		Assert.AreEqual( untagged.GameObject, senses.FindNearestVisible( "test-visible" ) );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	[DataRow( "default" )]
	[DataRow( "inherited" )]
	[DataRow( "overridden" )]
	public void WarmedScansStimuliAndLazyTagQueriesAllocateZeroBytes( string relationship )
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		using var playerTargets = EnablePlayerTargets();
		BaseNpc npc = relationship switch
		{
			"inherited" => SpawnNpc<SetupProbe>( scene, Vector3.Zero ),
			"overridden" => SpawnNpc<RelationshipProbe>( scene, Vector3.Zero ),
			_ => SpawnNpc<BaseNpc>( scene, Vector3.Zero )
		};
		npc.Faction = Factions.Ally;
		BaseNpc target = relationship == "inherited"
			? SpawnNpc<FactionProbe>( scene, Vector3.Forward * 200 )
			: SpawnNpc<BaseNpc>( scene, Vector3.Forward * 200 );
		target.Faction = Factions.Enemy;
		target.Senses.Enabled = false;
		AddBox( target.GameObject, target.EyePosition - Vector3.Forward * 10, new Vector3( 20, 20, 20 ) );

		var neutral = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 100 );
		neutral.Tags.Add( "allocation-neutral" );
		neutral.Senses.Enabled = false;
		var player = SpawnPlayer( scene, Vector3.Left * 200, out _ );
		scene.GameTick();
		var senses = npc.Senses;
		senses.Targets |= SensesLayer.SenseTargets.Players;
		npc.SetRelationship( player, Disposition.Hostile );
		var overrideCalls = 0;
		if ( npc is RelationshipProbe probe )
		{
			probe.Resolve = obj =>
			{
				overrideCalls++;
				return new Relationship( obj == player || obj == target.GameObject ? Disposition.Hostile : Disposition.Neutral );
			};
		}
		var factionReads = 0;
		if ( target is FactionProbe faction ) faction.OnRead = () => factionReads++;

		var system = scene.GetSystem<NpcSystem>();
		system.EmitStimulus( "allocation stimulus", target.WorldPosition, target.GameObject, 500, 100 );
		GameObject nearest = null;
		Allocations.AssertSettles( $"10 NPC scans with {relationship} relationships, stimuli and lazy tag queries", () =>
		{
			for ( var i = 0; i < 10; i++ )
			{
				system.ScanSenses( force: true );
				nearest = senses.FindNearestVisible( "allocation-neutral" );
			}
		} );
		CollectionAssert.AreEquivalent( new[] { target.GameObject, player }, senses.VisibleHostiles.ToArray() );
		Assert.AreEqual( "allocation stimulus", senses.Disturbance?.Kind );
		Assert.AreEqual( neutral.GameObject, nearest );
		if ( relationship == "overridden" ) Assert.IsTrue( overrideCalls > 0 );
		if ( relationship == "inherited" ) Assert.IsTrue( factionReads > 0 );
	}
}
