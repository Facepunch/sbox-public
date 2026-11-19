using Sandbox.Engine.Resources;
using Sandbox.Navigation;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace NavigationTests;

/// <summary>
/// Drops and jumps the navmesh generates: link components of their own that follow the mesh in use,
/// what agents make of them, regeneration, and bakes that hold them.
/// </summary>
[TestClass]
public class NavMeshGeneratedLinkTests
{
	// A platform 96 high on a floor, all in one tile. Its edges are too high to step or jump up.
	static readonly Vector3 OnTop = new( 300, 512, 96 ), Below = new( 800, 512, 0 );

	static (Scene Scene, GameObject Platform) Ledge( bool generate = true )
	{
		// Generated links take their types from the resources the engine ships
		NavigationTestScene.ShippedLinkType( "drop" );
		NavigationTestScene.ShippedLinkType( "jump" );

		var scene = new Scene();
		scene.Floor( new Vector3( 512, 512, 0 ), 1000 );
		var platform = scene.Box( new Vector3( 300, 512, 48 ), new Vector3( 300, 300, 96 ) );
		if ( generate ) scene.NavMesh.GenerateLinks = true;
		return (scene, platform);
	}

	static NavMeshLink[] Generated( Scene scene )
	{
		scene.ProcessDeletes();
		return scene.GetAllComponents<NavMeshLink>().Where( link => link.IsGenerated ).ToArray();
	}

	// What the mesh in use reports for all its tiles
	static List<Nav.NavLink> Reported( NavMesh navMesh )
	{
		var tiles = new (int X, int Y)[256];
		var links = new List<Nav.NavLink>();
		foreach ( var (x, y) in tiles.AsSpan( 0, navMesh.mesh.GetTiles( tiles ) ) ) navMesh.mesh.GetGeneratedLinks( x, y, links );
		return links;
	}

	static NavMeshPathStatus Down( NavMesh navMesh, NavMeshAgent agent = null ) => navMesh.CalculatePath( new() { Start = OnTop, Target = Below, Agent = agent } ).Status;

	[TestMethod]
	public async Task NoLinksAreGeneratedByDefault()
	{
		var (scene, _) = Ledge( generate: false );
		using var scope = scene.Push();
		Assert.IsFalse( scene.NavMesh.GenerateLinks );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.IsFalse( scene.NavMesh.mesh.LinkSettings.Enabled );
		Assert.AreEqual( 0, Generated( scene ).Length );
		Assert.AreEqual( NavMeshPathStatus.Partial, Down( scene.NavMesh ) );
	}

	[TestMethod]
	public async Task ALedgeGetsDropLinksThatAnAgentWalksDown()
	{
		var (scene, _) = Ledge();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var links = Generated( scene );
		Assert.IsTrue( links.Length >= 4, $"{links.Length} links" );
		CollectionAssert.AreEquivalent( Reported( scene.NavMesh ), links.Select( link => link.generated ).ToList() );
		foreach ( var link in links )
		{
			Assert.AreSame( NavMeshLinkType.Drop, link.Type );
			Assert.IsFalse( link.IsBiDirectional, "A drop is one way" );
			Assert.IsTrue( link.WorldStartPositionOnNavmesh.Value.z > 90 && link.WorldEndPositionOnNavmesh.Value.z < 10 );
		}

		// One container, not saved or networked
		var root = links.Select( link => link.GameObject.Parent ).Distinct().Single();
		Assert.IsTrue( root.Flags.HasFlag( GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked ) );
		Assert.IsFalse( scene.Serialize().ToJsonString().Contains( root.Id.ToString() ) );

		Assert.AreEqual( NavMeshPathStatus.Complete, Down( scene.NavMesh ) );
		Assert.AreEqual( NavMeshPathStatus.Partial, scene.NavMesh.CalculatePath( new() { Start = Below, Target = OnTop } ).Status );

		// Crossed like a placed link
		var agent = scene.Agent( OnTop );
		var crossed = new List<(NavMeshLink Link, NavMeshLinkType Type)>();
		agent.LinkEnter = () => crossed.Add( (agent.CurrentLinkTraversal.Value.LinkComponent, agent.CurrentLinkTraversal.Value.Type) );
		agent.MoveTo( Below );
		for ( int i = 0; i < 3000 && (agent.IsNavigating || crossed.Count == 0); i++ ) scene.GameTick();
		Assert.AreEqual( 1, crossed.Count, $"The agent stopped at {agent.AgentPosition}" );
		Assert.IsTrue( links.Contains( crossed[0].Link ) );
		Assert.AreSame( NavMeshLinkType.Drop, crossed[0].Type );
		Assert.IsTrue( agent.AgentPosition.Distance( Below ) < 32, $"The agent stopped at {agent.AgentPosition}" );
	}

	[TestMethod]
	public async Task AnAgentThatMayNotDropStaysOnTheLedge()
	{
		var (scene, _) = Ledge();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var agent = scene.Agent( OnTop );
		agent.AllowAllLinkTypes = false;
		Assert.AreEqual( NavMeshPathStatus.Partial, Down( scene.NavMesh, agent ) );
		agent.AllowedLinkTypes.Add( NavMeshLinkType.Jump );
		Assert.AreEqual( NavMeshPathStatus.Partial, Down( scene.NavMesh, agent ) );
		agent.AllowedLinkTypes.Add( NavMeshLinkType.Drop );
		Assert.AreEqual( NavMeshPathStatus.Complete, Down( scene.NavMesh, agent ) );
	}

	[TestMethod]
	public async Task RegeneratingAnUnchangedSceneKeepsTheComponents()
	{
		var (scene, _) = Ledge();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var links = Generated( scene );

		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		CollectionAssert.AreEquivalent( links, Generated( scene ) );
		scene.NavMesh.SetDirty();
		await scene.Settle();
		CollectionAssert.AreEquivalent( links, Generated( scene ) );

		// Cleared from the scene, as an editor undo does: the next update makes them again
		links[0].GameObject.Parent.Destroy();
		scene.ProcessDeletes();
		scene.NavUpdate();
		var again = Generated( scene );
		Assert.AreEqual( links.Length, again.Length );
		Assert.IsFalse( again.Intersect( links ).Any() );
		Assert.AreEqual( NavMeshPathStatus.Complete, Down( scene.NavMesh ) );
	}

	[TestMethod]
	public async Task DropsAcrossATileBorderAreFoundAndKept()
	{
		// The east edge is just short of the border at x 1024, so its drops land in the next tile
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( new Vector3( 1024, 512, 0 ), 2000 );
		scene.Box( new Vector3( 855, 512, 48 ), new Vector3( 310, 300, 96 ) );
		scene.NavMesh.GenerateLinks = true;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var links = Generated( scene );
		Assert.IsTrue( links.Any( link => link.WorldStartPosition.x < 1024 && link.WorldEndPosition.x > 1024 ), "No drop crosses the border" );
		CollectionAssert.AreEquivalent( Reported( scene.NavMesh ), links.Select( link => link.generated ).ToList() );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = new Vector3( 855, 512, 96 ), Target = new Vector3( 1500, 512, 0 ) } ).Status );

		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		CollectionAssert.AreEquivalent( Reported( scene.NavMesh ), links.Select( link => link.generated ).ToList() );
		CollectionAssert.AreEquivalent( links, Generated( scene ) );
	}

	[TestMethod]
	public async Task AFullRegenerationLeavesExactlyTheNewSet()
	{
		var (scene, platform) = Ledge();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var links = Generated( scene );

		// The platform moves: rebuilt in place
		scene.Remove( platform );
		scene.Box( new Vector3( 700, 512, 48 ), new Vector3( 200, 200, 96 ) );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var moved = Generated( scene );
		Assert.IsTrue( moved.Length > 0 && !moved.Intersect( links ).Any() );
		Assert.IsTrue( links.All( link => !link.IsValid() ) );
		CollectionAssert.AreEquivalent( Reported( scene.NavMesh ), moved.Select( link => link.generated ).ToList() );

		// Other settings build a second mesh aside, whose set replaces the first when it takes over
		var first = scene.NavMesh.mesh;
		scene.NavMesh.AgentMaxDrop = 64;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		Assert.AreNotSame( first, scene.NavMesh.mesh );
		Assert.AreEqual( 0, Generated( scene ).Length, "No ledge is low enough to drop off" );

		scene.NavMesh.AgentMaxDrop = 192;
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var back = Generated( scene );
		CollectionAssert.AreEquivalent( Reported( scene.NavMesh ), back.Select( link => link.generated ).ToList() );
		CollectionAssert.AreEquivalent( moved.Select( link => link.generated ).ToList(), back.Select( link => link.generated ).ToList() );
		Assert.AreEqual( NavMeshPathStatus.Complete, scene.NavMesh.CalculatePath( new() { Start = new Vector3( 700, 512, 96 ), Target = new Vector3( 200, 512, 0 ) } ).Status );
	}

	[TestMethod]
	public async Task ABakeRestoresTheGeneratedLinksWithoutGenerating()
	{
		var (scene, _) = Ledge();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var links = Generated( scene ).Select( link => link.generated ).ToList();
		var baked = await scene.NavMesh.BakeDataToBytes();
		Assert.IsNotNull( baked );

		// A scene without geometry: everything comes from the bake
		var loaded = new Scene();
		using ( loaded.Push() )
		{
			loaded.NavMesh.GenerateLinks = true;
			loaded.NavMesh.IsEnabled = true;
			await loaded.NavMesh.LoadFromBakedData( baked );
			var restored = Generated( loaded );
			CollectionAssert.AreEquivalent( links, restored.Select( link => link.generated ).ToList() );
			Assert.IsTrue( restored.All( link => link.Type == NavMeshLinkType.Drop && !link.IsBiDirectional ) );
			Assert.AreEqual( NavMeshPathStatus.Complete, Down( loaded.NavMesh ) );

			loaded.NavUpdate();
			loaded.NavUpdate();
			Assert.AreEqual( 0, loaded.NavMesh.mesh.PendingTiles );
			CollectionAssert.AreEquivalent( restored, Generated( loaded ) );
		}

		// Made with links, so a navmesh without them generates instead
		using var plain = new NavMesh { IsEnabled = true };
		await plain.LoadFromBakedData( baked );
		Assert.IsNull( plain.GetRandomPoint() );
	}

	[TestMethod]
	public async Task ABakeOfTheFirstVersionStillLoads()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.Floor( Vector3.Zero, 700 );
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var baked = await scene.NavMesh.BakeDataToBytes();

		// Version 1: the same header, the raw length of the library's bake and its block to the end
		int raw = BinaryPrimitives.ReadInt32LittleEndian( baked.AsSpan( 24 ) ), stored = BinaryPrimitives.ReadInt32LittleEndian( baked.AsSpan( 28 ) );
		var old = new byte[28 + stored];
		baked.AsSpan( 0, 24 ).CopyTo( old );
		BinaryPrimitives.WriteUInt32LittleEndian( old.AsSpan( 4 ), 1 );
		BinaryPrimitives.WriteInt32LittleEndian( old.AsSpan( 24 ), raw );
		baked.AsSpan( 32, stored ).CopyTo( old.AsSpan( 28 ) );

		using var loaded = new NavMesh { IsEnabled = true };
		await loaded.LoadFromBakedData( old );
		Assert.IsTrue( loaded.OnMesh( Vector3.Zero ) );
		Assert.IsTrue( loaded.OnMesh( new Vector3( 300, 300, 0 ) ) );

		// It has no links, so a navmesh that generates them generates
		using var linked = new NavMesh { GenerateLinks = true, IsEnabled = true };
		await linked.LoadFromBakedData( old );
		Assert.IsNull( linked.GetRandomPoint() );
	}

	// The bake with its link block rewritten. Raw layout: four floats of settings, the tile count at 16, the first tile's link count at 28, its first link at 32.
	static byte[] WithLinkBlock( byte[] baked, Action<byte[]> edit )
	{
		int offset = 24;
		NavMesh.ReadBlock( baked, ref offset, false );
		int at = offset;
		var raw = NavMesh.ReadBlock( baked, ref offset, false );
		edit( raw );
		var data = new byte[at + 8 + Sandbox.Compression.LZ4.MaxCompressedSize( raw.Length )];
		baked.AsSpan( 0, at ).CopyTo( data );
		return data.AsSpan( 0, at + NavMesh.WriteBlock( raw, data.AsSpan( at ) ) ).ToArray();
	}

	[TestMethod]
	public async Task ABakeWithMalformedLinksIsRejectedWhole()
	{
		var (scene, _) = Ledge();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var baked = await scene.NavMesh.BakeDataToBytes();

		using ( var intact = new NavMesh { GenerateLinks = true, IsEnabled = true } )
		{
			await intact.LoadFromBakedData( WithLinkBlock( baked, _ => { } ) );
			Assert.IsNotNull( intact.GetRandomPoint(), "Rewriting alone must not damage it" );
		}

		var damaged = new (string Name, byte[] Data)[]
		{
			("negative tile count", WithLinkBlock( baked, raw => BinaryPrimitives.WriteInt32LittleEndian( raw.AsSpan( 16 ), -1 ) )),
			("negative link count", WithLinkBlock( baked, raw => BinaryPrimitives.WriteInt32LittleEndian( raw.AsSpan( 28 ), -1 ) )),
			("coordinate that is not finite", WithLinkBlock( baked, raw => BinaryPrimitives.WriteSingleLittleEndian( raw.AsSpan( 32 ), float.NaN ) )),
			("radius below zero", WithLinkBlock( baked, raw => BinaryPrimitives.WriteSingleLittleEndian( raw.AsSpan( 56 ), -4 ) )),
			("flag it does not know", WithLinkBlock( baked, raw => raw[60] |= 0x80 )),
			("truncated link block", baked.AsSpan( 0, baked.Length - 3 ).ToArray()),
		};
		foreach ( var (name, data) in damaged )
		{
			using var loaded = new NavMesh { GenerateLinks = true, IsEnabled = true };
			await loaded.LoadFromBakedData( data );
			Assert.IsNull( loaded.GetRandomPoint(), $"A bake with a {name} was loaded" );
		}
	}

	[TestMethod]
	public async Task AGeneratedLinksTypeAndGeometryAreFixed()
	{
		var (scene, _) = Ledge();
		using var scope = scene.Push();
		Assert.IsTrue( await scene.NavMesh.Generate( scene.PhysicsWorld ) );
		var link = Generated( scene )[0];
		link.Type = new NavMeshLinkType();
		Assert.AreSame( NavMeshLinkType.Drop, link.Type );

		// Moved, then turned off and on: the mesh still gets what was reported
		link.LocalEndPosition += Vector3.Up * 500;
		link.Enabled = false;
		link.Enabled = true;
		scene.NavUpdate();
		Assert.AreEqual( link.generated.End, (System.Numerics.Vector3)link.WorldEndPositionOnNavmesh.Value );
		Assert.AreEqual( NavMeshPathStatus.Complete, Down( scene.NavMesh ) );
	}
}
