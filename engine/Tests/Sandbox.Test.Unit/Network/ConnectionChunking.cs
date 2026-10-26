using System;
using System.Collections.Generic;
using Sandbox.Network;

namespace NetworkTests;

[TestClass]
public class ConnectionChunkingTest
{
	[TestMethod]
	public void BroadcastRecipientsShareChunkPackets()
	{
		var encoded = new byte[Connection.MaxChunkSize * 2 + 10];
		new Random( 42 ).NextBytes( encoded );

		var a = new RecordingConnection();
		var b = new RecordingConnection();
		byte[][] packets = null;

		var packet = NetPacket.Wrap( encoded );
		a.Send( packet, NetFlags.Reliable, ref packets );
		b.Send( packet, NetFlags.Reliable, ref packets );

		Assert.AreEqual( 3, a.Sent.Count );
		CollectionAssert.AreEqual( a.Sent, b.Sent );

		for ( var i = 0; i < packets.Length; i++ )
		{
			Assert.AreSame( packets[i], a.Sent[i] );
		}
	}

	[TestMethod]
	public void SmallPayloadIsSentAsIs()
	{
		var encoded = new byte[64];
		var connection = new RecordingConnection();
		byte[][] packets = null;

		connection.Send( NetPacket.Wrap( encoded ), NetFlags.Reliable, ref packets );

		Assert.AreEqual( 1, connection.Sent.Count );
		Assert.AreSame( encoded, connection.Sent[0] );
		Assert.IsNull( packets );
	}

	private sealed class RecordingConnection : Connection
	{
		public readonly List<byte[]> Sent = new();
		public override bool IsHost => false;
		internal override void InternalSend( NetPacket packet, NetFlags flags ) => Sent.Add( packet.Buffer );
		internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
		internal override void InternalClose( int closeCode, string closeReason ) { }
	}
}
