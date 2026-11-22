using System.Text.Json.Nodes;

namespace NetSim;

/// <summary>
/// What create messages and refreshes carry ends up the same on every peer, for prefab instances with many
/// components and for struct sync values.
/// </summary>
[TestClass]
public class SerializationTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	static void AssertSame( NetSimulation sim, Guid id )
	{
		var expected = sim.As( sim.Host, () => SerializationScene.Snapshot( sim.Host.Scene.Directory.FindByGuid( id ) ) );

		foreach ( var client in sim.Clients )
		{
			var actual = sim.As( client, () => client.Scene.Directory.FindByGuid( id ) is { } go ? SerializationScene.Snapshot( go ) : null );
			Assert.IsNotNull( actual, $"{client} doesn't have {id}" );

			if ( FirstDifference( expected, actual, "" ) is { } difference )
				Assert.Fail( $"{client} differs from the host at {difference}" );
		}
	}

	static string FirstDifference( JsonNode a, JsonNode b, string path )
	{
		if ( a is JsonObject oa && b is JsonObject ob )
		{
			foreach ( var (key, value) in oa )
			{
				if ( !ob.TryGetPropertyValue( key, out var other ) )
					return $"{path}.{key}: missing, host has {value?.ToJsonString()}";

				if ( FirstDifference( value, other, $"{path}.{key}" ) is { } d )
					return d;
			}

			foreach ( var (key, value) in ob )
			{
				if ( !oa.ContainsKey( key ) )
					return $"{path}.{key}: extra {value?.ToJsonString()}";
			}

			return null;
		}

		if ( a is JsonArray aa && b is JsonArray ab )
		{
			if ( aa.Count != ab.Count )
				return $"{path}: {aa.Count} entries, host has {ab.Count}";

			for ( var i = 0; i < aa.Count; i++ )
			{
				var name = (aa[i] as JsonObject)?["Name"] ?? (aa[i] as JsonObject)?["__type"];
				if ( FirstDifference( aa[i], ab[i], $"{path}[{i}{(name is null ? "" : $" {name}")}]" ) is { } d )
					return d;
			}

			return null;
		}

		return JsonNode.DeepEquals( a, b ) ? null : $"{path}: {b?.ToJsonString()}, host has {a?.ToJsonString()}";
	}

	[TestMethod]
	public void PlayerPrefabCreate()
	{
		using var sim = new NetSimulation();
		sim.Join();

		var id = sim.As( sim.Host, () => SerializationScene.SpawnPlayer( new Vector3( 10, 20, 30 ) ).Id );
		sim.RunSeconds( 0.5f );
		AssertSame( sim, id );

		sim.Join();
		sim.RunSeconds( 0.5f );
		AssertSame( sim, id );
	}

	[TestMethod]
	public void FullRefresh()
	{
		using var sim = new NetSimulation();
		sim.Join();

		var id = sim.As( sim.Host, () => SerializationScene.SpawnPlayer( Vector3.Zero ).Id );
		sim.RunSeconds( 0.5f );

		sim.As( sim.Host, () =>
		{
			var go = sim.Host.Scene.Directory.FindByGuid( id );
			var stats = go.Components.Get<SimStats>();
			stats.DisplayName = "renamed";
			stats.Kills = 12;
			stats.Armor = 50;
			stats.Badges.Add( "third" );
			stats.Target = go.Children[0];
			go.Components.Get<SimInventory>().ActiveWeapon = go.Components.Get<SimMovement>();
			go.Components.Get<SimCosmetics>().Destroy();
			new GameObject( go, true, "added" ).Components.Create<SimFire>();
			go.Network.Refresh();
		} );

		sim.RunSeconds( 0.5f );
		AssertSame( sim, id );

		sim.Join();
		sim.RunSeconds( 0.5f );
		AssertSame( sim, id );
	}

	[TestMethod]
	public void ComponentRefresh()
	{
		using var sim = new NetSimulation();
		sim.Join();

		var id = sim.As( sim.Host, () => SerializationScene.SpawnPlayer( Vector3.Zero ).Id );
		sim.RunSeconds( 0.5f );

		sim.As( sim.Host, () =>
		{
			var go = sim.Host.Scene.Directory.FindByGuid( id );
			var inventory = go.Components.Get<SimInventory>();
			inventory.Items = new List<string> { "rocket" };
			inventory.Ammo["rocket"] = 3;
			inventory.ActiveWeapon = null;
			go.Network.Refresh( inventory );

			var stats = go.Components.Create<SimStats>();
			stats.DisplayName = "second";
			go.Network.Refresh( stats );
		} );

		sim.RunSeconds( 0.5f );
		AssertSame( sim, id );
	}

	[TestMethod]
	public void BurningPropRefresh()
	{
		using var sim = new NetSimulation();
		sim.Join();

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "prop" );
			go.Components.Create<SimProp>();
			go.NetworkSpawn( null );
			return go.Id;
		} );

		sim.RunSeconds( 0.5f );

		var fireId = sim.As( sim.Host, () => SerializationScene.Ignite( sim.Host.Scene.Directory.FindByGuid( id ) ).Id );
		sim.Run( 3 );
		AssertSame( sim, id );

		var client = sim.Clients[0];
		var fire = sim.As( client, () => client.Scene.Directory.FindByGuid( fireId ) );
		Assert.IsNotNull( fire );
		Assert.IsTrue( sim.As( client, () => fire.Components.GetAll<ParticleEffect>( FindMode.EverythingInSelfAndDescendants ).Count() ) > 0 );

		sim.Join();
		sim.Run( 3 );
		AssertSame( sim, id );
	}

	static SimGrabber Grabber( NetSimulation sim, SimPeer peer, Guid id ) => sim.As( peer, () => peer.Scene.Directory.FindByGuid( id ).Components.Get<SimGrabber>() );

	[TestMethod]
	public void StructSyncEveryField()
	{
		using var sim = new NetSimulation( new NetSimSettings { Link = new SimLinkSettings { Loss = 0.2f } } );
		var a = sim.Join();

		var (id, targetId) = sim.As( sim.Host, () =>
		{
			var target = new GameObject( true, "target" );
			target.NetworkSpawn( null );
			var go = SerializationScene.SpawnPlayer( Vector3.Zero );
			return (go.Id, target.Id);
		} );

		sim.RunSeconds( 0.5f );

		var plain = new SimPlainState { Position = new Vector3( 1, 2, 3 ), Count = 1, Name = "one" };
		sim.As( sim.Host, () => Grabber( sim, sim.Host, id ).Plain = plain );
		sim.RunSeconds( 1 );
		Assert.AreEqual( 1, Grabber( sim, a, id ).Plain.Count );

		// Only fields after the first change
		sim.As( sim.Host, () => Grabber( sim, sim.Host, id ).Plain = plain with { Count = 2 } );
		sim.RunSeconds( 1 );
		Assert.AreEqual( 2, Grabber( sim, a, id ).Plain.Count );

		sim.As( sim.Host, () => Grabber( sim, sim.Host, id ).Plain = plain with { Count = 2, Name = "two" } );
		sim.RunSeconds( 1 );
		Assert.AreEqual( "two", Grabber( sim, a, id ).Plain.Name );

		sim.As( sim.Host, () => Grabber( sim, sim.Host, id ).Grab = new SimGrabState
		{
			Active = true,
			GameObject = sim.Host.Scene.Directory.FindByGuid( targetId ),
			LocalOffset = new Vector3( 4, 5, 6 ),
			GrabOffset = Rotation.FromYaw( 90 ),
			GrabDistance = 64
		} );

		sim.RunSeconds( 1 );

		var b = sim.Join();
		sim.RunSeconds( 1 );

		foreach ( var client in new[] { a, b } )
		{
			var grab = Grabber( sim, client, id ).Grab;
			Assert.IsTrue( grab.Active, $"{client}" );
			Assert.AreEqual( targetId, sim.As( client, () => grab.GameObject?.Id ), $"{client}" );
			Assert.AreEqual( new Vector3( 4, 5, 6 ), grab.LocalOffset, $"{client}" );
			Assert.AreEqual( 64, grab.GrabDistance, $"{client}" );

			var p = Grabber( sim, client, id ).Plain;
			Assert.AreEqual( new Vector3( 1, 2, 3 ), p.Position, $"{client}" );
			Assert.AreEqual( 2, p.Count, $"{client}" );
			Assert.AreEqual( "two", p.Name, $"{client}" );
		}
	}

	[TestMethod]
	public void StructListSyncEveryField()
	{
		using var sim = new NetSimulation();
		var a = sim.Join();

		var id = sim.As( sim.Host, () =>
		{
			var go = new GameObject( true, "points" );
			go.Components.Create<SimPointList>();
			go.NetworkSpawn( null );
			return go.Id;
		} );

		SimPointList Points( SimPeer peer ) => sim.As( peer, () => peer.Scene.Directory.FindByGuid( id ).Components.Get<SimPointList>() );

		sim.RunSeconds( 0.5f );
		sim.As( sim.Host, () => Points( sim.Host ).Points.Add( new SimPlainState { Position = Vector3.One, Count = 1, Name = "one" } ) );
		sim.RunSeconds( 1 );
		Assert.AreEqual( 1, Points( a ).Points.Single().Count );

		// Change fields after the first, in place
		sim.As( sim.Host, () => { var list = Points( sim.Host ).Points; list[0] = list[0] with { Count = 2 }; } );
		sim.RunSeconds( 1 );
		Assert.AreEqual( 2, Points( a ).Points.Single().Count );

		sim.As( sim.Host, () => { var list = Points( sim.Host ).Points; list[0] = list[0] with { Name = "two" }; } );
		sim.RunSeconds( 1 );
		Assert.AreEqual( "two", Points( a ).Points.Single().Name );

		var b = sim.Join();
		sim.RunSeconds( 1 );
		Assert.AreEqual( 2, Points( b ).Points.Single().Count );
		Assert.AreEqual( "two", Points( b ).Points.Single().Name );
	}

	[TestMethod]
	public void ClientOwnedStructSync()
	{
		using var sim = new NetSimulation();
		var a = sim.Join();
		var b = sim.Join();

		var id = sim.As( a, () => SerializationScene.SpawnPlayer( Vector3.Zero ).Id );
		sim.RunSeconds( 0.5f );

		for ( var i = 1; i <= 5; i++ )
		{
			var count = i;
			sim.As( a, () => Grabber( sim, a, id ).Plain = new SimPlainState { Position = Vector3.One, Count = count, Name = "a" } );
			sim.Run( 2 );
		}

		sim.RunSeconds( 0.5f );

		foreach ( var peer in new[] { sim.Host, b } )
			Assert.AreEqual( 5, Grabber( sim, peer, id ).Plain.Count, $"{peer}" );
	}
}
