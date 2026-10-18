using Sandbox.Network;

namespace NetSim;

/// <summary>
/// Players that keep moving must keep moving for everyone watching, through joins, outages and hitches.
/// </summary>
[TestClass]
public class DesyncTests
{
	[TestInitialize]
	public void Init() => ThreadSafe.MarkMainThread();

	sealed class Player
	{
		public SimPeer Owner;
		public Guid Id;
		public float Phase;
	}

	/// <summary>
	/// Moves every player in a circle each frame and tracks how long each observer last saw each one move.
	/// </summary>
	sealed class Game : IDisposable
	{
		public readonly NetSimulation Sim;
		public readonly List<Player> Players = new();
		readonly Dictionary<(SimPeer Observer, Guid Id), (Vector3 Position, int Frame)> _lastMove = new();
		public readonly Dictionary<(SimPeer Observer, Guid Id), int> Worst = new();
		public bool HostSpawnsPlayers;

		public Game( NetSimSettings settings ) => Sim = new NetSimulation( settings );

		public SimPeer Join()
		{
			var client = Sim.Join();
			AddPlayer( client );
			return client;
		}

		public void AddPlayer( SimPeer client )
		{
			var player = new Player { Owner = client, Phase = Players.Count };
			Players.Add( player );

			// Like a game's network helper: the host spawns the player and hands it over
			var spawner = HostSpawnsPlayers ? Sim.Host : client;
			Sim.As( spawner, () =>
			{
				var go = new GameObject( true, $"player {client.Name}" );
				player.Id = go.Id;
				go.WorldPosition = Position( player );
				go.Components.Create<SimPlayer>();
				go.Network.Interpolation = true;
				go.NetworkSpawn( HostSpawnsPlayers ? Networking.System.FindConnection( client.Local.Id ) : Connection.Local );
			} );
		}

		Vector3 Position( Player p )
		{
			var t = Sim.Now + p.Phase;
			return new Vector3( (float)Math.Cos( t ) * 300 + p.Phase * 1000, (float)Math.Sin( t ) * 300, 0 );
		}

		public void Run( float seconds, Action<int> beforeFrame = null )
		{
			var frames = (int)(seconds * Sim.Settings.FrameRate);
			for ( var i = 0; i < frames; i++ )
			{
				beforeFrame?.Invoke( i );

				foreach ( var p in Players )
				{
					if ( p.Owner.Paused ) continue;
					Sim.As( p.Owner, () =>
					{
						var go = Sandbox.Game.ActiveScene.Directory.FindByGuid( p.Id );
						if ( go.IsValid() && go.Network.IsOwner ) go.WorldPosition = Position( p );
					} );
				}

				Sim.Step();
				Observe();
			}
		}

		void Observe()
		{
			foreach ( var observer in Sim.Peers )
			{
				if ( observer.Paused || !observer.IsConnected ) continue;

				Sim.As( observer, () =>
				{
					foreach ( var p in Players )
					{
						if ( p.Owner == observer ) continue;

						var go = Sandbox.Game.ActiveScene.Directory.FindByGuid( p.Id );
						var key = (observer, p.Id);
						var found = _lastMove.TryGetValue( key, out var last );

						// Missing counts as not moving
						if ( !go.IsValid() )
						{
							if ( !found ) _lastMove[key] = (new Vector3( float.NaN ), Sim.Frame);
							else UpdateWorst( key, last.Frame );
							continue;
						}

						var position = go.WorldPosition;
						if ( !found || float.IsNaN( last.Position.x ) || last.Position.Distance( position ) > 0.01f )
						{
							_lastMove[key] = (position, Sim.Frame);
							continue;
						}

						UpdateWorst( key, last.Frame );
					}
				} );
			}
		}

		void UpdateWorst( (SimPeer, Guid) key, int since )
		{
			var stuck = Sim.Frame - since;
			if ( !Worst.TryGetValue( key, out var worst ) || stuck > worst )
				Worst[key] = stuck;
		}

		public void ResetWorst() => Worst.Clear();

		/// <summary>
		/// Observer/player pairs that went without moving for longer than this.
		/// </summary>
		public List<string> Stuck( float seconds )
		{
			var limit = (int)(seconds * Sim.Settings.FrameRate);
			var result = new List<string>();

			foreach ( var ((observer, id), frames) in Worst.OrderByDescending( x => x.Value ) )
			{
				if ( frames <= limit ) continue;

				var player = Players.First( p => p.Id == id );
				var now = Sim.Frame - _lastMove[(observer, id)].Frame;
				result.Add( $"{observer} saw {player.Owner}'s player still for {frames / Sim.Settings.FrameRate:0.00}s{(now > limit ? $" (still stuck, {DescribeView( observer, player )})" : "")}" );
			}

			return result;
		}

		string DescribeView( SimPeer observer, Player p )
		{
			return Sim.As( observer, () =>
			{
				var go = Sandbox.Game.ActiveScene.Directory.FindByGuid( p.Id );
				if ( !go.IsValid() ) return "missing";
				return $"owner {go.Network.OwnerId} (real {p.Owner.Local.Id}) v{go.Network.SnapshotVersion}";
			} );
		}

		public void Dispose() => Sim.Dispose();
	}

	static NetSimSettings Lobby( float loss = 0.02f ) => new() { Mesh = true, Link = new SimLinkSettings { LatencySeconds = 0.05f, Loss = loss, JitterSeconds = 0.01f } };
	static NetSimSettings Dedicated( float loss = 0.02f ) => Lobby( loss ) with { Mesh = false };

	static void AssertNoneStuck( Game game, float seconds = 0.5f )
	{
		var stuck = game.Stuck( seconds );
		Assert.AreEqual( 0, stuck.Count, string.Join( "\n", stuck.Take( 20 ) ) );
	}

	[TestMethod]
	[DataRow( true, false )]
	[DataRow( true, true )]
	[DataRow( false, false )]
	[DataRow( false, true )]
	public void LateJoinersSeeEveryoneMove( bool mesh, bool hostSpawns )
	{
		using var game = new Game( mesh ? Lobby() : Dedicated() ) { HostSpawnsPlayers = hostSpawns };

		for ( var i = 0; i < 4; i++ ) game.Join();
		game.Run( 5 );

		game.Join();
		game.Run( 3 );
		game.Join();
		game.Run( 2 );
		game.ResetWorst();
		game.Run( 5 );

		AssertNoneStuck( game );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void JoinsAtTheSameTime( bool mesh )
	{
		using var game = new Game( mesh ? Lobby() : Dedicated() ) { HostSpawnsPlayers = true };

		for ( var i = 0; i < 3; i++ ) game.Join();
		game.Run( 3 );

		var a = game.Sim.AddClient();
		var b = game.Sim.AddClient();
		var c = game.Sim.AddClient();
		var joining = new[] { a, b, c };

		for ( var i = 0; i < 600 && joining.Any( j => !j.IsConnected ); i++ )
		{
			game.Run( 1f / 60 );
			foreach ( var j in joining )
			{
				if ( j.IsConnected && j.JoinedFrame == 0 )
				{
					j.JoinedFrame = game.Sim.Frame;
					if ( mesh )
						foreach ( var other in game.Sim.Clients.Where( o => o != j && o.IsConnected && (o.JoinedFrame < j.JoinedFrame || !joining.Contains( o )) ) )
							game.Sim.LinkPeers( j, other );
					game.AddPlayer( j );
				}
			}
		}

		game.Run( 3 );
		game.ResetWorst();
		game.Run( 5 );

		AssertNoneStuck( game );
	}

	[TestMethod]
	[DataRow( 1f )]
	[DataRow( 5f )]
	[DataRow( 20f )]
	public void DirectLinkOutage( float seconds )
	{
		using var game = new Game( Lobby() ) { HostSpawnsPlayers = true };

		var a = game.Join();
		var b = game.Join();
		game.Join();
		game.Run( 3 );

		var until = game.Sim.Now + seconds;
		game.Sim.Transport.DownUntil = c => (c.Owner == a && c.RemotePeer == b) || (c.Owner == b && c.RemotePeer == a) ? until : 0;
		game.Run( seconds );
		game.Run( 2 );
		game.ResetWorst();
		game.Run( 5 );

		AssertNoneStuck( game );
	}

	[TestMethod]
	[DataRow( "client", 1f )]
	[DataRow( "client", 5f )]
	[DataRow( "host", 1f )]
	[DataRow( "host", 5f )]
	public void Hitch( string who, float seconds )
	{
		using var game = new Game( Lobby() ) { HostSpawnsPlayers = true };

		for ( var i = 0; i < 4; i++ ) game.Join();
		game.Run( 3 );

		var peer = who == "host" ? game.Sim.Host : game.Sim.Clients[1];
		peer.Paused = true;
		game.Run( seconds );
		peer.Paused = false;
		game.Run( 2 );
		game.ResetWorst();
		game.Run( 5 );

		AssertNoneStuck( game );
	}

	[TestMethod]
	public void HeavyLoss()
	{
		using var game = new Game( Lobby( 0.2f ) ) { HostSpawnsPlayers = true };

		for ( var i = 0; i < 4; i++ ) game.Join();
		game.Run( 5 );
		game.Join();
		game.Run( 5 );
		game.Sim.Transport.Settings = game.Sim.Transport.Settings with { Loss = 0.02f };
		game.Run( 2 );
		game.ResetWorst();
		game.Run( 5 );

		AssertNoneStuck( game );
	}
}

