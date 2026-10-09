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

		Game.TypeLibrary = new TypeLibrary();
		Game.TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );

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
	public void NestedTargetedInternalMessageIsDepthLimited()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var client = clientAndHost.Client;

		// One wrapper is legitimate - the host relays targeted messages - so it must still arrive.
		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNested( 1, client.Id ) );
		Assert.AreNotEqual( 0, client.Messages.Count, "A singly nested targeted message should still be delivered." );

		// At the limit still runs, so the limit is nowhere near the stack.
		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNested( LimitDepth, client.Id ) );
		Assert.AreNotEqual( 0, client.Messages.Count, $"Nesting {LimitDepth} deep is on the limit and should still be delivered." );

		// One past it is not.
		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNested( LimitDepth + 1, client.Id ) );
		Assert.AreEqual( 0, client.Messages.Count, $"Nesting {LimitDepth + 1} deep is over the limit and must be dropped." );

		// A deep nest must be dropped before the recursion can exhaust the stack.
		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNested( AttackDepth, client.Id ) );
		Assert.AreEqual( 0, client.Messages.Count, "A deeply nested targeted message must not reach its innermost payload." );
	}

	// TargetedMessage dispatches its nested handler directly, bypassing HandleIncomingMessage.
	[TestMethod]
	public void NestedTargetedMessageIsDepthLimited()
	{
		using var scope = new Scene().Push();
		using var clientAndHost = new ClientAndHost( TypeLibrary );

		clientAndHost.BecomeHost();

		var client = clientAndHost.Client;

		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNestedTargeted( 1, client.Id ) );
		Assert.AreNotEqual( 0, client.Messages.Count, "A singly nested targeted message should still be delivered." );

		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNestedTargeted( LimitDepth, client.Id ) );
		Assert.AreNotEqual( 0, client.Messages.Count, $"Nesting {LimitDepth} deep is on the limit and should still be delivered." );

		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNestedTargeted( LimitDepth + 1, client.Id ) );
		Assert.AreEqual( 0, client.Messages.Count, $"Nesting {LimitDepth + 1} deep is over the limit and must be dropped." );

		client.Messages.Clear();
		Dispatch( clientAndHost, BuildNestedTargeted( AttackDepth, client.Id ) );
		Assert.AreEqual( 0, client.Messages.Count, "A deeply nested targeted message must not reach its innermost payload." );
	}

	private static byte[] BuildNestedTargeted( int wrappers, Guid clientId )
	{
		var payload = PackTargeted( clientId, clientId, [] );

		for ( int i = 0; i < wrappers; i++ )
		{
			payload = PackTargeted( clientId, Guid.Empty, payload );
		}

		return payload;
	}

	private static byte[] PackTargeted( Guid senderId, Guid targetId, byte[] message )
	{
		var bs = ByteStream.Create( message.Length + 128 );

		bs.Write( InternalMessageType.Packed );

		TypeLibrary.ToBytes( new TargetedMessage
		{
			SenderId = senderId,
			TargetId = targetId,
			Message = message,
			Flags = (byte)NetFlags.Reliable,
		}, ref bs );

		var bytes = bs.ToArray();
		bs.Dispose();

		return bytes;
	}

	private static void Dispatch( ClientAndHost clientAndHost, byte[] payload )
	{
		using var reader = ByteStream.CreateReader( payload );

		Networking.System.HandleIncomingMessage( new NetworkSystem.NetworkMessage
		{
			Source = clientAndHost.Client,
			Data = reader,
		} );
	}

	// Layers addressed to nobody so the host recurses, around one addressed at a real
	// connection so delivery is observable.
	private static byte[] BuildNested( int wrappers, Guid clientId )
	{
		var payload = Pack( clientId, clientId, [] );

		for ( int i = 0; i < wrappers; i++ )
		{
			payload = Pack( clientId, Guid.Empty, payload );
		}

		return payload;
	}

	private static byte[] Pack( Guid senderId, Guid targetId, byte[] data )
	{
		var bs = ByteStream.Create( data.Length + 128 );

		bs.Write( InternalMessageType.Packed );

		TypeLibrary.ToBytes( new TargetedInternalMessage
		{
			SenderId = senderId,
			TargetId = targetId,
			Data = data,
			Flags = (byte)NetFlags.Reliable,
		}, ref bs );

		var bytes = bs.ToArray();
		bs.Dispose();

		return bytes;
	}
}
