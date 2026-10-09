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
		/// quickplay, server, web, console, reload, game, benchmark, local); the rest is filled in
		/// when the menu knows which tile was used.
		/// </summary>
		public sealed record Origin( string Kind, string Ident = null, string Surface = null, string Shelf = null, int Position = -1, string List = null, string Via = null, string Referrer = null )
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
				if ( (Kind == "web" || Surface == "web") && NormalizeWebReferrer( Referrer ) is { } host ) d["referrer"] = host;
				return d;
			}
		}

		/// <summary>Accept only an external DNS hostname, never a URL or additional launch arguments.</summary>
		internal static string NormalizeWebReferrer( string value )
		{
			if ( string.IsNullOrEmpty( value ) || value.Length > 253 ) return null;
			var host = value.ToLowerInvariant().TrimEnd( '.' );
			if ( host.StartsWith( "www.", StringComparison.Ordinal ) ) host = host[4..];
			var parts = host.Split( '.' );
			if ( parts.Length < 2 || parts[^1].Length == 0 || parts[^1][0] is < 'a' or > 'z' ) return null;
			foreach ( var part in parts )
			{
				if ( part.Length is < 1 or > 63 || part[0] == '-' || part[^1] == '-' ) return null;
				if ( part.Any( c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') ) ) return null;
			}
			return host == "sbox.game" || host.EndsWith( ".sbox.game", StringComparison.Ordinal ) ? null : host;
		}

		/// <summary>
		/// A request older than this is from something the player gave up on. Joining a lobby can retry
		/// for a couple of minutes.
		/// </summary>
		const float RequestLifetime = 300;

		static readonly Lock loadLock = new();

		static Origin request;
		static RealTimeSince requestAge;

		static Load abandoned;
		static RealTimeSince abandonedAge;

		static Dictionary<string, object> completedLoad;
		static Origin completedOrigin;

		public static Load CurrentLoad { get; private set; }

		static FastTimer runTimer;
		static int runLoads;
		static int runGames;
		static float? runFirstGame;

		internal static void RunStarted() => runTimer = FastTimer.StartNew();

		/// <summary>
		/// Once, on the way out: how long s&amp;box was open and whether a game was ever played. A run
		/// with no game is someone who opened the menu and left. Lost if the process dies.
		/// </summary>
		internal static void ReportRun()
		{
			if ( Application.IsEditor || Application.IsDedicatedServer || Application.IsHeadless ) return;

			var e = new Events.EventRecord( "app.run" );
			e.SetValue( "s", (int)runTimer.ElapsedSeconds );
			e.SetValue( "loads", runLoads );
			e.SetValue( "games", runGames );
			if ( runFirstGame is { } first ) e.SetValue( "first_game_s", (int)first );
			e.Submit();
		}

		/// <summary>
		/// The player asked for a game. With <paramref name="replace"/> off, a generic route (a console
		/// command, a lobby join) doesn't overwrite a fresh request, unless that one was for another game.
		/// </summary>
		public static void GameRequested( Origin origin, bool replace = true )
		{
			lock ( loadLock )
			{
				if ( !replace && request is not null && requestAge < RequestLifetime &&
					(origin.Ident is null || request.Ident is null || SameGame( request.Ident, origin.Ident )) )
					return;

				request = origin;
				requestAge = 0;
			}
		}

		internal static Origin PendingRequest
		{
			get { lock ( loadLock ) return request; }
		}

		/// <summary>Clear only the request owned by the operation that failed, never a newer join.</summary>
		internal static void CancelRequest( Origin expected )
		{
			lock ( loadLock )
			{
				if ( ReferenceEquals( request, expected ) ) request = null;
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

				if ( request is not null && requestAge < RequestLifetime && (request.Ident is null || SameGame( request.Ident, ident )) )
				{
					origin = request;
					waited = requestAge;
				}
				else if ( remote && abandoned is { Remote: true } && abandonedAge < 10 && SameGame( abandoned.Ident, ident ) )
				{
					// Same attempt: the server restarted the handshake (lobby owner left, host migrated)
					origin = abandoned.Origin;
				}

				abandoned = null;

				request = null;

				runLoads++;
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
		/// Retained until a heartbeat carrying this exact load has been acknowledged.
		/// </summary>
		internal static (Dictionary<string, object> Load, Origin Origin) PeekCompletedLoad( string game )
		{
			lock ( loadLock )
			{
				if ( completedLoad is null || game is null )
					return default;

				if ( completedLoad.GetValueOrDefault( "ident" ) is string ident && !SameGame( ident, game ) )
					return default;

				return (completedLoad, completedOrigin);
			}
		}

		internal static void AcknowledgeCompletedLoad( Dictionary<string, object> load )
		{
			lock ( loadLock )
			{
				if ( !ReferenceEquals( completedLoad, load ) ) return;
				completedLoad = null;
				completedOrigin = null;
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
				runGames++;
				runFirstGame ??= (float)runTimer.ElapsedSeconds;

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
				abandoned = load;
				abandonedAge = 0;

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
