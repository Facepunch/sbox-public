using System;
using Sandbox.Internal;
using Sandbox.Network;
using SceneTests;

namespace SceneTests.GameObjects;

using static GlobalGameNamespace;

// A stack overflow can't be caught, so unbounded nesting kills the server process.
[TestClass]
public class NetworkRecursionTest
{
	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;

		Game.TypeLibrary = SharedTypeLibrary.Engine;

		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	// Deep enough to have overflowed before the fix, so a regression fails loudly.
	private const int AttackDepth = 4096;

	// Must track NetworkSystem.MaxNestedDispatch.
	private const int LimitDepth = 128;

	[TestMethod]
	public void NestedMulticastIsDepthLimited()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		// One wrapper is legitimate - clients send multicasts - so it must still arrive.
		Assert.IsTrue( Delivered( clientAndHost, 1 ), "A singly nested multicast should still be delivered." );

		// At the limit still runs, so the limit is nowhere near the stack.
		Assert.IsTrue( Delivered( clientAndHost, LimitDepth ), $"Nesting {LimitDepth} deep is on the limit and should still be delivered." );

		// One past it is not.
		Assert.IsFalse( Delivered( clientAndHost, LimitDepth + 1 ), $"Nesting {LimitDepth + 1} deep is over the limit and must be dropped." );

		// A deep nest must be dropped before the recursion can exhaust the stack.
		Assert.IsFalse( Delivered( clientAndHost, AttackDepth ), "A deeply nested multicast must not reach its innermost payload." );
	}

	// Layers of multicasts for the host around a client tick, which shows up as the client's visibility origin
	private static bool Delivered( ClientAndHost clientAndHost, int wrappers )
	{
		var client = clientAndHost.Client;
		client.VisibilityOrigins = [];

		var tick = ByteStream.Create( 32 );
		tick.Write( InternalMessageType.ClientTick );
		tick.Write( (char)1 );
		tick.Write( 1f );
		tick.Write( 2f );
		tick.Write( 3f );
		var payload = tick.ToArray();
		tick.Dispose();

		for ( int i = 0; i < wrappers; i++ )
		{
			var packet = NetworkSystem.EncodeMulticast( client.Id, NetFlags.Reliable, [Connection.Local.Id], payload );
			payload = Connection.Decode( packet.Span ).ToArray();
			packet.Release();
		}

		using var reader = ByteStream.CreateReader( payload );

		Networking.System.HandleIncomingMessage( new NetworkSystem.NetworkMessage
		{
			Source = client,
			Data = reader,
		} );

		return client.VisibilityOrigins.Length == 1;
	}
}
