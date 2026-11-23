using Sandbox.Internal;
using Sandbox.Network;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkTests;

/// <summary>
/// A lobby that can't be made while the scene loads disconnects only once the load is over. The host joins at the load's
/// end, where a game spawns its player: disconnecting during the load left the game with no player at all.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LobbyFailureTest
{
	[TestMethod]
	public void LobbyFailingDuringALoadDisconnectsOnceTheLoadIsOver()
	{
		var previousSystem = Networking.System;
		var previousLocal = Connection.Local;
		try
		{
			var system = new NetworkSystem( "lobby-failed", new TypeLibrary() );
			Networking.System = system;
			Connection.Local = new LocalConnection( Guid.NewGuid() );
			var load = new TaskCompletionSource();

			var disconnect = Networking.DisconnectAfterLoad( load.Task, CancellationToken.None );
			Assert.IsFalse( disconnect.IsCompleted );
			Assert.AreSame( system, Networking.System, "The host must still be hosting when the load finishes, so it joins" );

			load.SetResult();
			Assert.IsTrue( disconnect.Wait( TimeSpan.FromSeconds( 10 ) ) );
			Assert.IsTrue( system.IsDisconnected );
			Assert.IsNull( Networking.System );
		}
		finally
		{
			Networking.System = previousSystem;
			Connection.Local = previousLocal;
		}
	}

	[TestMethod]
	public void LobbyFailingWithNoLoadDisconnectsAtOnce()
	{
		var previousSystem = Networking.System;
		var previousLocal = Connection.Local;
		try
		{
			var system = new NetworkSystem( "lobby-failed", new TypeLibrary() );
			Networking.System = system;
			Connection.Local = new LocalConnection( Guid.NewGuid() );

			var disconnect = Networking.DisconnectAfterLoad( null, CancellationToken.None );

			Assert.IsTrue( disconnect.IsCompleted );
			Assert.IsNull( Networking.System );
		}
		finally
		{
			Networking.System = previousSystem;
			Connection.Local = previousLocal;
		}
	}

	[TestMethod]
	public void LobbyReplacedDuringTheLoadIsNotDisconnected()
	{
		var previousSystem = Networking.System;
		var previousLocal = Connection.Local;
		try
		{
			var system = new NetworkSystem( "next-lobby", new TypeLibrary() );
			Networking.System = system;
			Connection.Local = new LocalConnection( Guid.NewGuid() );
			using var cancelled = new CancellationTokenSource();
			var load = new TaskCompletionSource();

			var disconnect = Networking.DisconnectAfterLoad( load.Task, cancelled.Token );
			cancelled.Cancel();
			load.SetException( new InvalidOperationException( "the load threw" ) );

			Assert.IsTrue( disconnect.Wait( TimeSpan.FromSeconds( 10 ) ) );
			Assert.IsFalse( system.IsDisconnected );
			Assert.AreSame( system, Networking.System );
		}
		finally
		{
			Networking.System = previousSystem;
			Connection.Local = previousLocal;
		}
	}
}
