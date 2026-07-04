using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System;

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
