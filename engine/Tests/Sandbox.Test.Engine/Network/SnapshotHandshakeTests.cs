using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NetworkTests;

/// <summary>
/// Exercises the initial snapshot handler while scene loading is suspended.
/// </summary>
[TestClass, DoNotParallelize]
public class SnapshotHandshakeTests
{
	private sealed class LoadingGame( Task loading ) : GameNetworkSystem
	{
		/// <inheritdoc/>
		public override Task SetSnapshotAsync( SnapshotMsg data ) => loading;
	}

	private sealed class HostConnection() : EmptyConnection( Guid.NewGuid() )
	{
		internal readonly List<Guid> Ready = [];

		/// <summary>
		/// Dedicated client transports report the remote as host even after replacement.
		/// </summary>
		public override bool IsHost => true;

		internal override void InternalSend( NetPacket packet, NetFlags flags )
		{
			var reader = ByteStream.CreateReader( Decode( packet.Span ) );
			try
			{
				Assert.AreEqual( InternalMessageType.Packed, reader.Read<InternalMessageType>() );
				Ready.Add( System.TypeLibrary.FromBytes<ClientReady>( ref reader ).HandshakeId );
			}
			finally
			{
				reader.Dispose();
			}
		}
	}

	/// <summary>
	/// Only the handshake that started loading can send readiness or disconnect on failure.
	/// </summary>
	[DataTestMethod]
	[DataRow( "current", false )]
	[DataRow( "current", true )]
	[DataRow( "disconnected", false )]
	[DataRow( "disconnecting", false )]
	[DataRow( "reconnected", false )]
	[DataRow( "reconnected", true )]
	[DataRow( "host changed", false )]
	[DataRow( "restart requested", false )]
	[DataRow( "new handshake", false )]
	public async Task SnapshotCompletionBelongsToItsHandshake( string sessionChange, bool loadFails )
	{
		var previousSystem = Networking.System;
		var previousLocal = Connection.Local;
		var previousDll = IGameInstanceDll.Current;
		var previousTitle = LoadingScreen.Title;
		try
		{
			var loading = new TaskCompletionSource();
			var library = sessionChange == "disconnected" ? new TypeLibrary() : SharedTypeLibrary.ScriptingWithSystem;
			var system = new NetworkSystem( "snapshot", library ) { GameSystem = new LoadingGame( loading.Task ) };
			var host = new HostConnection();
			Networking.System = system;
			system.Connect( host );

			var handshake = Guid.NewGuid();
			Connection.Local = new LocalConnection( Guid.NewGuid() ) { HandshakeId = handshake, State = Connection.ChannelState.MountVPKs };
			var disconnects = new List<string>();
			var dll = DispatchProxy.Create<IGameInstanceDll, ConnectionAttemptTest.DisconnectRecorder>();
			((ConnectionAttemptTest.DisconnectRecorder)dll).OnDisconnect = disconnects.Add;
			IGameInstanceDll.Current = dll;

			var pending = system.On_Handshake_Snapshot( new InitialSnapshotResponse { HandshakeId = handshake }, host, default );
			Assert.IsFalse( pending.IsCompleted );
			Assert.AreEqual( Connection.ChannelState.Snapshot, Connection.Local.State );

			switch ( sessionChange )
			{
				case "disconnected":
					system.Disconnect();
					Networking.System = null;
					library.Dispose();
					break;
				case "disconnecting":
					system.IsDisconnecting = true;
					break;
				case "reconnected":
					// The replacement hasn't received its ServerInfo yet.
					Networking.System = new NetworkSystem( "replacement", library );
					break;
				case "host changed":
					system.SetHostConnection( new HostConnection() );
					break;
				case "restart requested":
					Connection.Local.State = Connection.ChannelState.Unconnected;
					break;
				case "new handshake":
					Connection.Local = new LocalConnection( Guid.NewGuid() ) { HandshakeId = Guid.NewGuid(), State = Connection.ChannelState.Snapshot };
					break;
			}

			if ( loadFails )
			{
				loading.SetException( new InvalidOperationException( "Snapshot load failed" ) );
			}
			else
			{
				loading.SetResult();
			}

			await pending;

			CollectionAssert.AreEqual( sessionChange == "current" && !loadFails ? new[] { handshake } : Array.Empty<Guid>(), host.Ready );
			CollectionAssert.AreEqual( sessionChange == "current" && loadFails ? new[] { "Error Deserializing Snapshot" } : Array.Empty<string>(), disconnects );
		}
		finally
		{
			LoadingScreen.Title = previousTitle;
			IGameInstanceDll.Current = previousDll;
			Connection.Local = previousLocal;
			Networking.System = previousSystem;
		}
	}
}
