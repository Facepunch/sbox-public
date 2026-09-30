using System.Threading;

namespace Sandbox;

internal static partial class Api
{
	// Each attempt to get into a game, from the player asking for it until they're playing: what
	// asked for it, how long each loading stage took, and whether it made it. Every attempt is
	// submitted as a game.load event; a successful one also rides on the next heartbeat so load
	// times and origins can be tied to whether the player came back.
	public static partial class Activity
	{
		/// <summary>
		/// What asked for the game. <see cref="Kind"/> is the broad route (menu, friend, invite, party,
		/// quickplay, server, web, console); the rest is filled in when the menu knows which tile was used.
		/// </summary>
		public sealed record Origin( string Kind, string Ident = null, string Surface = null, string Shelf = null, int Position = -1, string List = null, string Via = null )
		{
			public Dictionary<string, object> ToData()
			{
				var d = new Dictionary<string, object> { ["kind"] = Kind };
				if ( Ident is not null ) d["ident"] = Ident;
				if ( Surface is not null ) d["surface"] = Surface;
				if ( Shelf is not null ) d["shelf"] = Shelf;
				if ( Position >= 0 ) d["pos"] = Position;
				if ( List is not null ) d["list"] = List;
				if ( Via is not null ) d["via"] = Via;
				return d;
			}
		}

		/// <summary>
		/// A request older than this is from something the player gave up on.
		/// </summary>
		const float RequestLifetime = 120;

		static readonly Lock loadLock = new();

		static Origin request;
		static RealTimeSince requestAge;

		static Dictionary<string, object> completedLoad;
		static Origin completedOrigin;

		public static Load CurrentLoad { get; private set; }

		/// <summary>
		/// The player asked for a game. With <paramref name="replace"/> off, a generic route (a console
		/// command) doesn't overwrite what the menu already said.
		/// </summary>
		public static void GameRequested( Origin origin, bool replace = true )
		{
			lock ( loadLock )
			{
				if ( !replace && request is not null && requestAge < RequestLifetime )
					return;

				request = origin;
				requestAge = 0;
			}
		}

		/// <summary>
		/// A game package started loading. <paramref name="remote"/> is a join to someone else's server.
		/// </summary>
		public static Load LoadBegin( string ident, bool remote )
		{
			lock ( loadLock )
			{
				CurrentLoad?.End( "superseded" );

				var waited = 0f;
				Origin origin = null;

				if ( request is not null && requestAge < RequestLifetime )
				{
					origin = request;
					waited = requestAge;
				}

				request = null;

				CurrentLoad = new Load( ident, remote, origin, waited );
				return CurrentLoad;
			}
		}

		/// <summary>
		/// Stage marker for whatever load is running.
		/// </summary>
		public static void LoadStage( string name ) => CurrentLoad?.Stage( name );

		/// <summary>
		/// The current load reached the game.
		/// </summary>
		public static void LoadFinished() => CurrentLoad?.End( "success" );

		/// <summary>
		/// The current load stopped before reaching the game. A null reason means the player cancelled.
		/// </summary>
		public static void LoadAbandoned( string reason ) => CurrentLoad?.End( reason is null ? "cancel" : "fail", reason );

		/// <summary>
		/// Taken once by the first heartbeat after a successful load of this game.
		/// </summary>
		internal static (Dictionary<string, object> Load, Origin Origin) TakeCompletedLoad( string game )
		{
			lock ( loadLock )
			{
				if ( completedLoad is null || game is null )
					return default;

				if ( completedLoad.GetValueOrDefault( "ident" ) is string ident && !SameGame( ident, game ) )
					return default;

				var taken = (completedLoad, completedOrigin);
				completedLoad = null;
				completedOrigin = null;
				return taken;
			}
		}

		static bool SameGame( string a, string b )
		{
			static string Strip( string s ) => s.Split( '#' )[0].Trim();
			return string.Equals( Strip( a ), Strip( b ), StringComparison.OrdinalIgnoreCase );
		}

		static void LoadCompleted( Load load, Dictionary<string, object> data )
		{
			lock ( loadLock )
			{
				completedLoad = data;
				completedOrigin = load.Origin;

				if ( ReferenceEquals( CurrentLoad, load ) )
					CurrentLoad = null;
			}
		}

		static void LoadDiscarded( Load load )
		{
			lock ( loadLock )
			{
				if ( ReferenceEquals( CurrentLoad, load ) )
					CurrentLoad = null;
			}
		}

		internal sealed class Load
		{
			public string Ident { get; }
			public bool Remote { get; }
			public Origin Origin { get; }
			public bool Ended { get; private set; }

			readonly float _waited;
			readonly FastTimer _timer = FastTimer.StartNew();
			readonly Dictionary<string, int> _stages = new();
			string _stage = "start";
			FastTimer _stageTimer = FastTimer.StartNew();

			long _bytes;
			int _files;
			int _downloads;
			int _cachedDownloads;
			double _downloadSeconds;

			public Load( string ident, bool remote, Origin origin, float waited )
			{
				Ident = ident;
				Remote = remote;
				Origin = origin;
				_waited = waited;
			}

			public void Stage( string name )
			{
				if ( Ended || name == _stage ) return;

				CloseStage();
				_stage = name;
				_stageTimer = FastTimer.StartNew();
			}

			void CloseStage()
			{
				_stages[_stage] = _stages.GetValueOrDefault( _stage ) + (int)_stageTimer.ElapsedMilliSeconds;
			}

			/// <summary>
			/// A package download finished. Zero bytes means every file was already cached.
			/// </summary>
			public void Downloaded( long bytes, int files, double seconds )
			{
				if ( Ended ) return;

				Interlocked.Add( ref _bytes, bytes );
				Interlocked.Add( ref _files, files );
				Interlocked.Increment( ref _downloads );
				if ( files == 0 ) Interlocked.Increment( ref _cachedDownloads );

				lock ( _stages ) _downloadSeconds += seconds;
			}

			public void End( string outcome, string reason = null )
			{
				if ( Ended ) return;
				Ended = true;

				CloseStage();

				var data = new Dictionary<string, object>
				{
					["ident"] = Ident,
					["mode"] = Remote ? "join" : "host",
					["outcome"] = outcome,
					["stage"] = _stage,
					["ms"] = (int)_timer.ElapsedMilliSeconds,

					// From the request to the load starting: matchmaking, connecting, or the player
					// reading a create-game dialog. Kept out of ms so it doesn't read as load time.
					["wait_ms"] = (int)(_waited * 1000),
					["stages"] = _stages,
					["bytes"] = _bytes,
					["files"] = _files,
					["downloads"] = _downloads,
					["cached"] = _cachedDownloads,
					["download_s"] = Math.Round( _downloadSeconds, 2 ),
				};

				if ( reason is not null ) data["reason"] = reason.Length > 200 ? reason[..200] : reason;
				if ( Origin is not null ) data["origin"] = Origin.ToData();

				var e = new Events.EventRecord( "game.load" );
				foreach ( var (k, v) in data ) e.SetValue( k, v );
				e.Submit();

				Log.Info( $"game.load {outcome} {Ident} {data["ms"]}ms [{string.Join( ", ", _stages.Select( x => $"{x.Key}={x.Value}" ) )}]" );

				if ( outcome == "success" )
					LoadCompleted( this, data );
				else
					LoadDiscarded( this );
			}
		}
	}
}
