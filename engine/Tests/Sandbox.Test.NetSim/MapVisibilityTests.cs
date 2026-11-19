using Borealis.Source2.Resources;
using Borealis.Source2.World;
using System.Reflection;
using N3 = System.Numerics.Vector3;

namespace NetSim;

/// <summary>
/// The host sends a client only the objects the map's visibility lets it see from where it looks
/// (<see cref="Scene.IsBBoxVisibleToConnection"/>). The map here is a real one's visibility (rl_white_bloodfield_benchmark,
/// <c>SBOX_TEST_VIS_VPK</c> for another) on the host's <see cref="MapInstance"/>, without its world: NetSim has no native
/// engine to load one. Peers have no cameras, so a client looks from the world origin and the map is placed to put the
/// spot it looks from there. Inconclusive when the map isn't there.
/// </summary>
[TestClass]
public class MapVisibilityTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	static WorldVisibility ReadOrSkip()
	{
		var vpk = Environment.GetEnvironmentVariable( "SBOX_TEST_VIS_VPK" ) ?? "core/maps/rl_white_bloodfield_benchmark.vpk";
		if ( !System.IO.File.Exists( vpk ) ) Assert.Inconclusive( $"No {vpk}" );

		using var package = VpkFile.Open( vpk );
		var file = package.Files.FirstOrDefault( x => x.EndsWith( "/world_visibility.vvis_c" ) );
		var visibility = file is null ? null : WorldVisibility.Read( package.Read( file ) );
		if ( visibility is null || visibility.IsEmpty ) Assert.Inconclusive( $"{vpk} has no visibility" );
		return visibility;
	}

	/// <summary>A map instance whose loaded map is only <paramref name="visibility"/>, placed at <paramref name="origin"/>.</summary>
	static MapInstance MapWith( WorldVisibility visibility, Vector3 origin )
	{
		var go = new GameObject( true, "map" );
		go.Flags |= GameObjectFlags.NotNetworked;
		var instance = go.Components.Create<MapInstance>();
		var map = (SceneMap)Activator.CreateInstance( typeof( SceneMap ), true );
		typeof( SceneMap ).GetField( "visibility", Private ).SetValue( map, visibility );
		typeof( SceneMap ).GetField( "visibilityRead", Private ).SetValue( map, true );
		typeof( SceneMap ).GetProperty( "Origin", Private | BindingFlags.Public ).SetValue( map, origin );
		typeof( MapInstance ).GetField( "loadedMap", Private ).SetValue( instance, map );
		return instance;
	}

	/// <summary>A spot that can't see everything, in the map's space: one box near it is visible from it and one isn't.</summary>
	static (N3 Eye, N3 Seen, N3 Hidden) Spots( WorldVisibility visibility )
	{
		var random = new Random( 2 );
		N3 Any() => visibility.Mins + (visibility.Maxs - visibility.Mins) * new N3( random.NextSingle(), random.NextSingle(), random.NextSingle() );
		var size = new N3( 16 );
		for ( var tries = 0; tries < 20000; tries++ )
		{
			var eye = Any();
			N3? seen = null, hidden = null;
			for ( var k = 0; k < 64 && (seen is null || hidden is null); k++ )
			{
				var at = Any();
				if ( visibility.IsBoxVisible( [eye], at - size, at + size, N3.Zero ) ) seen ??= at;
				else hidden ??= at;
			}

			if ( seen is { } s && hidden is { } h ) return (eye, s, h);
		}

		Assert.Inconclusive( "No spot that can't see everything" );
		return default;
	}

	static bool On( NetSimulation sim, SimPeer peer, Guid id ) =>
		sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id )?.Active ?? false );

	[TestMethod]
	public void ClientsGetOnlyWhatTheMapLetsThemSee()
	{
		var visibility = ReadOrSkip();
		var (eye, seenAt, hiddenAt) = Spots( visibility );

		// The client looks from the world origin: put the eye there
		Vector3 origin = -eye;
		using var sim = new NetSimulation();
		var map = sim.As( sim.Host, () => MapWith( visibility, origin ) );

		try
		{
			var client = sim.Join();

			Guid Spawn( Vector3 at ) => sim.As( sim.Host, () =>
			{
				var go = new GameObject( true, "prop" );
				go.WorldPosition = at;
				go.NetworkSpawn();
				go.Network.AlwaysTransmit = false;
				return go.Id;
			} );

			var seen = Spawn( (Vector3)seenAt + origin );
			var hidden = Spawn( (Vector3)hiddenAt + origin );
			sim.RunSeconds( 4 );

			Assert.IsTrue( sim.As( sim.Host, () => Game.ActiveScene.Directory.FindByGuid( hidden ) is not null ), "the host has both" );
			Assert.IsTrue( On( sim, client, seen ), "the one in view is sent" );
			Assert.IsFalse( On( sim, client, hidden ), "the one out of view isn't" );

			// Moved into view, it comes in
			sim.As( sim.Host, () => Game.ActiveScene.Directory.FindByGuid( hidden ).WorldPosition = (Vector3)seenAt + origin + Vector3.Up );
			sim.RunSeconds( 4 );
			Assert.IsTrue( On( sim, client, hidden ), "moved into view" );
		}
		finally
		{
			// Its map has no world to unload
			sim.As( sim.Host, () => typeof( MapInstance ).GetField( "loadedMap", Private ).SetValue( map, null ) );
		}
	}
}
