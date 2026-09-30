using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System;
using System.Collections.Generic;

namespace NetworkTests;

#pragma warning disable CS8981

[TestClass]
public class TcpConnectionTest
{
	TypeLibrary tl;

	[TestInitialize]
	public void TestInitialize()
	{
		Logging.Enabled = true;
		Project.Clear();
		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/tcp";
		AssetDownloadCache.Initialize( dir );

		tl = new TypeLibrary();
		tl.AddAssembly( typeof( Bootstrap ).Assembly, false );
		tl.AddAssembly( GetType().Assembly, true );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();
	}

	Sandbox.Network.StringTable InstallDataTables( NetworkSystem system )
	{
		Sandbox.Network.StringTable table = new( "Assembly", true );

		if ( system.IsHost )
		{
			table.Set( "TestAssembly", new byte[883712] );
			table.Set( "AnotherAssembly", new byte[345600] );
			table.Set( "MoreAssembly", new byte[376346] );
			table.Set( "WhatNotAnotherOne", new byte[153019] );
		}

		system.InstallTable( table );
		return table;
	}

	[TestMethod]
	[DoNotParallelize]
	public void Tcp_FakeLagReceivesOnTickThread()
	{
		using var listener = new System.Net.Sockets.TcpListener( System.Net.IPAddress.Loopback, 0 );
		listener.Start();
		using var peer = new System.Net.Sockets.TcpClient();
		peer.Connect( (System.Net.IPEndPoint)listener.LocalEndpoint );
		var channel = new TcpChannel( listener.AcceptTcpClient() );
		var previousFakeLag = Networking.FakeLag;
		var received = new System.Collections.Concurrent.ConcurrentQueue<(byte Value, int Thread)>();
		var tickThread = Environment.CurrentManagedThreadId;
		NetworkSystem.MessageHandler handler = msg => received.Enqueue( (msg.Data.Read<byte>(), Environment.CurrentManagedThreadId) );

		try
		{
			Networking.FakeLag = 100;
			channel.incoming.Writer.TryWrite( [Connection.FlagRaw, 1] );
			channel.InternalRecv( handler );
			Assert.IsTrue( received.IsEmpty );

			// Turning lag off must not let new packets overtake pending packets.
			Networking.FakeLag = 0;
			channel.incoming.Writer.TryWrite( [Connection.FlagRaw, 2] );
			channel.InternalRecv( handler );
			Assert.IsTrue( received.IsEmpty );

			System.Threading.Thread.Sleep( 300 );
			Assert.IsTrue( received.IsEmpty, "The fake-lag worker must not dispatch incoming messages" );

			channel.InternalRecv( handler );
			CollectionAssert.AreEqual( new[] { ((byte)1, tickThread), ((byte)2, tickThread) }, received.ToArray() );
		}
		finally
		{
			Networking.FakeLag = previousFakeLag;
			channel.Close( 0, "Test complete" );
		}
	}

	[TestMethod]
	[DoNotParallelize]
	[DataRow( 0 )]
	[DataRow( 100 )]
	public void Tcp_ReceiveStopsWhenCallbackCloses( int fakeLag )
	{
		using var listener = new System.Net.Sockets.TcpListener( System.Net.IPAddress.Loopback, 0 );
		listener.Start();
		using var peer = new System.Net.Sockets.TcpClient();
		peer.Connect( (System.Net.IPEndPoint)listener.LocalEndpoint );
		var channel = new TcpChannel( listener.AcceptTcpClient() );
		var previousFakeLag = Networking.FakeLag;
		var received = new List<byte>();
		var closed = false;
		NetworkSystem.MessageHandler handler = msg =>
		{
			received.Add( msg.Data.Read<byte>() );
			if ( !closed )
			{
				channel.Close( 0, "Closed by callback" );
				closed = true;
			}
		};

		try
		{
			Networking.FakeLag = fakeLag;
			channel.incoming.Writer.TryWrite( [Connection.FlagRaw, 1] );
			channel.incoming.Writer.TryWrite( [Connection.FlagRaw, 2] );
			channel.InternalRecv( handler );
			if ( fakeLag > 0 )
			{
				Assert.AreEqual( 0, received.Count );
				System.Threading.Thread.Sleep( 300 );
				channel.InternalRecv( handler );
			}

			CollectionAssert.AreEqual( new byte[] { 1 }, received.ToArray() );
			channel.incoming.Writer.TryWrite( [Connection.FlagRaw, 3] );
			channel.InternalRecv( handler );
			CollectionAssert.AreEqual( new byte[] { 1 }, received.ToArray(), "Closed channels must not resume receiving" );
		}
		finally
		{
			Networking.FakeLag = previousFakeLag;
			if ( !closed ) channel.Close( 0, "Test complete" );
		}
	}

	[TestMethod]
	[DoNotParallelize]
	public async Task Tcp_ServerClient()
	{
		var server = new NetworkSystem( "server", tl );
		server.InitializeHost();
		server.AddSocket( new TcpSocket( "127.0.0.1", 55333 ) );
		var serverTable = InstallDataTables( server );

		Assert.AreEqual( serverTable.Entries.Count, 4 );

		var client = new NetworkSystem( "client", tl );
		client.Connect( new TcpChannel( "127.0.0.1", 55333 ) );
		var clientTable = InstallDataTables( client );
		Assert.AreEqual( clientTable.Entries.Count, 0 );

		Assert.IsTrue( client.IsClient );

		Connection.Local.State = Connection.ChannelState.Unconnected;

		for ( int i = 0; i < 500; i++ )
		{
			server.Tick();
			client.Tick();
			await Task.Delay( 20 );

			if ( client.Connection.State == Connection.ChannelState.Connected )
			{
				Console.WriteLine( "Client fully connected.." );
				break;
			}
		}

		// Full Connected needs the engine game loop to finish loading server information, which
		// never happens in this harness - the handshake parks at LoadingServerInformation. Assert
		// the handshake progressed and remember the state so the heartbeat phase can detect a drop.
		Assert.AreNotEqual( Connection.ChannelState.Unconnected, client.Connection.State, "Client handshake never progressed" );
		var stateAfterHandshake = client.Connection.State;

		Assert.AreEqual( clientTable.Entries.Count, 4 );

		// Stay connected for a few seconds, to test heartbeats
		for ( int i = 0; i < 100; i++ )
		{
			server.Tick();
			client.Tick();
			await Task.Delay( 20 );
		}

		// A dropped connection during the heartbeat window should fail the test
		Assert.AreEqual( stateAfterHandshake, client.Connection.State, "Client connection state changed while idling on heartbeats" );

		System.Console.WriteLine( "Disconnecting.." );
		client.Disconnect();

		System.Console.WriteLine( "Shutting down.." );

		server.Disconnect();
	}


}
