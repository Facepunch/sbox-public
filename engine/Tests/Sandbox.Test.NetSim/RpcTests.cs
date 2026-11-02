using Sandbox.Network;

namespace NetSim;

/// <summary>
/// RPCs of every argument shape reach the right peers with the right values.
/// </summary>
public enum ProbeMode { First, Second, Third }

public sealed class RpcProbe : Component
{
	public static readonly List<string> Calls = new();

	static void Record( string call ) => Calls.Add( $"{SimPeer.Current?.Name}: {call} from {Rpc.Caller?.Id}" );

	[Rpc.Broadcast]
	public void None() => Record( "None" );

	[Rpc.Broadcast]
	public void Pods( int a, float b, Vector3 c, bool d, Color e, ProbeMode f ) => Record( $"Pods {a} {b} {c} {d} {e.Hex} {f}" );

	[Rpc.Broadcast]
	public void References( GameObject go, RpcProbe probe, string text, string nothing, int[] numbers ) =>
		Record( $"References {go?.Name} {probe?.GameObject.Name} {text} {nothing ?? "null"} [{string.Join( ",", numbers ?? [] )}]" );

	[Rpc.Broadcast]
	public void Seven( int a, int b, int c, int d, int e, int f, int g ) => Record( $"Seven {a + b + c + d + e + f + g}" );

	[Rpc.Broadcast]
	public void Generic<T>( T value ) => Record( $"Generic {typeof( T ).Name} {value}" );

	[Rpc.Host]
	public void ToHost( int x ) => Record( $"ToHost {x}" );

	[Rpc.Owner]
	public void ToOwner( int x ) => Record( $"ToOwner {x}" );

	[Rpc.Broadcast]
	public static void Static( int x, string y ) => Record( $"Static {x} {y}" );

	[Rpc.Broadcast( NetFlags.Unreliable )]
	public static void UnreliableStatic( int value ) => Record( $"Unreliable {value}" );

	[Rpc.Broadcast]
	public void Throws( int x ) => throw new InvalidOperationException( $"throws {x}" );
}

[TestClass]
public class RpcTests
{
	[TestInitialize]
	public void Init()
	{
		ThreadSafe.MarkMainThread();
		RpcProbe.Calls.Clear();
	}

	static (NetSimulation Sim, SimPeer A, SimPeer B, Guid Probe) Start( bool mesh )
	{
		var sim = new NetSimulation( new NetSimSettings { Mesh = mesh, Link = new SimLinkSettings { LatencySeconds = 0.05f } } );
		var a = sim.Join();
		var b = sim.Join();

		// Owned by client a
		var id = sim.As( a, () =>
		{
			var go = new GameObject( true, "probe" );
			go.Components.Create<RpcProbe>();
			go.NetworkSpawn();
			return go.Id;
		} );

		sim.RunSeconds( 1 );
		return (sim, a, b, id);
	}

	static RpcProbe Probe( NetSimulation sim, SimPeer peer, Guid id ) => sim.As( peer, () => Game.ActiveScene.Directory.FindByGuid( id ).Components.Get<RpcProbe>() );

	static void Call( NetSimulation sim, SimPeer peer, Guid id, Action<RpcProbe> call )
	{
		sim.As( peer, () => call( Probe( sim, peer, id ) ) );
		sim.RunSeconds( 0.5f );
	}

	static void AssertCalled( NetSimulation sim, string call, SimPeer from, params SimPeer[] on )
	{
		foreach ( var peer in sim.Peers )
		{
			var expected = $"{peer.Name}: {call} from {from.Local.Id}";
			var count = RpcProbe.Calls.Count( c => c == expected );
			Assert.AreEqual( on.Contains( peer ) ? 1 : 0, count, $"{call} on {peer}. Calls:\n{string.Join( "\n", RpcProbe.Calls )}" );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ArgumentsArriveIntact( bool mesh )
	{
		var (sim, a, b, id) = Start( mesh );
		using var _ = sim;
		var all = sim.Peers.ToArray();

		Call( sim, b, id, p => p.None() );
		AssertCalled( sim, "None", b, all );

		var color = Color.FromBytes( 10, 20, 30 );
		Call( sim, b, id, p => p.Pods( 42, 1.5f, new Vector3( 1, 2, 3 ), true, color, ProbeMode.Third ) );
		AssertCalled( sim, $"Pods 42 {1.5f} {new Vector3( 1, 2, 3 )} True {color.Hex} Third", b, all );

		Call( sim, b, id, p => p.References( p.GameObject, p, "hello", null, [1, 2, 3] ) );
		AssertCalled( sim, "References probe probe hello null [1,2,3]", b, all );

		Call( sim, b, id, p => p.Seven( 1, 2, 3, 4, 5, 6, 7 ) );
		AssertCalled( sim, "Seven 28", b, all );

		Call( sim, b, id, p => p.Generic( 2.5f ) );
		AssertCalled( sim, $"Generic Single {2.5f}", b, all );

		Call( sim, b, id, p => RpcProbe.Static( 9, "static" ) );
		AssertCalled( sim, "Static 9 static", b, all );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TargetedRpcsRunWhereTheyShould( bool mesh )
	{
		var (sim, a, b, id) = Start( mesh );
		using var _ = sim;

		Call( sim, b, id, p => p.ToHost( 1 ) );
		AssertCalled( sim, "ToHost 1", b, sim.Host );

		Call( sim, b, id, p => p.ToOwner( 2 ) );
		AssertCalled( sim, "ToOwner 2", b, a );

		Call( sim, a, id, p => p.ToOwner( 3 ) );
		AssertCalled( sim, "ToOwner 3", a, a );
	}

	[TestMethod]
	public void FilteredBroadcastOnlyReachesTheFilter()
	{
		var (sim, a, b, id) = Start( false );
		using var _ = sim;

		sim.As( sim.Host, () =>
		{
			using ( Rpc.FilterInclude( Connection.Find( b.Local.Id ) ) )
				Probe( sim, sim.Host, id ).Pods( 1, 0, default, false, default, default );
		} );
		sim.RunSeconds( 0.5f );

		AssertCalled( sim, $"Pods 1 0 {default( Vector3 )} False {default( Color ).Hex} First", sim.Host, b );
	}

	[TestMethod]
	public void PlainMethodsGetTypedInvokers()
	{
		Assert.IsNotNull( RpcInvoker.Get( typeof( RpcProbe ).GetMethod( nameof( RpcProbe.None ) ) ) );
		Assert.IsNotNull( RpcInvoker.Get( typeof( RpcProbe ).GetMethod( nameof( RpcProbe.Pods ) ) ) );
		Assert.IsNotNull( RpcInvoker.Get( typeof( RpcProbe ).GetMethod( nameof( RpcProbe.References ) ) ) );
		Assert.IsNotNull( RpcInvoker.Get( typeof( RpcProbe ).GetMethod( nameof( RpcProbe.Static ) ) ) );

		// Too many parameters, and generic ones, go through reflection
		Assert.IsNull( RpcInvoker.Get( typeof( RpcProbe ).GetMethod( nameof( RpcProbe.Seven ) ) ) );
		Assert.IsNull( RpcInvoker.Get( typeof( RpcProbe ).GetMethod( nameof( RpcProbe.Generic ) ) ) );
	}

	[TestMethod]
	public void ThrowingRpcDoesNotBreakTheNextOne()
	{
		var (sim, a, b, id) = Start( false );
		using var _ = sim;

		Call( sim, b, id, p => { try { p.Throws( 1 ); } catch ( InvalidOperationException ) { } } );
		Call( sim, b, id, p => p.None() );
		AssertCalled( sim, "None", b, sim.Peers.ToArray() );
	}
}
