using Sandbox.Network;
using System;
using System.IO;

namespace NetworkTests;

/// <summary>
/// A connection the host hasn't let in can't make it deserialize anything but the handshake.
/// Deserializing alone was enough to run the host out of memory.
/// </summary>
[TestClass, DoNotParallelize]
public class HandshakeGateTests
{
	static NetworkSystem System() => new( "handshake-gate-test", SharedTypeLibrary.ScriptingWithSystem );

	static bool IsHandshakePayload( NetworkSystem system, byte[] payload, Guid responseTo = default )
	{
		using var reader = ByteStream.CreateReader( payload );
		var msg = new NetworkSystem.NetworkMessage { Source = new GateConnection(), Data = reader };
		var start = msg.Data.Position;

		var allowed = system.IsHandshakePayload( ref msg, responseTo );

		Assert.AreEqual( start, msg.Data.Position, "The gate must leave the payload unread" );
		return allowed;
	}

	static byte[] Packed<T>( NetworkSystem system, T value )
	{
		var bs = ByteStream.Create( 256 );
		try
		{
			system.Serialize( value, ref bs );
			return bs.ToArray();
		}
		finally
		{
			bs.Dispose();
		}
	}

	[TestMethod]
	public void HandshakeMessagesAreRead()
	{
		var system = System();

		Assert.IsTrue( IsHandshakePayload( system, Packed( system, new UserInfo { Name = "joining" } ) ) );
		Assert.IsTrue( IsHandshakePayload( system, Packed( system, new RestartHandshakeMsg() ) ) );
	}

	[TestMethod]
	public void EverythingElseIsDroppedUnread()
	{
		var system = System();

		using var ms = new MemoryStream();
		using var w = new BinaryWriter( ms );
		w.Write( (byte)BytePack.Identifier.Dictionary );
		w.Write( int.MaxValue );
		w.Flush();

		Assert.IsFalse( IsHandshakePayload( system, ms.ToArray() ) );
		Assert.IsFalse( IsHandshakePayload( system, Packed( system, "not a handshake" ) ) );
		Assert.IsFalse( IsHandshakePayload( system, Packed( system, new UserInfo() ), Guid.NewGuid() ), "Responses only for requests we sent" );
	}

	private sealed class GateConnection : Connection
	{
		internal override void InternalSend( NetPacket packet, NetFlags flags ) { }
		internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
		internal override void InternalClose( int closeCode, string closeReason ) { }
	}
}
