using Sandbox.Engine;
using Sandbox.UI;
using System.Diagnostics;
using System.Threading;
using static Sandbox.Diagnostics.PerformanceStats;

namespace Sandbox;

internal static partial class Api
{
	public static partial class Activity
	{
		static int ActivityCount;
		static string[] lastAddons;
		static string sessionGame;
		static Dictionary<string, object> sessionLoad;
		static Origin sessionOrigin;
		static readonly Lock updateLock = new();
		static Task queuedUpdate = Task.CompletedTask;

		static SemaphoreSlim ActivityMutex = new SemaphoreSlim( 1, 1 );

		/// <summary>
		/// Written when a session opens and deleted when it closes. Still there on the next launch means
		/// the process died without closing it.
		/// </summary>
		const string OpenSessionFile = "activity_session.json";
		static bool checkedOpenSession;

		record class OpenSession( string Session, string Game, DateTime Started );

		static readonly Lock exitLock = new();
		static string exitReason;
		static string exitDetail;

		public static bool IsSessionActive => SessionId != Guid.Empty;

		public static float SessionSeconds => IsSessionActive ? (float)SessionTimer.ElapsedSeconds : 0.0f;
		public static FastTimer SessionTimer;

		/// <summary>Keep main-thread snapshots in order, including a close followed by a fast reload.</summary>
		internal static void QueueUpdate( string game, string gameVersion, string map, string[] addons, object net = null )
		{
			var (load, origin) = PeekCompletedLoad( game );
			lock ( updateLock )
			{
				queuedUpdate = queuedUpdate.ContinueWith( _ => UpdateActivity( game, gameVersion, map, addons, net, load, origin ), TaskScheduler.Default ).Unwrap();
			}
		}

		/// <summary>
		/// Heartbeat for the current game. <paramref name="net"/> is the network state sampled on the
		/// main thread: mode, players, max.
		/// </summary>
		static async Task UpdateActivity( string game, string gameVersion, string map, string[] addons, object net, Dictionary<string, object> load, Origin origin )
		{
			try
			{
				await ActivityMutex.WaitAsync();
				var performanceData = Performance.Flip();

				if ( Application.IsEditor ) return;

				if ( Application.IsDedicatedServer )
				{
					await UpdateDedicatedServerActivity( game, gameVersion, map, addons, performanceData );
					return;
				}

				ReportUncleanSession();
				game = game?.Trim().ToLowerInvariant();
				bool newSessionHash = !string.Equals( game, sessionGame, StringComparison.Ordinal );

				//
				// Start new session hash if not set
				//
				if ( newSessionHash )
				{
					await CloseActivity( performanceData );
					sessionGame = game;

					if ( game == null || game.Contains( "#local" ) || game.StartsWith( "local." ) )
						return;

					//Log.Info( $"New Session Started! ({game}/{map})" );

					SessionId = Guid.NewGuid();

					NativeErrorReporter.SetTag( "activity_session_id", SessionId.ToString() );

					SessionTimer = FastTimer.StartNew();
					ActivityCount = 0;
					performanceData = null;

					SetExitReason( null );
					WriteOpenSession( game );
				}

				lastAddons = addons;
				if ( load is not null )
				{
					sessionLoad = load;
					sessionOrigin = origin;
				}

				// Something went wrong
				if ( !IsSessionActive )
					return;

				if ( game != null ) game = game.Trim().ToLower();

				var data = new Dictionary<string, object>();
				data.Add( "game", game );
				data.Add( "gameversion", gameVersion );
				data.Add( "map", map );
				data.Add( "content", lastAddons );
				data.Add( "st", SessionSeconds.FloorToInt() );
				data.Add( "sh", SessionId.ToString() );
				data.Add( "performance", performanceData );
				data.Add( "config", GetConfig() );
				data.Add( "hardware", Engine.SystemInfo.AsObject() );
				data.Add( "i", ActivityCount++ );
				data.Add( "net", net );

				if ( sessionLoad is not null ) data.Add( "load", sessionLoad );
				if ( sessionOrigin is not null ) data.Add( "origin", sessionOrigin.ToData() );

				if ( newSessionHash )
					data.Add( "open", 1 );

				if ( Sandbox.Backend.Account is not { } account ) return;
				await account.Activity( data );
				AcknowledgeCompletedLoad( sessionLoad );
				sessionLoad = null;
				sessionOrigin = null;

			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
			finally
			{
				ActivityMutex.Release();
			}
		}

		private static Task UpdateDedicatedServerActivity( string game, string gameVersion, string map, string[] addons, object performanceData )
		{
			var e = new Api.Events.EventRecord( "DedicatedStatus" );

			e.SetValue( "Machine", Environment.MachineName );
			e.SetValue( "System", new { Hardware = SystemInfo.AsObject(), Config = Sandbox.Api.GetConfig() } );
			e.SetValue( "Game", game );
			e.SetValue( "Map", map );
			e.SetValue( "Clients", Connection.All.Count() );
			e.SetValue( "Addons", addons );
			e.SetValue( "Hostname", Networking.ServerName );

			e.SetValue( "Uptime", RealTime.Now );
			e.SetValue( "ApproximateProcessMemoryUsage", PerformanceStats.ApproximateProcessMemoryUsage );

			e.SetValue( "Timings", Timings.All.ToDictionary( x => x.Key, x => x.Value.AverageMs( int.MaxValue ) ) );
			e.SetValue( "PerformanceData", performanceData );

			e.Submit();

			return Task.CompletedTask;
		}

		internal static async Task Shutdown()
		{
			Task pending;
			lock ( updateLock ) pending = queuedUpdate;
			await pending.ConfigureAwait( false );
			await ActivityMutex.WaitAsync().ConfigureAwait( false );
			try
			{
				if ( !IsSessionActive ) return;
				await CloseActivity( Performance.Flip() ).ConfigureAwait( false );
			}
			finally { ActivityMutex.Release(); }
		}

		static async Task CloseActivity( object performanceData )
		{
			if ( !IsSessionActive ) return;

			// wait for the stats to flush first - we need the session hash!
			await Stats.ForceFlushAsync().ConfigureAwait( false );

			var data = new Dictionary<string, object>();
			data.Add( "game", "" );
			data.Add( "st", SessionSeconds.FloorToInt() );
			data.Add( "sh", SessionId.ToString() );
			data.Add( "i", ActivityCount++ );
			data.Add( "content", lastAddons );
			data.Add( "performance", performanceData );
			data.Add( "config", GetConfig() );
			data.Add( "hardware", Engine.SystemInfo.AsObject() );
			data.Add( "close", 1 );
			data.Add( "exit", TakeExitReason() );
			if ( sessionLoad is not null ) data.Add( "load", sessionLoad );
			if ( sessionOrigin is not null ) data.Add( "origin", sessionOrigin.ToData() );

			try
			{
				if ( Sandbox.Backend.Account is { } account )
				{
					await account.Activity( data ).ConfigureAwait( false );
					AcknowledgeCompletedLoad( sessionLoad );
				}
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when closing activity {e.Message}" );
			}

			SessionId = Guid.Empty;
			SessionTimer = default;
			ActivityCount = -1;
			lastAddons = null;
			sessionLoad = null;
			sessionOrigin = null;

			try { EngineFileSystem.Config.DeleteFile( OpenSessionFile ); } catch { }
		}

		/// <summary>
		/// Why the current session is ending, sent with the close: menu, leave, disconnect, kicked, quit
		/// or crash. The first reason wins - a kick calls disconnect, which closes the game. Null clears it.
		/// </summary>
		public static void SetExitReason( string reason, string detail = null )
		{
			lock ( exitLock )
			{
				if ( reason is null )
				{
					exitReason = null;
					exitDetail = null;
					return;
				}

				if ( !IsSessionActive || exitReason is not null ) return;

				exitReason = reason;
				exitDetail = detail?.Replace( "\n", " " ).Trim();
				if ( exitDetail?.Length > 200 ) exitDetail = exitDetail[..200];
			}
		}

		static object TakeExitReason()
		{
			lock ( exitLock )
			{
				var exit = exitReason is null ? null : new Dictionary<string, object> { ["reason"] = exitReason, ["detail"] = exitDetail };
				exitReason = null;
				exitDetail = null;
				return exit;
			}
		}

		static void WriteOpenSession( string game )
		{
			try { EngineFileSystem.Config.WriteJson( OpenSessionFile, new OpenSession( SessionId.ToString(), game, DateTime.UtcNow ) ); }
			catch ( System.Exception e ) { Log.Warning( e, $"Couldn't write {OpenSessionFile}" ); }
		}

		/// <summary>
		/// Once per launch: if the last run left a session open, it ended without a close - a crash,
		/// a kill, or a power cut. Reported against that session so it can be matched to its heartbeats.
		/// </summary>
		static void ReportUncleanSession()
		{
			if ( checkedOpenSession ) return;
			checkedOpenSession = true;

			try
			{
				if ( !EngineFileSystem.Config.FileExists( OpenSessionFile ) ) return;

				var open = EngineFileSystem.Config.ReadJsonOrDefault<OpenSession>( OpenSessionFile );
				EngineFileSystem.Config.DeleteFile( OpenSessionFile );

				if ( open?.Session is null ) return;

				var e = new Events.EventRecord( "session.unclean" );
				e.SetValue( "sh", open.Session );
				e.SetValue( "game", open.Game );
				e.SetValue( "started", open.Started );
				e.Submit();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Couldn't read {OpenSessionFile}" );
			}
		}
	}
}
