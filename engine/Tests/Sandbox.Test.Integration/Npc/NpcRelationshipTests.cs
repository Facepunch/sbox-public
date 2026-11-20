using Sandbox.Npcs;
using Sandbox.Utility;
using static NpcTests.NpcSensesSnapshotTests;

namespace NpcTests;

[TestClass]
public class NpcRelationshipTests
{
	public sealed class Predator : BaseNpc
	{
		public int DefaultCalls;

		protected override void SetupRelationships() => SetRelationship( "protected", Disposition.Neutral );

		protected override Relationship GetFallbackRelationship( GameObject other )
		{
			DefaultCalls++;
			return new( Disposition.Hostile, 2 );
		}
	}

	[TestMethod]
	public void ExplicitRelationshipsTakePrecedenceOverDynamicDefaults()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var predator = SpawnNpc<Predator>( scene, Vector3.Zero );
		var target = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 100 );

		Assert.AreEqual( Disposition.Friendly, predator.GetDisposition( predator.GameObject ) );
		Assert.AreEqual( Disposition.Friendly, predator.GetDisposition( null ) );
		Assert.AreEqual( 0, predator.DefaultCalls );
		Assert.AreEqual( new Relationship( Disposition.Hostile, 2 ), predator.GetRelationship( target.GameObject ) );
		Assert.AreEqual( Disposition.Neutral, target.GetDisposition( predator.GameObject ) );

		predator.SetRelationship( target.GameObject, Disposition.Neutral, 7 );
		Assert.AreEqual( new Relationship( Disposition.Neutral, 7 ), predator.GetRelationship( target.GameObject ) );
		predator.ClearRelationship( target.GameObject );
		target.Faction = "protected";
		Assert.AreEqual( Disposition.Neutral, predator.GetDisposition( target.GameObject ) );
		Assert.AreEqual( 1, predator.DefaultCalls, "Explicit neutral must not fall through to predator defaults" );

		predator.SetRelationship( target.GameObject, Disposition.Friendly );
		Assert.AreEqual( Disposition.Friendly, predator.GetDisposition( target.GameObject ) );
		predator.ClearRelationship( target.GameObject );
		target.Faction = "unknown";
		Assert.AreEqual( Disposition.Hostile, predator.GetDisposition( target.GameObject ) );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void SensingUsesDynamicDefaultsWithoutAllocating()
	{
		var scene = new Scene();
		using var destroy = new DisposeAction( scene.Destroy );
		using var scope = scene.Push();
		var predator = SpawnNpc<Predator>( scene, Vector3.Zero );
		var prey = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 100 );
		var protectedNpc = SpawnNpc<BaseNpc>( scene, Vector3.Forward * 50 );
		protectedNpc.Faction = "protected";
		prey.Senses.Enabled = false;
		protectedNpc.Senses.Enabled = false;
		scene.GameTick();
		var system = scene.GetSystem<NpcSystem>();

		Allocations.AssertSettles( "Sensing with dynamic relationship defaults", () => system.ScanSenses( force: true ) );
		Assert.AreEqual( prey.GameObject, predator.Senses.FindBestTarget() );
		Assert.AreEqual( 1, predator.Senses.VisibleHostiles.Count );
		Assert.IsTrue( predator.DefaultCalls > 0 );
	}
}
