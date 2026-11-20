using Sandbox.Npcs;
using static NpcTests.NpcSensesSnapshotTests;

namespace NpcTests;

[TestClass]
public class NpcSensesRangeTests
{
	static BaseNpc Spawn( Scene scene, Vector3 position ) => SpawnNpc<BaseNpc>( scene, position, fieldOfView: null );

	static void Scan( Scene scene ) => scene.GetSystem<NpcSystem>().ScanSenses( force: true );

	[TestMethod]
	[DataRow( 1f )]
	[DataRow( -1f )]
	public void RangePrefilterKeepsRoundedBoundaryAndDropsDistantNpcs( float direction )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = Spawn( scene, new Vector3( direction * 11.99999f, 0, 0 ) );
		var boundary = Spawn( scene, new Vector3( direction * 512, 0, 0 ) );
		var far = Spawn( scene, new Vector3( direction * 1000, 0, 0 ) );
		scene.GameTick();
		var senses = npc.Senses;
		senses.FieldOfView = 360;
		senses.SightRange = senses.HearingRange = 500;
		npc.SetRelationship( boundary.GameObject, Disposition.Hostile );
		npc.SetRelationship( far.GameObject, Disposition.Hostile );
		Assert.AreEqual( 500f, npc.WorldPosition.Distance( boundary.WorldPosition ), "Float distance rounds this target onto the range edge" );

		Scan( scene );
		CollectionAssert.AreEqual( new[] { boundary.GameObject }, senses.VisibleHostiles.ToArray() );
		CollectionAssert.AreEqual( new[] { boundary.GameObject }, senses.AudibleTargets.ToArray() );
	}

	[TestMethod]
	public void NegativeAndNonfiniteRangesKeepTheOriginalAdmissionRules()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = Spawn( scene, Vector3.Zero );
		var target = Spawn( scene, Vector3.Forward * 1000 );
		scene.GameTick();
		var senses = npc.Senses;
		senses.FieldOfView = 360;
		npc.SetRelationship( target.GameObject, Disposition.Hostile );
		foreach ( var range in new[] { -1f, float.NaN, float.PositiveInfinity, float.MaxValue } )
		{
			senses.SightRange = senses.HearingRange = range;
			Scan( scene );
			var distance = npc.WorldPosition.Distance( target.WorldPosition );
			Assert.AreEqual( !(distance > range), senses.VisibleHostiles.Contains( target.GameObject ), $"Sight range {range}" );
			Assert.AreEqual( distance <= range, senses.AudibleTargets.Contains( target.GameObject ), $"Hearing range {range}" );
		}
	}

	[TestMethod]
	public void SquaredRangeOverflowAndUnderflowStillUseTheExactDistanceCheck()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = Spawn( scene, Vector3.Zero );
		var target = Spawn( scene, Vector3.Forward * 1e20f );
		scene.GameTick();
		var senses = npc.Senses;
		senses.FieldOfView = 360;
		npc.SetRelationship( target.GameObject, Disposition.Hostile );
		senses.SightRange = senses.HearingRange = float.MaxValue;
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( target.GameObject ), "The original float distance overflows even though the range is finite" );

		target.WorldPosition = Vector3.Forward * 1e-30f;
		senses.SightRange = senses.HearingRange = 0;
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( target.GameObject ), "A squared distance that underflows to zero still counts as at the origin" );
	}

	[TestMethod]
	public void HearingRangeRemainsIndependentOfSightRange()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = Spawn( scene, Vector3.Zero );
		var target = Spawn( scene, Vector3.Backward * 400 );
		scene.GameTick();
		npc.Senses.SightRange = 500;
		npc.Senses.HearingRange = 200;
		npc.SetRelationship( target.GameObject, Disposition.Hostile );
		Scan( scene );
		Assert.IsFalse( npc.Senses.AudibleTargets.Contains( target.GameObject ) );
		Assert.IsFalse( npc.Senses.VisibleHostiles.Contains( target.GameObject ) );

		npc.Senses.HearingRange = 450;
		Scan( scene );
		Assert.IsTrue( npc.Senses.AudibleTargets.Contains( target.GameObject ) );
		Assert.IsFalse( npc.Senses.VisibleHostiles.Contains( target.GameObject ) );
	}

	[TestMethod]
	public void FieldOfViewCacheTracksRuntimeChangesAndPersonalSpace()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var npc = Spawn( scene, Vector3.Zero );
		var target = Spawn( scene, new Vector3( 100, 173, 0 ) );
		scene.GameTick();
		var senses = npc.Senses;
		npc.SetRelationship( target.GameObject, Disposition.Hostile );
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( target.GameObject ), "Default 200 degree field of view" );
		senses.FieldOfView = 90;
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( target.GameObject ) );
		senses.FieldOfView = 180;
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( target.GameObject ) );
		senses.FieldOfView = float.NaN;
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( target.GameObject ) );
		senses.FieldOfView = float.PositiveInfinity;
		Scan( scene );
		Assert.IsFalse( senses.VisibleHostiles.Contains( target.GameObject ) );
		target.WorldPosition = Vector3.Backward * 10;
		Scan( scene );
		Assert.IsTrue( senses.VisibleHostiles.Contains( target.GameObject ), "Personal space bypasses the cone even with a nonfinite field of view" );
	}
}
