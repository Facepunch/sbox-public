using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System;
using System.Reflection;
using System.Threading;

namespace NetworkTests;

[TestClass]
[DoNotParallelize]
public class ConnectionAttemptTest
{
	public class DisconnectRecorder : DispatchProxy
	{
		public Action<string> OnDisconnect { get; set; }

		protected override object Invoke( MethodInfo method, object[] args )
		{
			Assert.AreEqual( nameof( IGameInstanceDll.Disconnect ), method.Name );
			OnDisconnect( (string)args[0] );
			return null;
		}
	}

	[DataTestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void JoinFailureOnlyRequestsAModalWithoutACallerHandlingTheError( bool handlesFailure )
	{
		var previousGame = IGameInstanceDll.Current;
		try
		{
			const string failure = "Steam could not join the lobby: Full.";
			string reportedFailure = null;
			var disconnects = 0;
			var game = DispatchProxy.Create<IGameInstanceDll, DisconnectRecorder>();
			((DisconnectRecorder)game).OnDisconnect = message =>
			{
				disconnects++;
				Assert.AreEqual( handlesFailure ? failure : null, reportedFailure );
				Assert.AreEqual( handlesFailure ? null : failure, message );
			};
			IGameInstanceDll.Current = game;
			Action<string> onFailure = handlesFailure ? message => reportedFailure = message : null;

			var method = typeof( Networking ).GetMethod( "ReportConnectionFailure", BindingFlags.NonPublic | BindingFlags.Static );
			method.Invoke( null, [failure, onFailure] );

			Assert.AreEqual( 1, disconnects );
		}
		finally { IGameInstanceDll.Current = previousGame; }
	}

	[DataTestMethod]
	[DataRow( null )]
	[DataRow( "Kicked from server.\n\nReason: Removed by host" )]
	[DataRow( "Connection timed out" )]
	public void SocketCloseWhileUnconnectedPreservesFailureWithoutAnotherDisconnect( string previousFailure )
	{
		var previousLocal = Connection.Local;
		var previousGame = IGameInstanceDll.Current;
		try
		{
			Connection.Local = new LocalConnection( Guid.NewGuid() ) { State = Connection.ChannelState.Unconnected };
			// A second Disconnect would dereference this and fail the test.
			IGameInstanceDll.Current = null;
			var system = new NetworkSystem( "closed-attempt", new TypeLibrary() ) { FailureReason = previousFailure };

			system.OnServerDisconnection( 5003, "Connection closed" );
			system.OnServerDisconnection( 5004, "Late callback" );

			Assert.AreEqual( previousFailure ?? "The server connection closed.\nReason: Connection closed (code 5003).", system.FailureReason );
		}
		finally { Connection.Local = previousLocal; IGameInstanceDll.Current = previousGame; }
	}

	static Task<bool> WaitForConnection( CancellationToken token )
	{
		var method = typeof( Networking ).GetMethod( "AwaitSuccessfulConnection", BindingFlags.NonPublic | BindingFlags.Static );
		return (Task<bool>)method.Invoke( null, [token, 1d] );
	}

	[TestMethod]
	public async Task TeardownCancellationPreservesTheReportedFailure()
	{
		var previous = Networking.System;
		try
		{
			var system = new NetworkSystem( "failure-test", new TypeLibrary() ) { FailureReason = "Steam reason 5003" };
			Networking.System = system;
			using var cancellation = new CancellationTokenSource();
			cancellation.Cancel();
			Assert.IsFalse( await WaitForConnection( cancellation.Token ) );
			Assert.AreEqual( "Steam reason 5003", system.FailureReason );
		}
		finally { Networking.System = previous; }
	}

	[TestMethod]
	public async Task OldAttemptDoesNotAcceptANewConnectionsHandshake()
	{
		var previousSystem = Networking.System;
		var previousLocal = Connection.Local;
		try
		{
			Networking.System = new NetworkSystem( "old-attempt", new TypeLibrary() );
			Connection.Local = new LocalConnection( Guid.NewGuid() );
			var waiting = WaitForConnection( default );
			var replacement = new NetworkSystem( "new-attempt", new TypeLibrary() );
			Networking.System = replacement;
			Connection.Local = new LocalConnection( Guid.NewGuid() ) { State = Connection.ChannelState.LoadingServerInformation };
			Assert.IsFalse( await waiting );
			Assert.AreSame( replacement, Networking.System );
		}
		finally { Connection.Local = previousLocal; Networking.System = previousSystem; }
	}
}
